#if WINDOWS

using Microsoft.Win32;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OlympUI;
using Olympus.External;
using SDL2;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Olympus.NativeImpls {
    [SuppressUnmanagedCodeSecurity]
    public unsafe partial class NativeWin32 : NativeImpl {

        static readonly IntPtr NULL = (IntPtr) 0;
        static readonly IntPtr ONE = (IntPtr) 1;

        static readonly IntPtr InvisibleRegion = CreateRectRgn(0, 0, -1, -1);

        // Windows 10 1809 introduced acrylic blur.
        static readonly bool SystemHasAcrylic = Environment.OSVersion.Version >= new Version(10, 0, 17763, 0);
        // Windows 10 1903 introduced PreferredAppMode.
        static readonly bool SystemHasPreferredAppMode = SystemHasAcrylic && Environment.OSVersion.Version >= new Version(10, 0, 18362, 0);
        // Windows 11 ???? introduced host backdrop acrylic blur. Needs to be revisited in the future - currently using 22557 as a number as that's the insider build this kinda became mandatory with, but 22557 is buggy in itself.
        static readonly bool SystemHasHostBackdropAcrylic = SystemHasAcrylic && Environment.OSVersion.Version >= new Version(10, 0, 22557, 0);
        // Windows 11 (22000) is (going to be) the first reliable non-insider build with (almost) fixed acrylic.
        // Apparently self-acrylic is still better than having an acrylic background window though.
        static readonly bool SetAcrylicOnSelf = SystemHasAcrylic && true; // (Environment.GetEnvironmentVariable("OLYMPUS_WIN32_SELFACRYLIC") == "1" || Environment.OSVersion.Version >= new Version(10, 0, 22000, 0));
        static readonly bool SystemHasAcrylicFixes = SystemHasAcrylic && Environment.OSVersion.Version >= new Version(10, 0, 22000, 0);
        // Windows 11 clips the window for us, Windows 10 doesn't. It's as simple as that... right? No!
        // There once was ClipSelf for testing but it just misbehaved greatly.
        static readonly bool ClipBackgroundAcrylicChild = false; // Could be explored further.
        static readonly bool ExtendedBorderedWindow = !SystemHasAcrylicFixes;
        static readonly bool ExtendedBorderedWindowResizeOutside = false; // TODO
        static readonly bool LegacyBorderedWindow = ExtendedBorderedWindow;
        static readonly bool SetAcrylicOnSelfMaximized = SystemHasHostBackdropAcrylic; // TODO

        // Some Windows build after 1809 made moving acrylic windows laggy.
        // Windows 11 introduces Mica for certain Win32 applications, which also suffer from the same lag.
        // Microsoft, please fix this. In the meantime, we can work around it, but why should we if Microsoft doesn't?
        // Update: Turns out some 22H2 insider build fixed it, finally?
        // FIXME: Make sure to keep this enabled for old versions of Windows, f.e. Win10!
        static readonly bool FlickerAcrylicOnSelfMove = false;

        static bool IsOpenGL => FNAHooks.FNA3DDriver?.Equals("opengl", StringComparison.InvariantCultureIgnoreCase) ?? false;
        static bool IsVulkan => FNAHooks.FNA3DDriver?.Equals("vulkan", StringComparison.InvariantCultureIgnoreCase) ?? false;

        private IntPtr HWnd;
        private IntPtr HDc;

        private IntPtr WndProcPrevPtr;
        private WndProcDelegate WndProcCurrent;
        private IntPtr WndProcCurrentPtr;
        private IntPtr DefWindowProcW;

        private RECT MaximizedOffset;

        internal bool WorkaroundDWMMaximizedTitleBarSize;

        internal int OffsetLeft = 0;
        internal int OffsetTop = 0;
        // TODO: DWMWA_CAPTION_BUTTON_BOUNDS returns something close-ish but not fully correct.
        internal int WindowControlsWidth => _IsMaximized ? WorkaroundDWMMaximizedTitleBarSize ? 141 : 142 : 138;
        internal int WindowControlsHeight => _IsMaximized ? WorkaroundDWMMaximizedTitleBarSize ? OffsetTop + 1 : OffsetTop + 8 : OffsetTop;

        private bool? _IsTransparentPreferred;
        private bool IsTransparentPreferred => _IsTransparentPreferred ??= (SystemHasAcrylic && Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", null) as int? != 0);
        private bool IsTransparent;
        private Microsoft.Xna.Framework.Color LastBackgroundColor;
        private float LastBackgroundBlur;

        private bool IsNclButtonDown;
        private HitTestValues IsNclButtonDownValue;
        private bool IsNclButtonDownAndMoving;
        private bool IsNclButtonDownAndMovingFooled;

        private bool WindowIdleForceRedraw;

        private bool Initialized = false;
        private bool Ready = false;
        private Bitmap SplashMain;
        private Bitmap SplashWheel;
        private Bitmap? SplashCanvas;
        private IntPtr SplashCanvasHBitmap;
        private IntPtr SplashCanvasHDC;
        private bool SplashDone;
        private Win10BackgroundForm? BackgroundAcrylicChild;
        private Win10BackgroundForm? BackgroundResizeChild;
        private Thread? BackgroundResizeChildThread;

        private RECT LastWindowRect;
        private RECT LastClientRect;
        private TimeSpan LastTickStart;
        private TimeSpan LastTickEnd;
        private volatile bool ManuallyBlinking;

        private Thread? BGThread;
        private bool BGThreadRedraw;
        private bool BGThreadRedrawSkip;

        private WrappedGraphicsDeviceManager? WrappedGDM;

        private Thread? InitThread;
        private int InitPaint;
        private Stopwatch InitStopwatch = new();

        private Display[]? _Displays;
        // FIXME: Display hotplugging!
        private Display[] Displays {
            get {
                if (_Displays is not null)
                    return _Displays;

                List<Display> displays = new();

                // FIXME: Figure out / influence what adapter is being used by FNA ahead of time!
                // FIXME: Figure out / influence what adapter is being used by Windows ahead of time!

                // The first adapter in the list is what Windows wants us to use (as per Windows settings overrides).
                // If Windows forces us to use a non-main adapter, it also fakes all outputs to belong to that adapter (luckily no flicker).
                bool isMain = true;

                using SharpDX.DXGI.Factory1 factory = new();
                foreach (SharpDX.DXGI.Adapter1 adapterRaw in factory.Adapters1) {
                    SharpDX.DXGI.AdapterDescription1 adapterInfo = adapterRaw.Description1;
                    Adapter adapter = new(isMain);
                    isMain = false;
                    foreach (SharpDX.DXGI.Output displayRaw in adapterRaw.Outputs) {
                        SharpDX.DXGI.OutputDescription displayInfo = displayRaw.Description;
                        SharpDX.Mathematics.Interop.RawRectangle bounds = displayInfo.DesktopBounds;
                        displays.Add(new(adapter, new(
                            bounds.Left, bounds.Top,
                            bounds.Right - bounds.Left, bounds.Bottom - bounds.Top
                        )));
                    }
                }

                return _Displays = displays.ToArray();
            }
        }

        private Display? CurrentDisplay {
            get {
                RECT rect = LastWindowRect;
                Microsoft.Xna.Framework.Point center = new(
                    rect.Left + (rect.Right - rect.Left) / 2,
                    rect.Top + (rect.Bottom - rect.Top) / 2
                );

                foreach (Display display in Displays)
                    if (display.Bounds.Contains(center))
                        return display;

                return null;
            }
        }

        public override bool CanRenderTransparentBackground => SystemHasAcrylic && (ClientSideDecoration == ClientSideDecorationMode.Title || SetAcrylicOnSelfMaximized || !_IsMaximized);

        public override bool IsActive {
            get {
                IntPtr active = GetActiveWindow();
                IntPtr foreground = GetForegroundWindow();
                if (active == HWnd && foreground == HWnd)
                    return true;
                // Resize child can't ever be active, but it can be in the foreground.
                IntPtr child = BackgroundResizeChild?.FriendlyHandle ?? NULL;
                if (child != NULL && foreground == child)
                    return true;
                return false;
            }
        }

        private bool _IsMaximized;
        public override bool IsMaximized => _IsMaximized;
        public override Microsoft.Xna.Framework.Point WindowPosition {
            get {
                Microsoft.Xna.Framework.Point pos = new();
                SDL.SDL_GetWindowPosition(App.Window.Handle, out pos.X, out pos.Y);
                return pos;
            }
            set => SDL.SDL_SetWindowPosition(App.Window.Handle, value.X, value.Y);
        }

        public Microsoft.Xna.Framework.Point WindowSize => new Microsoft.Xna.Framework.Point(
            App.Graphics.PreferredBackBufferWidth + (LastWindowRect.Right - LastWindowRect.Left) - (LastClientRect.Right - LastClientRect.Left),
            App.Graphics.PreferredBackBufferHeight + (LastWindowRect.Bottom - LastWindowRect.Top) - (LastClientRect.Bottom - LastClientRect.Top)
        );

        private bool? _DarkModePreferred;
        public override bool? DarkModePreferred => _DarkModePreferred ??= (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", null) as int? == 0);
        private bool _DarkMode;
        public override bool DarkMode {
            get => _DarkMode;
            set {
                if (_DarkMode == value)
                    return;
                _DarkMode = value;

                if (Ready)
                    SetBackgroundBlur(null, true);

                // No immediate visible change.
                SetWindowTheme(HWnd, value ? "DarkMode_Explorer" : "Explorer", null);

                // Works but breaks opaque background.
#if true
                int dark = value ? 1 : 0;
                WindowCompositionAttributeData data = new() {
                    Attribute = WindowCompositionAttribute.WCA_USEDARKMODECOLORS,
                    Data = (IntPtr) (&dark),
                    SizeOfData = sizeof(int),
                };
                SetWindowCompositionAttribute(HWnd, ref data);
#else
                // Equivalent to above, but using a publicly documented method for 22000+.
                int dark = value ? 1 : 0;
                DwmSetWindowAttribute(HWnd, DwmWindowAttribute.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
#endif

                if (SystemHasAcrylic) {
                    // No immediate visible change.
                    AllowDarkModeForWindow?.Invoke(HWnd, value);
                    // Changes the context menu when right-clicking on the titlebar.
                    if (SystemHasPreferredAppMode) {
                        SetPreferredAppMode?.Invoke(value ? PreferredAppMode.ForceDark : PreferredAppMode.ForceLight);
                    } else {
                        AllowDarkModeForApp?.Invoke(value);
                    }
                    RefreshImmersiveColorPolicyState?.Invoke();
                }
            }
        }

        private Microsoft.Xna.Framework.Color? _Accent;
        public override Microsoft.Xna.Framework.Color Accent => _Accent ??= new Microsoft.Xna.Framework.Color() {
            PackedValue = (uint) (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\DWM", "AccentColor", null) as int? ?? unchecked((int) 0xffeead00))
        };

        private Microsoft.Xna.Framework.Point _SplashSize;
        public override Microsoft.Xna.Framework.Point SplashSize => _SplashSize;

        public Microsoft.Xna.Framework.Color SplashColorMain = new(0x3b, 0x2d, 0x4a, 0xff);
        public Microsoft.Xna.Framework.Color SplashColorNeutral = new(0xff, 0xff, 0xff, 0xff);

        public override Microsoft.Xna.Framework.Color SplashColorBG => DarkMode ? SplashColorMain : SplashColorNeutral;
        public override Microsoft.Xna.Framework.Color SplashColorFG => DarkMode ? SplashColorNeutral : SplashColorMain;


        private bool _BackgroundBlur;
        public override bool BackgroundBlur {
            get => _BackgroundBlur;
            set => _BackgroundBlur = SetBackgroundBlur(value ? 0.6f : -1f);
        }

        public override bool ReduceBackBufferResizes => false; // !IsOpenGL && !IsVulkan && !(CurrentDisplay?.Adapter.IsMain ?? true);

        public override OlympUI.Padding Padding =>
            ExtendedBorderedWindow ? new() {
                Left = 8,
                Top = 8, // Could be 0 but let's have equal padding.
                Right = 8,
                Bottom = 8,
            } :
            default;
        public override ClientSideDecorationMode ClientSideDecoration =>
            EnvFlags.UserCSD ??
            (ExtendedBorderedWindow ? ClientSideDecorationMode.Full :
            ClientSideDecorationMode.Title);

        public override bool IsMultiThreadInit => true;


        public BasicMesh? WindowBackgroundMesh;
        public float WindowBackgroundOpacity;


        private bool _IsMouseFocus;
        public override bool IsMouseFocus => _IsMouseFocus;

        private bool _CaptureMouse;
        public override bool CaptureMouse {
            get => _CaptureMouse;
            set {
                if (_CaptureMouse == value)
                    return;
                _CaptureMouse = value;
                SDL.SDL_CaptureMouse(value ? SDL.SDL_bool.SDL_TRUE : SDL.SDL_bool.SDL_FALSE);
            }
        }

        public override Microsoft.Xna.Framework.Point MouseOffset => IsMaximized ? new(0, -8) : new(0, 0);


        public NativeWin32() {
            App app = App = new();

            Console.WriteLine($"Total time until new NativeWin32(): {app.GlobalWatch.Elapsed}");
            InitStopwatch.Start();

            FNAHooks.FNA3DDeviceUpdated += OnFNA3DDeviceUpdated;
            OnFNA3DDeviceUpdated();

            WndProcCurrentPtr = Marshal.GetFunctionPointerForDelegate(WndProcCurrent = WndProc);
            DefWindowProcW = NativeLibrary.GetExport(NativeLibrary.Load("user32.dll"), nameof(DefWindowProcW));

            if (SystemHasAcrylic) {
                IntPtr uxtheme = NativeLibrary.Load("uxtheme.dll");
                RefreshImmersiveColorPolicyState = Marshal.GetDelegateForFunctionPointer<d_RefreshImmersiveColorPolicyState>(GetProcAddress(uxtheme, (IntPtr) n_RefreshImmersiveColorPolicyState));
                AllowDarkModeForWindow = Marshal.GetDelegateForFunctionPointer<d_AllowDarkModeForWindow>(GetProcAddress(uxtheme, (IntPtr) n_AllowDarkModeForWindow));
                if (SystemHasPreferredAppMode) {
                    SetPreferredAppMode = Marshal.GetDelegateForFunctionPointer<d_SetPreferredAppMode>(GetProcAddress(uxtheme, (IntPtr) n_SetPreferredAppMode));
                } else {
                    AllowDarkModeForApp = Marshal.GetDelegateForFunctionPointer<d_AllowDarkModeForApp>(GetProcAddress(uxtheme, (IntPtr) n_AllowDarkModeForApp));
                }
            }

            // MSDN demands a minimum width of 300 for acceptable behavior with snapping the window to the grid.
            SDL.SDL_SetWindowMinimumSize(app.Window.Handle, 800, 600);

            SDL.SDL_SysWMinfo info = new();
            SDL.SDL_GetVersion(out info.version);
            SDL.SDL_GetWindowWMInfo(app.Window.Handle, ref info);
            IntPtr hwnd = HWnd = info.info.win.window;
            HDc = info.info.win.hdc;

            // Dark mode and the extended frame area + "blur" don't play along together.
            DarkMode = true;
            _DarkMode = DarkModePreferred ?? false;

            // GDI doesn't seem to have any tinted drawing, so let's just tint the image before drawing...
            SplashMain = GetTintedImage("splash_main_win32.png", SplashColorFG);
            SplashWheel = GetTintedImage("splash_wheel_win32.png", SplashColorBG);
            _SplashSize = new(
                SplashMain.Width / 2,
                SplashMain.Height / 2
            );

            // We should now be ready to set our own WndProc. Setting it earlier could possibly access Splash too soon?
            WndProcPrevPtr = SetWindowLongPtr(hwnd, /* GWLP_WNDPROC */ -4, WndProcCurrentPtr);

            // We could prepare the background blur styling here, but doing so turns the splash into additive-ish(?) mode.
            // SetBackgroundBlur(-1f, true);

            // Force-repaint and move the window for the splash screen.
            Microsoft.Xna.Framework.Point size = WindowSize;
            // FIXME: WindowSize seems to be weird. The size is perfect for SetWindowPos but the centering doesn't seem to match.
            SetWindowPos(
                hwnd, NULL,
                GetSystemMetrics(/* SM_CXSCREEN */ 0) / 2 - size.X / 2,
                GetSystemMetrics(/* SM_CYSCREEN */ 1) / 2 - size.Y / 2,
                size.X, size.Y,
                /* SWP_NOZORDER | SWP_FRAMECHANGED */ 0x0004 | 0x0020
            );
            InvalidateRect(hwnd, NULL, true);
            RedrawWindow(hwnd, NULL, NULL, RDW_INVALIDATE | RDW_FRAME | RDW_ERASE | RDW_INTERNALPAINT | RDW_UPDATENOW | RDW_ERASENOW);
        }

        private static Bitmap GetTintedImage(string name, Microsoft.Xna.Framework.Color tint) {
            Bitmap bmp = new(OlympUI.Assets.OpenStream(name) ?? throw new Exception("Win32 splash not found"));

            if (System.Drawing.Image.GetPixelFormatSize(bmp.PixelFormat) != 32) {
                using Bitmap old = bmp;
                bmp = old.Clone(new System.Drawing.Rectangle(0, 0, old.Width, old.Height), System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            }

            System.Drawing.Imaging.BitmapData srcData = bmp.LockBits(
                new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height),
                System.Drawing.Imaging.ImageLockMode.ReadWrite,
                bmp.PixelFormat
            );

            byte* splashData = (byte*) srcData.Scan0;
            for (int i = (bmp.Width * bmp.Height * 4) - 1 - 3; i > -1; i -= 4) {
                splashData[i + 0] = (byte) ((splashData[i + 0] * tint.B) / 255);
                splashData[i + 1] = (byte) ((splashData[i + 1] * tint.G) / 255);
                splashData[i + 2] = (byte) ((splashData[i + 2] * tint.R) / 255);
                splashData[i + 3] = (byte) ((splashData[i + 3] * tint.A) / 255);
            }

            bmp.UnlockBits(srcData);
            return bmp;
        }

        public override void Run() {
            IntPtr hwnd = HWnd;

            // The window won't repaint itself, especially not when dragging it around.
            BGThread = new(BGThreadLoop) {
                Name = "Olympus Win32 Helper Background Thread",
                IsBackground = true,
            };
            BGThread.Start();

            // Let's show the window early.
            // Style value grabbed at runtime, this is what the visible window's style is.
            SetWindowLongPtr(hwnd, /* GWL_STYLE */ -16, (IntPtr) (0x16cf0000 & (long) ~WindowStyles.WS_VISIBLE));
            // Required, otherwise it won't always show up in the task bar or at the top.
            ShowWindow(hwnd, /* SW_SHOWNORMAL */ 1);

            // Initialized is set by the first PrepareEarly run ran in the InitThread spawned in WndProc.
            // Confusing? Yes. But at least it prevents any blinking and delays before showing the splash.

            while (!Initialized || InitPaint >= 0) {
                Thread.Yield();
                while (PeekMessage(out NativeMessage msg, hwnd, 0, 0, /* PM_REMOVE */ 0x0001)) {
                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);

                    // This should ideally be handled in WndProc.
                    // Make sure that we show the splash before getting a bit hung on initializing FNA.
                    if (msg.msg == WindowsMessage.WM_QUIT) {
                        // Leave the init thread to die in a dirty state if necessary. This process is going to die anyway.
                        return;
                    }

                    if (Initialized && InitPaint >= 0) {
                        // Don't get stuck peeking messages if we're done already.
                        break;
                    }
                }
            }

            Console.WriteLine("Game.Run() #2 - running main loop on main thread");
            Game.Run();
        }

        public override void Dispose() {
            App.Dispose();
            FNAHooks.FNA3DDeviceUpdated -= OnFNA3DDeviceUpdated;
        }

        public override void PrepareEarly() {
            if (!Initialized) {
                Initialized = true;
                throw new Exception(ToString());
            }

            Console.WriteLine($"Total time until PrepareEarly: {App.GlobalWatch.Elapsed}");
            InitStopwatch.Stop();
        }

        public override void PrepareLate() {
            Ready = true;

            Console.WriteLine($"Total time until PrepareLate: {App.GlobalWatch.Elapsed}");

            if (SplashCanvas is not null) {
                DeleteDC(SplashCanvasHDC);
                DeleteObject(SplashCanvasHBitmap);
                SplashCanvas.Dispose();
            }

            // Enable background blur if possible without risking a semi-transparent splash.
            BackgroundBlur = true;

            // Set from force-dark mode to preferred mode after window creation.
            _DarkMode = !_DarkMode;
            DarkMode = !_DarkMode;

            // Do other late init stuff.

            WindowBackgroundMesh = new(App) {
                Shapes = {
                    // Will be updated in BeginDrawBB.
                    new MeshShapes.Quad() {
                        XY1 = new(0, 0),
                        XY2 = new(1, 0),
                        XY3 = new(0, 2),
                        XY4 = new(1, 2),
                    },
                    new MeshShapes.Quad() {
                        XY1 = new(1, 1),
                        XY2 = new(2, 1),
                        XY3 = new(1, 2),
                        XY4 = new(2, 2),
                    },
                },
                MSAA = false,
                Texture = OlympUI.Assets.White,
                BlendState = BlendState.Opaque,
                SamplerState = SamplerState.PointClamp,
            };
            WindowBackgroundMesh.Reload();
        }

        public override Microsoft.Xna.Framework.Point FixWindowPositionDisplayDrag(Microsoft.Xna.Framework.Point pos) {
            pos.Y += OffsetTop;
            return pos;
        }

        public override void Update(float dt) {
            TimeSpan time = App.GlobalWatch.Elapsed;
            LastTickStart = time;
        }

        public override void BeginDrawRT(float dt) {
            if (InitPaint >= -3)
                Console.WriteLine($"Total time until BeginDrawRT: {App.GlobalWatch.Elapsed}");

            Viewport vp = new(0, 0, App.Width, App.Height) {
                MinDepth = 0f,
                MaxDepth = 1f,
            };
            if (_IsMaximized) {
                vp.X += MaximizedOffset.Left;
                vp.Y += MaximizedOffset.Top;
                vp.Width -= MaximizedOffset.Left;
                vp.Height -= MaximizedOffset.Top;
            } else {
                // See WM_NCCALCSIZE handler.
                if (!ExtendedBorderedWindow) {
                    vp.Y += 1;
                    // ... weirdly enough there seems to be yet another special pixel row rule when painting and updating manually?!
                    if (!App.ManualUpdate) {
                        vp.Height -= 1;
                    }
                }
            }
            App.GraphicsDevice.Viewport = vp;
        }

        public override void EndDrawRT(float dt) {
        }

        public override void BeginDrawBB(float dt) {
            // One background color for light mode, three for dark mode (focused black vs focused gray vs unfocused), whyyyy
            if (!SetAcrylicOnSelfMaximized && _IsMaximized && ClientSideDecoration == ClientSideDecorationMode.Title) {
                WindowBackgroundOpacity = 1.5f;
            } else {
                WindowBackgroundOpacity -= dt * 2f;
            }
            if (WindowBackgroundOpacity > 0f && WindowBackgroundMesh is not null) {
                // The "ideal" maximized dark bg is 0x2e2e2e BUT it's too bright for the overlay.
                // Light mode is too dark to be called light mode.
                float a = Math.Min(WindowBackgroundOpacity, 1f);
                a = a * a * a * a * a;
                WindowBackgroundMesh.Color =
                    DarkMode ?
                    (new Microsoft.Xna.Framework.Color(0x1e, 0x1e, 0x1e, 0xff) * a) :
                    (new Microsoft.Xna.Framework.Color(0xe0, 0xe0, 0xe0, 0xff) * a);
                fixed (MiniVertex* vertices = &WindowBackgroundMesh.Vertices[0]) {
                    vertices[1].XY = new(App.Width - WindowControlsWidth, 0);
                    vertices[2].XY = new(0, App.Height);
                    vertices[3].XY = new(App.Width - WindowControlsWidth, App.Height);
                    vertices[4].XY = new(App.Width - WindowControlsWidth, WindowControlsHeight);
                    vertices[5].XY = new(App.Width, WindowControlsHeight);
                    vertices[6].XY = new(App.Width - WindowControlsWidth, App.Height);
                    vertices[7].XY = new(App.Width, App.Height);
                }
                WindowBackgroundMesh.QueueNext();
                WindowBackgroundMesh.Draw();
            }
        }

        public override void EndDrawBB(float dt) {
            LastTickEnd = App.GlobalWatch.Elapsed;

            if (InitPaint >= -3) {
                InitPaint--;
                Console.WriteLine($"Total time until EndDrawBB: {App.GlobalWatch.Elapsed}");
            }
        }

        public override void BeginDrawDirect(float dt) {
            BeginDrawBB(dt);
            BeginDrawRT(dt);
        }

        public override void EndDrawDirect(float dt) {
            EndDrawRT(dt);
            EndDrawBB(dt);
        }


        private void OnFNA3DDeviceUpdated() {
            if (FNAHooks.FNA3DDevice is FNA3DD3D11DeviceInfo d3d11Device) {
                FNAHooks.FNA3DDevice = new FNA3DD3D11Win32DeviceInfo(d3d11Device);
            }
        }


        private void BGThreadLoop() {
            IntPtr hwnd = HWnd;

            const int sleepDefault = 80;
            int sleep = default;

            uint guiThread = GetWindowThreadProcessId(hwnd, out _);
            GUITHREADINFO guiInfo = new() {
                cbSize = Marshal.SizeOf<GUITHREADINFO>()
            };

            Stopwatch timer = Stopwatch.StartNew();

            while (BGThread is not null) {
                long time = timer.Elapsed.Ticks;
                long sleepEnd = time + (sleep == default ? sleepDefault : sleep) * TimeSpan.TicksPerMillisecond;
                Thread.Yield();
                while ((time = timer.Elapsed.Ticks) < sleepEnd) {
                    sleep = (int) Math.Min(4, (sleepEnd - time) / TimeSpan.TicksPerMillisecond - 2);
                    if (sleep <= 0)
                        break;
                    Thread.Sleep(sleep);
                }
                while ((time = timer.Elapsed.Ticks) < sleepEnd) {
                    Thread.SpinWait(1);
                }
                sleep = default;

                bool redraw = false;

                if (!redraw) {
                    redraw = !Ready;
                }

                if (!redraw) {
                    redraw = WindowIdleForceRedraw;
                    if (!redraw) {
                        GetGUIThreadInfo(guiThread, ref guiInfo);
                        redraw = (guiInfo.flags & GuiThreadInfoFlags.GUI_INMOVESIZE) == GuiThreadInfoFlags.GUI_INMOVESIZE;
                    }
                }

                if (redraw) {
                    BGThreadRedraw = true;
                    if (!BGThreadRedrawSkip) {
                        if (!ManuallyBlinking) {
                            ManuallyBlinking = true;
                            RedrawWindow(hwnd, NULL, NULL, RDW_INVALIDATE | RDW_FRAME | RDW_ERASE | RDW_INTERNALPAINT | RDW_UPDATENOW | RDW_ERASENOW);
                            if (sleep == default)
                                sleep = 10;
                        } else {
                            if (sleep == default)
                                sleep = -1;
                        }
                    }
                    BGThreadRedrawSkip = false;
                } else {
                    BGThreadRedraw = false;
                }

                // Prevent getting stuck on the splash screen by fooling its miniature blocking loop that the user wants to leave.
                if (IsNclButtonDownAndMoving && !IsNclButtonDownAndMovingFooled && Initialized && !Ready) {
                    IsNclButtonDownAndMovingFooled = true;
                    PostMessage(hwnd, (int) WindowsMessage.WM_KEYDOWN, (IntPtr) /* VK_RETURN */ 0x0D, NULL);
                    PostMessage(hwnd, (int) WindowsMessage.WM_KEYUP, (IntPtr) /* VK_RETURN */ 0x0D, NULL);
                    PostMessage(hwnd, (int) WindowsMessage.WM_NCLBUTTONDOWN, (IntPtr) /* VK_RETURN */ 0x0D, NULL);
                }
            }
        }


        public bool SetBackgroundBlur(float? alpha = null, bool forceUpdate = false) {
            if (alpha is null)
                alpha = LastBackgroundBlur;
            else
                LastBackgroundBlur = alpha.Value;

            // FIXME: DWM composite checks whatever!

            // Windows 10 1809 introduced acrylic blur but it has become unreliable with further updates.
            if (SystemHasAcrylic) {
                Microsoft.Xna.Framework.Color colorPrev = LastBackgroundColor;
                Microsoft.Xna.Framework.Color color;

                if (SetAcrylicOnSelf && !SetAcrylicOnSelfMaximized && _IsMaximized) {
                    // FIXME: Maximized background blur leaves an unblurred area at the right edge with self-acrylic.
                    alpha = 0f;
                }

                if (!IsTransparentPreferred) {
                    // System-wide transparency is disabled by the user.
                    if (SystemHasHostBackdropAcrylic) {
                        // Host backdrop transparency automatically vanishes on focus loss and when transparency is disabled system-wide.
                        alpha = 1f;
                    } else {
                        // Acrylic blur breaks if transparency is enforced when it's disabled system-wide.
                        alpha = 0f;
                    }
                }

                if (alpha < 0f || (byte) (alpha * 255f) == 0) {
                    // Use the system theme color, otherwise the titlebar controls won't match.
                    color =
                        DarkMode ? new(0.2f, 0.2f, 0.2f, alpha.Value) :
                        new(0.65f, 0.65f, 0.65f, alpha.Value);

                } else {
                    // Use the system theme color, otherwise the titlebar controls won't match.
                    color =
                        // Dark mode transparency is very noticeable.
                        DarkMode ? new(0.2f, 0.2f, 0.2f, alpha.Value) :
                        // Light mode transparency is barely noticeable. Multiply by 0.5f also matches maximized better.
                        // Sadly making it too transparent also makes it too dark.
                        new(0.9f, 0.9f, 0.9f, alpha.Value);
                }

                forceUpdate |= colorPrev != color;

                if (SetAcrylicOnSelf) {
                    if (forceUpdate) {
                        if (ExtendedBorderedWindow) {
                            // DwmExtendFrameIntoClientArea loves to misbehave sometimes...

                        } else {
                            // Setting ONLY the top margin and not using invisible blurbehind works as well,
                            // but -1 everywhere allows for the system to show title bar buttons.
                            MARGINS margins = new() {
                                Left = -1,
                                Right = -1,
                                Top = -1,
                                Bottom = -1,
                            };
                            if (!IsTransparent && !SetAcrylicOnSelfMaximized && _IsMaximized) {
                                // Blame DWM for showing the supposedly invisible "window outside of screen" area with ^^^.
                                WorkaroundDWMMaximizedTitleBarSize = true;
                                margins = new() {
                                    Left = 0,
                                    Right = 0,
                                    // Top = rect.Bottom - rect.Top,
                                    // Bottom = 0,
                                    Top = 32,
                                    Bottom = LastWindowRect.Bottom - LastWindowRect.Top,
                                };
                            }
                            DwmExtendFrameIntoClientArea(HWnd, ref margins);
                            if (!SystemHasHostBackdropAcrylic) {
                                // This is only necessary with old acrylic, and breaks maximized bg on host backdrop.
                                DWM_BLURBEHIND blurBehind =
                                    IsTransparent ?
                                    new() {
                                        dwFlags = /* DWM_BB_ENABLE | DWM_BB_BLUREGION */ 0x1 | 0x2,
                                        fEnable = true,
                                        hRgnBlur = InvisibleRegion,
                                    } :
                                    new() {
                                        dwFlags = /* DWM_BB_ENABLE | DWM_BB_BLUREGION */ 0x1 | 0x2,
                                    // Dark mode breaks background blur, thus disable it fully.
                                    // Setting this to false turns the BG black / white, but true turns it light / dark gray.
                                    // fEnable = DarkMode,
                                    // Sadly trying to flip this on demand turns the light mode background black at all times.
                                    fEnable = true,
                                        hRgnBlur = NULL,
                                    };
                                DwmEnableBlurBehindWindow(HWnd, ref blurBehind);
                            }
                        }
                    }

                    SetAcrylic(HWnd, color);

                    if (ExtendedBorderedWindow && ExtendedBorderedWindowResizeOutside) {
                        if (BackgroundResizeChildThread is null) {
                            uint threadMain = GetCurrentThreadId();
                            BackgroundResizeChildThread = new(() => {
                                Win10BackgroundForm child = new(this, "Olympus Olympus Win32 Helper Background Resize Window", false);
                                child.Fix(false, -16, 0, 0, 8);
                                child.Show();
                                BackgroundResizeChild = child;
                                // AttachThreadInput(threadChild, threadMain, true);
                                Application.Run(child);
                            }) {
                                Name = "Olympus Win32 Helper Background Resize Window Thread",
                                IsBackground = true,
                            };
                            BackgroundResizeChildThread.SetApartmentState(ApartmentState.STA);
                            BackgroundResizeChildThread.Start();
                            while (BackgroundResizeChild is null) {
                            }
                            BackgroundResizeChild.FriendlyHandle = FindWindowEx(NULL, NULL, null, "Olympus Olympus Win32 Helper Background Resize Window");
                        }
                    } else {
                        BackgroundResizeChild?.Invoke(child => {
                            Application.Exit();
                            child.Close();
                            child.Dispose();
                            BackgroundResizeChild = null;
                        });
                        BackgroundResizeChildThread?.Join();
                        BackgroundResizeChildThread = null;
                    }

                } else if (IsTransparent) {
                    BackgroundResizeChild?.Invoke(child => {
                        Application.Exit();
                        child.Close();
                        child.Dispose();
                        BackgroundResizeChild = null;
                    });
                    BackgroundResizeChildThread?.Join();
                    BackgroundResizeChildThread = null;

                    Win10BackgroundForm? child = BackgroundAcrylicChild;
                    if (child is null) {
                        child = new Win10BackgroundForm(this, "Olympus Olympus Win32 Helper Background Acrylic Window", true);
                        child.Fix(false, 0, 0, 0, 0);
                    }
                    SetAcrylic(child.Handle, color);
                    if (BackgroundAcrylicChild is null) {
                        BackgroundAcrylicChild = child;
                        child.Show();
                    }

                    if (forceUpdate) {
                        // Apparently a surprisingly legitimate method to enforce a fully transparent window.
                        // Layered windows themselves are horrible, blurbehind isn't functional since Win8, but hey, this works!
                        // WS_EX_NOREDIRECTIONBITMAP would be ideal but sadly it'd require too many changes to SDL2 and FNA3D.
                        SetWindowLongPtr(HWnd, /* GWL_EXSTYLE */ -20, (IntPtr) ((long) GetWindowLongPtr(HWnd, -20) | /* WS_EX_LAYERED */ 0x80000));
                        SetLayeredWindowAttributes(HWnd, 0x00000000, 0xff, /* LWA_ALPHA */ 0x2);

                        MARGINS margins = new() {
                            Left = -1,
                            Right = -1,
                            Top = -1,
                            Bottom = -1,
                        };
                        DwmExtendFrameIntoClientArea(HWnd, ref margins);
                        DWM_BLURBEHIND blurBehind = new() {
                            dwFlags = /* DWM_BB_ENABLE | DWM_BB_BLUREGION */ 0x1 | 0x2,
                            fEnable = true,
                            hRgnBlur = InvisibleRegion,
                        };
                        DwmEnableBlurBehindWindow(HWnd, ref blurBehind);
                    }

                } else if (forceUpdate) {
                    BackgroundResizeChild?.Invoke(child => {
                        Application.Exit();
                        child.Close();
                        child.Dispose();
                        BackgroundResizeChild = null;
                    });
                    BackgroundResizeChildThread?.Join();
                    BackgroundResizeChildThread = null;

                    BackgroundAcrylicChild?.Close();
                    BackgroundAcrylicChild?.Dispose();
                    BackgroundAcrylicChild = null;

                    MARGINS margins = new() {
                        Left = -1,
                        Right = -1,
                        Top = -1,
                        Bottom = -1,
                    };
                    DwmExtendFrameIntoClientArea(HWnd, ref margins);
                    DWM_BLURBEHIND blurBehind = new() {
                        dwFlags = /* DWM_BB_ENABLE | DWM_BB_BLUREGION */ 0x1 | 0x2,
                        fEnable = false,
                        hRgnBlur = NULL,
                    };
                    DwmEnableBlurBehindWindow(HWnd, ref blurBehind);

                    SetWindowLongPtr(HWnd, /* GWL_EXSTYLE */ -20, (IntPtr) ((long) GetWindowLongPtr(HWnd, -20) & /* WS_EX_LAYERED */ ~0x80000));
                }

            }

            return false;
        }

        private void SetAcrylic(IntPtr hwnd, Microsoft.Xna.Framework.Color color) {
            AccentPolicy accent;
            WindowCompositionAttributeData data;

            if (SystemHasHostBackdropAcrylic) {
                /* Use host backdrop acrylic where possible.
                 * It follows Windows design guidelines a bit better, transitions between light and dark properly,
                 * and fixes a NC button color bug introduced in insider builds before 22557, when Win32 NC got a fresh coat of paint.
                 * Sadly it doesn't let us change the color or any of its behavior.
                 */

                BackdropStyle style = IsMaximized ? BackdropStyle.BACKDROP_MICA : BackdropStyle.BACKDROP_ACRYLIC;

                data = new() {
                    Attribute = WindowCompositionAttribute.WCA_BACKDROP_STYLE,
                    Data = (IntPtr) (&style),
                    SizeOfData = sizeof(int),
                };
                SetWindowCompositionAttribute(hwnd, ref data);

                accent = new() {
                    AccentState = AccentState.ACCENT_ENABLE_HOSTBACKDROP
                };

                data = new() {
                    Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
                    Data = (IntPtr) (&accent),
                    SizeOfData = sizeof(AccentPolicy),
                };
                SetWindowCompositionAttribute(hwnd, ref data);
                return;
            }

            /* Acrylic accent flags:
             * 0b00000000000000000000000000000001: ???????? Used by Windows Explorer for the task bar
             * 0b00000000000000000000000000000010: [WIN10 ] Force accent color? (At least according to info online)
             * 0b00000000000000000000000000000010: [WIN11+] Opaque(-ish?) but NOT Mica
             * 0b00000000000000000000000000000100: [WIN10+] Backdrop is as big as entire display area (unless clipped by Win11 round corners)
             * 0b00000000000000000000000000000100: [WIN11+] FIX RESIZING LAG BUT fullscreen spills onto all screens when maximized
             * 0b00000000000000000000000000001000: ???????? Not used by anything?
             * 0b00000000000000000000000000010000: [WIN11+] PERMANENT: Clip backdrop. Seems to fix the aforementioned spilling, only on 22000? But also clips too hard in maximized mode.
             * 0b00000000000000000000000000100000: [WIN10+] Left border
             * 0b00000000000000000000000001000000: [WIN10+] Top border
             * 0b00000000000000000000000010000000: [WIN10+] Right border
             * 0b00000000000000000000000100000000: [WIN10+] Bottom border
             */
            accent =
                color.A == 0 ?
                new() {
                    // DISABLED is transparent.
                    AccentState = AccentState.ACCENT_ENABLE_GRADIENT,
                    GradientColor = color.PackedValue | 0xff000000, // Kinda lucky about alignment here.
                } :
                new() {
                    AccentState = AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND,
                    AccentFlags =
                        (
                        SystemHasAcrylicFixes && !_IsMaximized ?
                        0b00000000000000000000000000010100U :
                        0b00000000000000000000000000000000U
                        ) |
                        (
                        LegacyBorderedWindow ?
                        0b00000000000000000000000111100000U :
                        0b00000000000000000000000000000000U
                        ) |
                        0b00000000000000000000000000000000U,
                    GradientColor = color.PackedValue, // Kinda lucky about alignment here.
            };

            data = new() {
                Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
                Data = (IntPtr) (&accent),
                SizeOfData = sizeof(AccentPolicy),
            };
            SetWindowCompositionAttribute(hwnd, ref data);
        }

        private static bool GetIsMaximized(IntPtr hwnd) {
            WINDOWPLACEMENT placement = new() {
                Length = Marshal.SizeOf<WINDOWPLACEMENT>(),
            };
            return GetWindowPlacement(hwnd, ref placement) && placement.ShowCmd == /* SW_MAXIMIZE */ 3;
        }

        private IntPtr WndProc(IntPtr hwnd, WindowsMessage msg, IntPtr wParam, IntPtr lParam) {
            if (hwnd != HWnd)
                return CallWindowProc(WndProcPrevPtr, hwnd, msg, wParam, lParam);

            // Console.WriteLine($"{hwnd}, {msg}: {wParam}, {lParam}");

            IntPtr rv;
            HitTestValues hit;
            POINT point, pointReal;
            RECT rect;
            MONITORINFO monitorInfo;
            IntPtr monitor;
            IntPtr hwndResize;

            switch (msg) {
                case WindowsMessage.WM_QUIT when !Ready:
                    Game.Exit();
                    break;

                // This feels quite dirty, but it's necessary to prevent hanging when moving the splash.
                // NCLBUTTONDOWN-induced WM_SYSCOMMAND SC_MOVE / SC_SIZEs will run their own blocking loop.
                case WindowsMessage.WM_NCLBUTTONDOWN:
                    IsNclButtonDown = true;
                    IsNclButtonDownValue = (HitTestValues) wParam;
                    rv = CallWindowProc(WndProcPrevPtr, hwnd, msg, wParam, lParam);
                    IsNclButtonDown = false;
                    IsNclButtonDownAndMovingFooled = false;
                    return rv;

                case WindowsMessage.WM_SYSCOMMAND when IsNclButtonDown:
                    WindowIdleForceRedraw = true;
                    IsNclButtonDownAndMoving = true;
                    rv = CallWindowProc(WndProcPrevPtr, hwnd, msg, wParam, lParam);
                    IsNclButtonDownAndMoving = false;
                    return NULL;

                case WindowsMessage.WM_MOVE:
                    WindowIdleForceRedraw = false;
                    GetWindowRect(HWnd, out LastWindowRect);
                    GetClientRect(HWnd, out LastClientRect);
                    if (Ready) {
                        // Just doing lots of SetBackgroundBlur seems to help with moving lag?!
                        if (SetAcrylicOnSelf && FlickerAcrylicOnSelfMove) {
                            SetBackgroundBlur(null, true);
                        }
                        // Force-redraw ourselves as on some systems, MOVE isn't followed up by PAINT...
                        if (!BGThreadRedraw) {
                            BGThreadRedrawSkip = true;
                            RedrawWindow(hwnd, NULL, NULL, RDW_INVALIDATE | RDW_FRAME | RDW_ERASE | RDW_INTERNALPAINT | RDW_UPDATENOW | RDW_ERASENOW);
                        }
                        BGThreadRedraw = false;
                    }
                    break;

                case WindowsMessage.WM_SIZE:
                    WindowIdleForceRedraw = false;
                    _IsMaximized = GetIsMaximized(hwnd);
                    GetWindowRect(hwnd, out LastWindowRect);
                    GetClientRect(hwnd, out LastClientRect);
                    if (Ready) {
                        if (_IsMaximized && SetAcrylicOnSelf) {
                            // SOMEHOW flashing the window is needed to update both the background and button colors.
                            // Luckily it also calls SetBackgroundBlur.
                            DarkMode = !DarkMode;
                            DarkMode = !DarkMode;
                        } else {
                            SetBackgroundBlur(null, true);
                        }
                    }
                    if (ClipBackgroundAcrylicChild && BackgroundAcrylicChild is not null) {
                        // Note that this won't be round, but given how this should only affect Win10, who cares?
                        // FIXME: Delete the old region? According to MSDN, Windows does that for us.
                        SetWindowRgn(BackgroundAcrylicChild.Handle, CreateRectRgn(LastClientRect.Left, LastClientRect.Top, LastClientRect.Right, LastClientRect.Bottom), false);
                    }
                    break;

                case WindowsMessage.WM_SYSCOMMAND when ((ulong) wParam & 0xFFF0) == /* SC_MOVE */ 0xF010:
                case WindowsMessage.WM_ENTERSIZEMOVE:
                case WindowsMessage.WM_ENTERIDLE:
                    WindowIdleForceRedraw = true;
                    break;
                case WindowsMessage.WM_EXITSIZEMOVE:
                case WindowsMessage.WM_CAPTURECHANGED when ((ulong) wParam) == 0:
                    WindowIdleForceRedraw = false;
                    break;

                case WindowsMessage.WM_ACTIVATE:
                    WindowIdleForceRedraw = false;
                    _IsMaximized = GetIsMaximized(HWnd);
                    GetWindowRect(HWnd, out LastWindowRect);
                    GetClientRect(HWnd, out LastClientRect);
                    break;

                case WindowsMessage.WM_SETTINGCHANGE:
                // case WindowsMessage.WM_THEMECHANGED:
                case WindowsMessage.WM_DWMCOMPOSITIONCHANGED:
                    _IsMaximized = GetIsMaximized(HWnd);
                    GetWindowRect(HWnd, out LastWindowRect);
                    GetClientRect(HWnd, out LastClientRect);
                    _DarkModePreferred = null;
                    _Accent = null;
                    _IsTransparentPreferred = null;
                    _DarkMode = !(DarkModePreferred ?? _DarkMode);
                    DarkMode = DarkModePreferred ?? !_DarkMode;
                    break;

                case WindowsMessage.WM_DISPLAYCHANGE:
                    _IsMaximized = GetIsMaximized(HWnd);
                    GetWindowRect(HWnd, out LastWindowRect);
                    GetClientRect(HWnd, out LastClientRect);
                    _Displays = null;
                    break;

                case WindowsMessage.WM_NCCALCSIZE:
                    _IsMaximized = GetIsMaximized(HWnd);
                    GetWindowRect(HWnd, out LastWindowRect);
                    GetClientRect(HWnd, out LastClientRect);
                    if (wParam == ONE) {
                        // Call the original WndProc for easier resizing,
                        // BUT undo the title bar at the top.
                        // Fun fact: There's no extra resize region at the top!
                        NCCALCSIZE_PARAMS* ncsize = (NCCALCSIZE_PARAMS*) lParam;
                        RECT prev = ncsize->RgRc0;
                        CallWindowProc(WndProcPrevPtr, hwnd, msg, wParam, lParam);
                        RECT* next = &ncsize->RgRc0;
                        OffsetLeft = next->Left - prev.Left;
                        OffsetTop = next->Top - prev.Top;
                        next->Top = prev.Top;
                        // Make sure that maximized windows line up with the workspace.
                        monitorInfo = new() {
                            Size = Marshal.SizeOf<MONITORINFO>(),
                        };
                        if (_IsMaximized &&
                            (monitor = MonitorFromWindow(hwnd, 0)) != NULL &&
                            GetMonitorInfo(monitor, ref monitorInfo)) {
                            // Setting param->rgrc0 = monitorInfo.WorkArea sadly breaks DwmDefWindowProc.
                            // Some DWM flags are doing a funny though AND we can revert to the pre-DefWindowProc rect just fine?!
                            *next = prev;
                            // SOMEHOW IT KEEPS GOING PAST THE SCREEN AREA TO THE LEFT (AND RIGHT ON WIN10?!) (AND BOTTOM ON IDK ANYMORE).
                            // TOP SEEMS TO BE FINE AFTER MUCH TESTING. I DO NOT KNOW WHAT IS GOING ON ANYMORE.
                            next->Left = monitorInfo.WorkArea.Left;
                            next->Right = monitorInfo.WorkArea.Right;
                            next->Bottom = monitorInfo.WorkArea.Bottom;
                            // The maximized window might extend a bit past the monitor area.
                            MaximizedOffset.Left = monitorInfo.WorkArea.Left - next->Left;
                            MaximizedOffset.Top = monitorInfo.WorkArea.Top - next->Top;
                            MaximizedOffset.Right = monitorInfo.WorkArea.Right - next->Right;
                            MaximizedOffset.Bottom = monitorInfo.WorkArea.Bottom - next->Bottom;
                        } else if (ExtendedBorderedWindow && !IsIconic(hwnd)) {
                            // Windows 10 loves to pretend that the non-client area is acrylic'd.
                            next->Left -= 8;
                            next->Right += 8;
                            next->Bottom += 8;
                        } else {
                            // We SHOULDN'T + 1 this but not doing this will cost us one row of pixels in windowed mode.
                            // param->rgrc0.Top += 1;
                            // Nevermind, turns out that row is also interactive...
                        }
                        return NULL;
                    }
                    break;

                case WindowsMessage.WM_NCACTIVATE:
                case WindowsMessage.WM_NCPAINT:
                    // Calling the default is a must, otherwise Windows 10 (and especially 11) won't add the special border.
                    // Weirdly enough Windows handles / forces a lot of painting outside of WM_NCPAINT as well...
                    // Likewise, handling the PAINT event enables the standard hit test handling... welp.
                    CallWindowProc(WndProcPrevPtr, hwnd, msg, wParam, lParam);
                    // In case this ever becomes necessary again...
                    // NCPaint(hwnd, wParam, System.Drawing.Color.Transparent);
                    return NULL;

                case WindowsMessage.WM_PAINT:
                    if (!Ready) {
                        if (!Initialized) {
                            IntPtr hdc = BeginPaint(hwnd, out PAINTSTRUCT ps);
                            if (hdc != NULL) {
                                try {
                                    PaintSplash(hdc);
                                } finally {
                                    EndPaint(hwnd, ref ps);
                                }
                            }
                        }

                        rv = CallWindowProc(DefWindowProcW, hwnd, msg, wParam, lParam);
                        ManuallyBlinking = false;

                        // This really shouldn't happen during WM_PAINT, but who's going to stop us?
                        // Init FNA after drawing the splash.
                        if (InitPaint >= 0) {
                            InitPaint++;

                            switch (InitPaint) {
                                case 3:
                                    // ... but sure that the graphics device is created delayed on the main thread.
                                    GraphicsDeviceManager gdm = (GraphicsDeviceManager) App.Services.GetService(typeof(IGraphicsDeviceManager));
                                    WrappedGDM = new(gdm);
                                    WrappedGDM.CanCreateDevice = false;
                                    break;

                                case 4:
                                    App.Services.RemoveService(typeof(IGraphicsDeviceManager));
                                    App.Services.RemoveService(typeof(IGraphicsDeviceService));
                                    break;

                                case 5:
                                    App.Services.AddService(typeof(IGraphicsDeviceManager), WrappedGDM);
                                    App.Services.AddService(typeof(IGraphicsDeviceService), WrappedGDM);
                                    break;

                                case 6:
                                    // Start initializing everything on a separate thread so that the splash can continue splashing.
                                    InitThread = new(() => {
                                        try {
                                            Console.WriteLine("Game.Run() #1 - initializing on separate thread");
                                            Game.Run();
                                        } catch (Exception ex) when (ex.Message == ToString()) {
                                            Console.WriteLine("Game.Run() #1 done");
                                        } catch (Exception ex) {
                                            Console.Error.WriteLine(ex);
                                            PostMessage(HWnd, (int) WindowsMessage.WM_QUIT, NULL, NULL);
                                            throw;
                                        }
                                    }) {
                                        Name = "Olympus Win32 FNA Initialization Thread",
                                        IsBackground = true,
                                    };
                                    InitThread.Start();
                                    break;

                                case 7:
                                    // Don't move on if we're not initialized yet.
                                    if (!Initialized)
                                        InitPaint--;
                                    break;

                                case 8:
                                    // WrappedGDM MUST be non-null here as it was created in an earlier paint.
                                    // Create the device now and repaint a few more times as device creation can change window properties.
                                    WrappedGDM!.CanCreateDevice = true;

                                    // XNA - and thus in turn FNA - love to re-center the window on device changes.
                                    Microsoft.Xna.Framework.Point pos = WindowPosition;
                                    FNAHooks.ApplyWindowChangesWithoutRestore = true;
                                    FNAHooks.ApplyWindowChangesWithoutResize = true;
                                    FNAHooks.ApplyWindowChangesWithoutCenter = true;
                                    WrappedGDM!.CreateDevice();
                                    FNAHooks.ApplyWindowChangesWithoutRestore = false;
                                    FNAHooks.ApplyWindowChangesWithoutResize = false;
                                    FNAHooks.ApplyWindowChangesWithoutCenter = false;

                                    // In some circumstances, fixing the window position is required, but only on device changes.
                                    if (WindowPosition != pos)
                                        WindowPosition = FixWindowPositionDisplayDrag(pos);

                                    break;

                                case 14:
                                    // We're done - repaint one more time but without the progress bar, otherwise it'll look stuck.
                                    SplashDone = true;
                                    break;

                                case 15:
                                    // We're done - set InitPaint to -1 prevent this from running any further, and to break out of the message pump loop.
                                    InitPaint = -1;
                                    break;

                            }
                        }

                        return rv;
                    }
#if true
                    // We could break and pass through to SDL2's handler, which then calls into FNA,
                    // or we could avoid the unnecessary double trip through P/Invoke marshalling and back.
                    App.ForceRedraw();
                    rv = CallWindowProc(DefWindowProcW, hwnd, msg, wParam, lParam);
                    ManuallyBlinking = false;
                    return rv;
#else
                    // NCPaint(hwnd, wParam, System.Drawing.Color.Transparent);
                    break;
#endif

                case WindowsMessage.WM_ERASEBKGND:
                    if (!Ready) {
                        // FIXME: Figure out if only WM_PAINT or WM_ERASEBKGND is necessary.
                        // PaintSplash(wParam);
                        return NULL;
                    }
                    break;

                case WindowsMessage.WM_MOUSEMOVE:
                    // High poll rate mouses cause SDL2's event pump to lag!
                    // Luckily there's a fix for this in modern SDL2.
                    // Let's continue using our own mouse focus tracking tho.
                    if (!_IsMouseFocus) {
                        _IsMouseFocus = true;
                        TRACKMOUSEEVENT track = new() {
                            cbSize = Marshal.SizeOf<TRACKMOUSEEVENT>(),
                            // dwFlags = /* TME_LEAVE | TME_NONCLIENT */ 0x00000012,
                            dwFlags = /* TME_LEAVE */ 0x00000002,
                            hwndTrack = hwnd,
                            dwHoverTime = 0,
                        };
                        TrackMouseEvent(ref track);
                    }

                    return CallWindowProc(DefWindowProcW, hwnd, msg, wParam, lParam);
                    // return CallWindowProc(WndProcPrevPtr, hwnd, msg, wParam, lParam);

                case WindowsMessage.WM_MOUSELEAVE:
                    _IsMouseFocus = false;
                    break;

                case WindowsMessage.WM_NCMOUSELEAVE:
                    _IsMouseFocus = false;
                    DwmDefWindowProc(hwnd, msg, wParam, lParam, out _);
                    break;

                case WindowsMessage.WM_NCMOUSEHOVER:
                case WindowsMessage.WM_NCMOUSEMOVE:
                // case WindowsMessage.WM_NCMOUSELEAVE:
                    DwmDefWindowProc(hwnd, msg, wParam, lParam, out _);
                    break;

                case WindowsMessage.WM_NCHITTEST:
                    // Don't use SDL's hittest helper as it won't pass it on if we return normal from the SDL side of things.
                    hit = (HitTestValues) CallWindowProc(WndProcPrevPtr, hwnd, msg, wParam, lParam);
                    // Call DwmDefWindowProc to let the system handle window controls.
                    bool hitDWM = DwmDefWindowProc(hwnd, msg, wParam, lParam, out rv);

                    pointReal = new() {
                        X = (ushort) (((ulong) lParam >> 0) & 0xFFFF),
                        Y = (ushort) (((ulong) lParam >> 16) & 0xFFFF),
                    };
                    point = pointReal;
                    rect = LastClientRect;
                    const int border = 8;
                    // FIXME: GET THIS THE HELL OUT OF HERE.
                    const int windowButtonWidth = border + 48 * 3 + 2 * 2 + 8;
                    if (hit == HitTestValues.HTCLIENT && ScreenToClient(hwnd, ref point)) {
                        if (point.Y < OffsetTop) {
                            if (hitDWM && point.Y > 0 && ((HitTestValues) rv) > HitTestValues.HTSYSMENU)
                                return rv;

                            if (point.Y < border && ExtendedBorderedWindow && ExtendedBorderedWindowResizeOutside) {
                                return (IntPtr) HitTestValues.HTNOWHERE;
                            }

                            if (ClientSideDecoration >= ClientSideDecorationMode.Full && point.Y >= border && point.X >= rect.Right - windowButtonWidth && point.X <= rect.Right - border) {
                                return (IntPtr) HitTestValues.HTCLIENT;
                            }

                            // Windows 11 (maybe 10 too) allows the window to be moved in a weird way from the corners.
                            if (_IsMaximized && (point.X <= 32 || point.X > rect.Right - 32)) {
                                return (IntPtr) HitTestValues.HTCLIENT;
                            }

                            if (!(ExtendedBorderedWindow && ExtendedBorderedWindowResizeOutside)) {
                                if (point.Y < border) {
                                    if (point.X < border) {
                                        return (IntPtr) (_IsMaximized ? HitTestValues.HTNOWHERE : HitTestValues.HTTOPLEFT);
                                    }
                                    if (point.X >= rect.Right - border) {
                                        return (IntPtr) (_IsMaximized ? HitTestValues.HTNOWHERE : HitTestValues.HTTOPRIGHT);
                                    }
                                    return (IntPtr) (_IsMaximized ? HitTestValues.HTNOWHERE : HitTestValues.HTTOP);
                                }
                                if (_IsMaximized || (ExtendedBorderedWindow && !ExtendedBorderedWindowResizeOutside && ClientSideDecoration < ClientSideDecorationMode.Title)) {
                                    return (IntPtr) HitTestValues.HTCAPTION;
                                }
                                if (point.X < border) {
                                    return (IntPtr) (_IsMaximized ? HitTestValues.HTNOWHERE : HitTestValues.HTLEFT);
                                }
                                if (point.X >= rect.Right - border) {
                                    return (IntPtr) (_IsMaximized ? HitTestValues.HTNOWHERE : HitTestValues.HTRIGHT);
                                }
                            }

                            return (IntPtr) HitTestValues.HTCAPTION;
                        }

                        if (_IsMaximized && (
                                point.X < border || point.X >= rect.Right - border ||
                                point.Y < border || point.Y >= rect.Bottom - border
                            ))
                            return (IntPtr) HitTestValues.HTNOWHERE;

                        if (!_IsMaximized && ExtendedBorderedWindow && !ExtendedBorderedWindowResizeOutside) {
                            if (point.Y >= rect.Bottom - border) {
                                if (point.X < border) {
                                    return (IntPtr) (_IsMaximized ? HitTestValues.HTNOWHERE : HitTestValues.HTBOTTOMLEFT);
                                }
                                if (point.X >= rect.Right - border) {
                                    return (IntPtr) (_IsMaximized ? HitTestValues.HTNOWHERE : HitTestValues.HTBOTTOMRIGHT);
                                }
                                return (IntPtr) (_IsMaximized ? HitTestValues.HTNOWHERE : HitTestValues.HTBOTTOM);
                            }
                            if (point.X < border) {
                                return (IntPtr) (_IsMaximized ? HitTestValues.HTNOWHERE : HitTestValues.HTLEFT);
                            }
                            if (point.X >= rect.Right - border) {
                                return (IntPtr) (_IsMaximized ? HitTestValues.HTNOWHERE : HitTestValues.HTRIGHT);
                            }
                        }

                        // Splash screens on Windows behave like full-sized titlebars.
                        if (!Ready)
                            return (IntPtr) HitTestValues.HTCAPTION;
                    }
                    return (IntPtr) hit;

                case WindowsMessage.WM_NCRBUTTONUP:
                    // The system menu won't show up outside of the original title bar.
                    hit = (HitTestValues) wParam;
                    pointReal = new() {
                        X = (short) (((ulong) lParam >> 0) & 0xFFFF),
                        Y = (short) (((ulong) lParam >> 16) & 0xFFFF),
                    };
                    point = pointReal;
                    rect = LastClientRect;
                    if (ScreenToClient(hwnd, ref point)) {
                        if (hit == HitTestValues.HTCAPTION || !Ready) {
                            int cmd = TrackPopupMenu(GetSystemMenu(hwnd, false), /* TPM_RETURNCMD */ 0x0100, pointReal.X, pointReal.Y, 0, hwnd, IntPtr.Zero);
                            if (cmd > 0)
                                SendMessage(hwnd, (int) WindowsMessage.WM_SYSCOMMAND, (IntPtr) cmd, IntPtr.Zero);
                            break;
                        }
                    }
                    break;

                case WindowsMessage.WM_SYSCOMMAND:
                    if (!Ready) {
                        switch ((ulong) wParam & 0xFFF0) {
                            case /* SC_CLOSE */ 0xF060:
                                PostMessage(HWnd, (int) WindowsMessage.WM_QUIT, NULL, NULL);
                                return (IntPtr) 1;
                        }
                    }
                    break;

                case WindowsMessage.WM_USER_ADE_MOVE_AFTER:
                    SetWindowPos(hwnd, wParam, 0, 0, 0, 0, (uint) lParam);
                    return NULL;

                case WindowsMessage.WM_USER_ADE_MOVE:
                    SetWindowPos(
                        hwnd, NULL,
                        unchecked((short) (ushort) (((ulong) wParam) >> 0)),
                        unchecked((short) (ushort) (((ulong) wParam) >> 16)),
                        unchecked((short) (ushort) (((ulong) lParam) >> 0)),
                        unchecked((short) (ushort) (((ulong) lParam) >> 16)),
                        /* SWP_NOACTIVATE | SWP_NOOWNERZORDER */ 0x0010 | 0x0200
                    );
                    return NULL;

            }

            rv = CallWindowProc(WndProcPrevPtr, hwnd, msg, wParam, lParam);

            switch (msg) {
                case WindowsMessage.WM_MOVE:
                case WindowsMessage.WM_SIZE:
                case WindowsMessage.WM_ACTIVATE:
                case WindowsMessage.WM_WININICHANGE:
                case WindowsMessage.WM_THEMECHANGED:
                case WindowsMessage.WM_DWMCOMPOSITIONCHANGED:
                case WindowsMessage.WM_DISPLAYCHANGE:
                    _IsMaximized = GetIsMaximized(HWnd);
                    GetWindowRect(HWnd, out LastWindowRect);
                    GetClientRect(HWnd, out LastClientRect);
                    // Excessive but eh whatever.
                    BackgroundAcrylicChild?.Fix(false, 0, 0, 0, 0);
                    hwndResize = BackgroundResizeChild?.FriendlyHandle ?? NULL;
                    if (hwndResize != NULL) {
                        PostMessage(
                            hwndResize, (int) WindowsMessage.WM_USER_ADE_CALL_FIX, NULL,
                            (IntPtr) (
                                ((unchecked((ulong) (byte) (sbyte) -16)) << 0) | 
                                ((unchecked((ulong) (byte) (sbyte) 0)) << 8) |
                                ((unchecked((ulong) (byte) (sbyte) 0)) << 16) |
                                ((unchecked((ulong) (byte) (sbyte) 8)) << 24)
                            )
                        );
                    }
                    break;

                case WindowsMessage.WM_WINDOWPOSCHANGED:
                    // TODO: This likes to fire quite often.
                    WINDOWPOS* posInfo = (WINDOWPOS*) lParam;
                    Win10BackgroundForm? resizer = BackgroundResizeChild;
                    hwndResize = resizer?.FriendlyHandle ?? NULL;
                    GUITHREADINFO guiInfo = new() {
                        cbSize = Marshal.SizeOf<GUITHREADINFO>()
                    };
                    if (resizer is not null && hwndResize != NULL && posInfo->HWndInsertAfter != hwndResize && GetGUIThreadInfo(resizer.ThreadID, ref guiInfo) && (guiInfo.flags & GuiThreadInfoFlags.GUI_INMOVESIZE) == 0) {
                        SetWindowPos(
                            hwndResize, posInfo->HWndInsertAfter,
                            0, 0,
                            0, 0,
                            /* SWP_NOSIZE | SWP_NOMOVE | SWP_NOREDRAW | SWP_NOACTIVATE | SWP_DEFERERASE | SWP_NOSENDCHANGING */ 0x0001 | 0x0002 | 0x0008 | 0x0010 | 0x2000 | 0x0400
                        );
                    }
                    break;

                case WindowsMessage.WM_PAINT:
                    ManuallyBlinking = false;
                    break;
            }

            return rv;
        }

        internal static void NCPaint(IntPtr hwnd, IntPtr wParam, System.Drawing.Color color) {
            IntPtr hdc =
                wParam == NULL ?
                GetDCEx(hwnd, wParam, DCX_WINDOW | DCX_USESTYLE | DCX_LOCKWINDOWUPDATE) :
                GetDCEx(hwnd, wParam, DCX_WINDOW | DCX_USESTYLE | DCX_LOCKWINDOWUPDATE | DCX_CACHE | DCX_INTERSECTRGN | DCX_NODELETERGN);

            if (hdc == NULL)
                return;

            try {
                using (Graphics g = Graphics.FromHdc(hdc)) {
                    g.Clear(color);
                }
            } finally {
                ReleaseDC(hwnd, hdc);
            }
        }

        private void PaintSplash(IntPtr hdc) {
            if (hdc == NULL)
                return;

            Microsoft.Xna.Framework.Point wheelOffset = new(128, 156);

            int width = LastClientRect.Right - LastClientRect.Left;
            int height = LastClientRect.Bottom - LastClientRect.Top;

            float prog = (float) (InitStopwatch.Elapsed.TotalMilliseconds / 1000D);

            if (SplashCanvas is not null && (SplashCanvas.Width < width || SplashCanvas.Height < height)) {
                DeleteObject(SplashCanvasHBitmap);
                SplashCanvas.Dispose();
                SplashCanvas = null;
            }

            if (SplashCanvasHDC == IntPtr.Zero) {
                SplashCanvasHDC = CreateCompatibleDC(hdc);
            }

            if (SplashCanvas is null) {
                SplashCanvas = new(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                SplashCanvasHBitmap = SplashCanvas.GetHbitmap();
                SelectObject(SplashCanvasHDC, SplashCanvasHBitmap);
            }

            using (Graphics g = Graphics.FromHdc(SplashCanvasHDC)) {
                // Fun fact: white is best to hide the fact that there is still a very short white flash.
                Microsoft.Xna.Framework.Color bgXNA = SplashColorBG;
                Microsoft.Xna.Framework.Color fgXNA = SplashColorFG;
                System.Drawing.Color bg = System.Drawing.Color.FromArgb(bgXNA.A, bgXNA.R, bgXNA.G, bgXNA.B);
                System.Drawing.Color fg = System.Drawing.Color.FromArgb(fgXNA.A, fgXNA.R, fgXNA.G, fgXNA.B);
                g.Clear(bg);
                g.ResetTransform();

                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;

                g.DrawImage(
                    SplashMain,
                    width / 2 - _SplashSize.X / 2,
                    height / 2 - _SplashSize.Y / 2,
                    SplashMain.Width / 2,
                    SplashMain.Height / 2
                );

                g.ResetTransform();
                g.TranslateTransform(
                    width / 2 - _SplashSize.X / 2 + wheelOffset.X / 2 + 0.75f,
                    height / 2 - _SplashSize.Y / 2 + wheelOffset.Y / 2 + 0.25f
                );
                g.RotateTransform(2f * prog / MathF.PI * 180f);
                g.DrawImage(
                    SplashWheel,
                    -wheelOffset.X / 2 - 0.75f,
                    -wheelOffset.Y / 2 - 0.25f,
                    SplashMain.Width / 2,
                    SplashMain.Height / 2
                );
                g.ResetTransform();

                if (!SplashDone) {
                    using SolidBrush b = new(fg);
                    prog *= 1.3f;
                    prog %= 2;
                    if (prog < 1f) {
                        prog = prog * prog * prog;
                        g.FillRectangle(
                            b,
                            width / 2 - _SplashSize.X / 2,
                            height / 2 + _SplashSize.Y / 2 - 8,
                            (int) (_SplashSize.X * prog),
                            2
                        );
                    } else {
                        prog -= 1f;
                        prog = 1f - prog;
                        prog = prog * prog * prog;
                        prog = 1f - prog;
                        g.FillRectangle(
                            b,
                            width / 2 - _SplashSize.X / 2 + (int) (_SplashSize.X * prog),
                            height / 2 + _SplashSize.Y / 2 - 8,
                            (int) (_SplashSize.X * (1f - prog)),
                            2
                        );
                    }
                }
            }

            BitBlt(hdc, 0, 0, width, height, SplashCanvasHDC, 0, 0, TernaryRasterOperations.SRCCOPY);
        }


#region Misc utils

        [DllImport("user32.dll")]
        static extern IntPtr GetActiveWindow();

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        static extern int GetSystemMetrics(int smIndex);

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll")]
        static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

        [DllImport("user32.dll")]
        static extern int GetWindowRgn(IntPtr hWnd, IntPtr hRgn);

        [DllImport("user32.dll")]
        static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr hWndChildAfter, string? className, string? windowTitle);

        [DllImport("kernel32")]
        static extern IntPtr GetProcAddress(IntPtr hModule, IntPtr procName);

#endregion


#region Window accent policy and composition attributes

        enum AccentState {
            ACCENT_DISABLED,
            ACCENT_ENABLE_GRADIENT,
            ACCENT_ENABLE_TRANSPARENTGRADIENT,
            ACCENT_ENABLE_BLURBEHIND,
            ACCENT_ENABLE_ACRYLICBLURBEHIND,
            ACCENT_ENABLE_HOSTBACKDROP,
            ACCENT_TRANSPARENT,
            ACCENT_INVALID_STATE,
        }

        enum CornerStyle {
            CORNER_DEFAULT,
            CORNER_SQUARE,
            CORNER_ROUND,
            CORNER_LIGHTSHADOW_ROUND_SMALL,
            CORNER_LIGHTSHADOW_ROUND,
            CORNER_INVALID,
        }

        enum BackdropStyle {
            BACKDROP_DEFAULT,
            BACKDROP_NONE,
            BACKDROP_MICA,
            BACKDROP_ACRYLIC,
            BACKDROP_TABBED,
        }

        enum PartColorTarget {
            PART_BORDER,
            PART_NC_BACKGROUND,
            PART_NC_TEXT,
            PART_NC_INVALID,
        }

        [StructLayout(LayoutKind.Sequential)]
        struct AccentPolicy {
            public AccentState AccentState;
            public uint AccentFlags;
            public uint GradientColor;
            public int AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct PartColorPolicy {
            public PartColorTarget Part;
            public uint Color;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct WindowCompositionAttributeData {
            public WindowCompositionAttribute Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        enum WindowCompositionAttribute {
            WCA_UNDEFINED,
            WCA_NCRENDERING_ENABLED,
            WCA_NCRENDERING_POLICY,
            WCA_TRANSITIONS_FORCEDISABLED,
            WCA_ALLOW_NCPAINT,
            WCA_CAPTION_BUTTON_BOUNDS,
            WCA_NONCLIENT_RTL_LAYOUT,
            WCA_FORCE_ICONIC_REPRESENTATION,
            WCA_EXTENDED_FRAME_BOUNDS,
            WCA_HAS_ICONIC_BITMAP,
            WCA_THEME_ATTRIBUTES,
            WCA_NCRENDERING_EXILED,
            WCA_NCADORNMENTINFO,
            WCA_EXCLUDED_FROM_LIVEPREVIEW,
            WCA_VIDEO_OVERLAY_ACTIVE,
            WCA_FORCE_ACTIVEWINDOW_APPEARANCE,
            WCA_DISALLOW_PEEK,
            WCA_CLOAK,
            WCA_CLOAKED,
            WCA_ACCENT_POLICY,
            WCA_FREEZE_REPRESENTATION,
            WCA_EVER_UNCLOAKED,
            WCA_VISUAL_OWNER,
            WCA_HOLOGRAPHIC,
            WCA_EXCLUDED_FROM_DDA,
            WCA_PASSIVEUPDATEMODE,
            WCA_USEDARKMODECOLORS,
            WCA_CORNER_STYLE,
            WCA_PART_COLOR,
            WCA_DISABLE_MOVESIZE_FEEDBACK,
            WCA_BACKDROP_STYLE,
            WCA_LAST,
        }

        enum DwmWindowAttribute {
            DWMWA_NCRENDERING_ENABLED = 1,
            DWMWA_NCRENDERING_POLICY,
            DWMWA_TRANSITIONS_FORCEDISABLED,
            DWMWA_ALLOW_NCPAINT,
            DWMWA_CAPTION_BUTTON_BOUNDS,
            DWMWA_NONCLIENT_RTL_LAYOUT,
            DWMWA_FORCE_ICONIC_REPRESENTATION,
            DWMWA_FLIP3D_POLICY,
            DWMWA_EXTENDED_FRAME_BOUNDS,
            DWMWA_HAS_ICONIC_BITMAP,
            DWMWA_DISALLOW_PEEK,
            DWMWA_EXCLUDED_FROM_PEEK,
            DWMWA_CLOAK,
            DWMWA_CLOAKED,
            DWMWA_FREEZE_REPRESENTATION,
            DWMWA_PASSIVE_UPDATE_MODE,
            DWMWA_USE_HOSTBACKDROPBRUSH,
            DWMWA_USE_IMMERSIVE_DARK_MODE = 20,
            DWMWA_WINDOW_CORNER_PREFERENCE = 33,
            DWMWA_BORDER_COLOR,
            DWMWA_CAPTION_COLOR,
            DWMWA_TEXT_COLOR,
            DWMWA_VISIBLE_FRAME_BORDER_THICKNESS,
            DWMWA_LAST
        }

        [DllImport("user32.dll")]
        static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        [DllImport("user32.dll")]
        static extern int GetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        [DllImport("dwmapi.dll")]
        static extern int DwmGetWindowAttribute(IntPtr hwnd, DwmWindowAttribute dwAttribute, out bool pvAttribute, int cbAttribute);

        [DllImport("dwmapi.dll")]
        static extern int DwmGetWindowAttribute(IntPtr hwnd, DwmWindowAttribute dwAttribute, out int pvAttribute, int cbAttribute);

        [DllImport("dwmapi.dll")]
        static extern int DwmGetWindowAttribute(IntPtr hwnd, DwmWindowAttribute dwAttribute, out RECT pvAttribute, int cbAttribute);

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, DwmWindowAttribute dwAttribute, ref bool pvAttribute, int cbAttribute);

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, DwmWindowAttribute dwAttribute, ref int pvAttribute, int cbAttribute);

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, DwmWindowAttribute dwAttribute, ref RECT pvAttribute, int cbAttribute);



#endregion


#region Window margin

        [StructLayout(LayoutKind.Sequential)]
        struct MARGINS {
            public int Left;
            public int Right;
            public int Top;
            public int Bottom;
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

#endregion


#region Window handler

        [StructLayout(LayoutKind.Sequential)]
        struct WINDOWPOS {
            public IntPtr HWnd;
            public IntPtr HWndInsertAfter;
            public int X;
            public int Y;
            public int CX;
            public int CY;
            public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct NCCALCSIZE_PARAMS {
            public RECT RgRc0;
            public RECT RgRc1;
            public RECT RgRc2;
            public WINDOWPOS* Pos;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct POINT {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct WINDOWPLACEMENT {
            public int Length;
            public int Flags;
            public int ShowCmd;
            public POINT MinPosition;
            public POINT MaxPosition;
            public RECT NormalPosition;
        }


        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct MONITORINFO {
            public int Size;
            public RECT Monitor;
            public RECT WorkArea;
            public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MINMAXINFO {
            public POINT Reserved;
            public POINT MaxSize;
            public POINT MaxPosition;
            public POINT MinTrackSize;
            public POINT MaxTrackSize;
        }

        enum HitTestValues {
            HTERROR = -2,
            HTTRANSPARENT = -1,
            HTNOWHERE = 0,
            HTCLIENT = 1,
            HTCAPTION = 2,
            HTSYSMENU = 3,
            HTGROWBOX = 4,
            HTMENU = 5,
            HTHSCROLL = 6,
            HTVSCROLL = 7,
            HTMINBUTTON = 8,
            HTMAXBUTTON = 9,
            HTLEFT = 10,
            HTRIGHT = 11,
            HTTOP = 12,
            HTTOPLEFT = 13,
            HTTOPRIGHT = 14,
            HTBOTTOM = 15,
            HTBOTTOMLEFT = 16,
            HTBOTTOMRIGHT = 17,
            HTBORDER = 18,
            HTOBJECT = 19,
            HTCLOSE = 20,
            HTHELP = 21
        }

        [Flags]
        enum WVR {
            WVR_ALIGNTOP    = 0x0010,
            WVR_ALIGNLEFT   = 0x0020,
            WVR_ALIGNBOTTOM = 0x0040,
            WVR_ALIGNRIGHT  = 0x0080,
            WVR_HREDRAW     = 0x0100,
            WVR_VREDRAW     = 0x0200,
            WVR_REDRAW      = WVR_HREDRAW | WVR_VREDRAW,
            WVR_VALIDRECTS  = 0x0400,
        }

        delegate IntPtr WndProcDelegate(IntPtr hWnd, WindowsMessage msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        struct TRACKMOUSEEVENT {
            public int cbSize;
            public uint dwFlags;
            public IntPtr hwndTrack;
            public int dwHoverTime;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct NativeMessage {
            public IntPtr handle;
            public WindowsMessage msg;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public POINT p;
        }

        [DllImport("user32.dll")]
        static extern bool PeekMessage(out NativeMessage lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

        [DllImport("user32.dll")]
        static extern bool TranslateMessage(ref NativeMessage lpMsg);

        [DllImport("user32.dll")]
        static extern IntPtr DispatchMessage(ref NativeMessage lpMsg);

        [DllImport("user32.dll")]
        static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, WindowsMessage uMsg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll")]
        static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll")]
        static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wp, IntPtr lp);

        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wp, IntPtr lp);

        [DllImport("user32.dll")]
        static extern IntPtr GetSystemMenu(IntPtr hWnd, bool bRevert);

        [DllImport("user32.dll")]
        static extern bool DestroyMenu(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern int TrackPopupMenu(IntPtr hMenu, uint uFlags, int x, int y, int nReserved, IntPtr hWnd, IntPtr prcRect);

        [DllImport("user32.dll")]
        static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

        [DllImport("user32.dll")]
        static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern IntPtr MonitorFromWindow(IntPtr hwnd, int dwFlags);

        [DllImport("user32.dll")]
        static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [DllImport("dwmapi.dll")]
        static extern bool DwmDefWindowProc(IntPtr hWnd, WindowsMessage msg, IntPtr wParam, IntPtr lParam, out IntPtr plResult);

        [DllImport("user32.dll")]
        static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT lpEventTrack);

#endregion


#region Window non-client area repaint

        [StructLayout(LayoutKind.Sequential)]
        struct RECT {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        const int DCX_WINDOW = 0x1;
        const int DCX_CACHE = 0x2;
        const int DCX_INTERSECTRGN = 0x80;
        const int DCX_LOCKWINDOWUPDATE = 400;
        const int DCX_USESTYLE = 0x10000; // Supposedly undocumented for decades.
        const int DCX_NODELETERGN = 0x40000; // Supposedly undocumented for decades.

        const int RDW_INVALIDATE = 0x1;
        const int RDW_INTERNALPAINT = 0x2;
        const int RDW_ERASE = 0x4;
        const int RDW_NOERASE = 0x20;
        const int RDW_NOCHILDREN = 0x40;
        const int RDW_UPDATENOW = 0x100;
        const int RDW_ERASENOW = 0x200;
        const int RDW_FRAME = 0x400;

        [Flags]
        enum WindowStyles : uint {
            WS_BORDER = 0x800000,
            WS_CAPTION = 0xc00000,
            WS_CHILD = 0x40000000,
            WS_CLIPCHILDREN = 0x2000000,
            WS_CLIPSIBLINGS = 0x4000000,
            WS_DISABLED = 0x8000000,
            WS_DLGFRAME = 0x400000,
            WS_GROUP = 0x20000,
            WS_HSCROLL = 0x100000,
            WS_MAXIMIZE = 0x1000000,
            WS_MAXIMIZEBOX = 0x10000,
            WS_MINIMIZE = 0x20000000,
            WS_MINIMIZEBOX = 0x20000,
            WS_OVERLAPPED = 0x0,
            WS_OVERLAPPEDWINDOW = WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | WS_SIZEFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX,
            WS_POPUP = 0x80000000u,
            WS_POPUPWINDOW = WS_POPUP | WS_BORDER | WS_SYSMENU,
            WS_SIZEFRAME = 0x40000,
            WS_SYSMENU = 0x80000,
            WS_TABSTOP = 0x10000,
            WS_VISIBLE = 0x10000000,
            WS_VSCROLL = 0x200000,
        }

        [DllImport("user32.dll")]
        static extern IntPtr GetWindowDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern IntPtr GetDCEx(IntPtr hWnd, IntPtr hRgn, int flags);

        [DllImport("user32.dll")]
        static extern IntPtr ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("user32.dll")]
        static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hRgnUpdate, int flags);

        [DllImport("user32.dll")]
        static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

#endregion


#region Window client area repaint

        enum TernaryRasterOperations : uint {
            SRCCOPY = 0x00CC0020,
            SRCPAINT = 0x00EE0086,
            SRCAND = 0x008800C6,
            SRCINVERT = 0x00660046,
            SRCERASE = 0x00440328,
            NOTSRCCOPY = 0x00330008,
            NOTSRCERASE = 0x001100A6,
            MERGECOPY = 0x00C000CA,
            MERGEPAINT = 0x00BB0226,
            PATCOPY = 0x00F00021,
            PATPAINT = 0x00FB0A09,
            PATINVERT = 0x005A0049,
            DSTINVERT = 0x00550009,
            BLACKNESS = 0x00000042,
            WHITENESS = 0x00FF0062,
            CAPTUREBLT = 0x40000000,
        }


        [StructLayout(LayoutKind.Sequential)]
        struct PAINTSTRUCT {
            public IntPtr hdc;
            public bool fErase;
            public RECT rcPaint;
            public bool fRestore;
            public bool fIncUpdate;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] rgbReserved;
        }

        [DllImport("user32.dll")]
        static extern IntPtr BeginPaint(IntPtr hwnd, out PAINTSTRUCT lpPaint);

        [DllImport("user32.dll")]
        static extern bool EndPaint(IntPtr hWnd, [In] ref PAINTSTRUCT lpPaint);

        [DllImport("gdi32.dll")]
        static extern bool BitBlt(IntPtr hObject, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hObjSource, int nXSrc, int nYSrc, TernaryRasterOperations dwRop);

#endregion


#region Child window

        [DllImport("user32.dll")]
        static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll")]
        static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int nWidth, int nHeight, uint uFlags);

#endregion


#region Transparent window

        enum CombineRgnStyles {
            RGN_AND = 1,
            RGN_OR,
            RGN_XOR,
            RGN_DIFF,
            RGN_COPY,
            RGN_MAX,
            RGN_MIN = RGN_AND,
        }

        [StructLayout(LayoutKind.Sequential)]
        struct BLENDFUNCTION {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct DWM_BLURBEHIND {
            public int dwFlags;
            public bool fEnable;
            public IntPtr hRgnBlur;
            public bool fTransitionOnMaximized;
        }

        [DllImport("user32.dll")]
        static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

        [DllImport("user32.dll")]
        static extern bool UpdateLayeredWindow(
            IntPtr hwnd,
            IntPtr hdcDst, IntPtr pptDst, IntPtr psize,
            IntPtr hdcSrc, ref POINT pptSrc,
            uint crKey, [In] ref BLENDFUNCTION pblend,
            uint dwFlags
        );

        [DllImport("gdi32.dll")]
        static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int cx, int cy);

        [DllImport("gdi32.dll")]
        static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);

        [DllImport("gdi32.dll")]
        static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        static extern bool DeleteObject(IntPtr hObject);

        [DllImport("dwmapi.dll")]
        static extern void DwmEnableBlurBehindWindow(IntPtr hwnd, ref DWM_BLURBEHIND blurBehind);

        [DllImport("gdi32.dll")]
        static extern IntPtr CreateRectRgn(int x1, int y1, int x2, int y2);

        [DllImport("gdi32.dll")]
        static extern int CombineRgn(IntPtr hrgnDest, IntPtr hrgnSrc1, IntPtr hrgnSrc2, CombineRgnStyles fnCombineMode);

        [DllImport("kernel32.dll")]
        static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

#endregion


#region Dark Mode

        enum PreferredAppMode {
            Default,
            AllowDark,
            ForceDark,
            ForceLight,
            Invalid
        }

        // The following functions are defined in uxtheme.

        const int n_RefreshImmersiveColorPolicyState = 104;
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        delegate void d_RefreshImmersiveColorPolicyState();
        d_RefreshImmersiveColorPolicyState? RefreshImmersiveColorPolicyState;

        const int n_AllowDarkModeForWindow = 133;
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        delegate bool d_AllowDarkModeForWindow(IntPtr hWnd, bool value);
        d_AllowDarkModeForWindow? AllowDarkModeForWindow;

        // Older than 1903 / 18362
        const int n_AllowDarkModeForApp = 135;
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        delegate bool d_AllowDarkModeForApp(bool value);
        d_AllowDarkModeForApp? AllowDarkModeForApp;

        // 1903+ / 18362+
        const int n_SetPreferredAppMode = 135;
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        delegate PreferredAppMode d_SetPreferredAppMode(PreferredAppMode value);
        d_SetPreferredAppMode? SetPreferredAppMode;

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string? pszSubIdList);

#endregion


#region GUI Thread

        [Flags]
        enum GuiThreadInfoFlags {
            GUI_CARETBLINKING = 0x00000001,
            GUI_INMENUMODE = 0x00000004,
            GUI_INMOVESIZE = 0x00000002,
            GUI_POPUPMENUMODE = 0x00000010,
            GUI_SYSTEMMENUMODE = 0x00000008
        }

        [StructLayout(LayoutKind.Sequential)]
        struct GUITHREADINFO {
            public int cbSize;
            public GuiThreadInfoFlags flags;
            public IntPtr hwndActive;
            public IntPtr hwndFocus;
            public IntPtr hwndCapture;
            public IntPtr hwndMenuOwner;
            public IntPtr hwndMoveSize;
            public IntPtr hwndCaret;
            public System.Drawing.Rectangle rcCaret;
        }

        [DllImport("user32.dll")]
        static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

#endregion


#region Custom display info

        private class Adapter {

            public bool IsMain;

            public Adapter(bool isMain) {
                IsMain = isMain;
            }

        }

        private class Display {

            public Adapter Adapter;
            public Microsoft.Xna.Framework.Rectangle Bounds;

            public Display(Adapter adapter, Microsoft.Xna.Framework.Rectangle bounds) {
                Adapter = adapter;
                Bounds = bounds;
            }

        }

#endregion

    }
}

#endif
