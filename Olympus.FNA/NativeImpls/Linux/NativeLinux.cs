using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OlympUI;
using SDL2;
using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Olympus.NativeImpls {
    public partial class NativeLinux : NativeImpl {

        protected bool Initialized = false;
        protected bool Ready = false;

        protected Thread? SplashThread;

        public Point _SplashSize;
        public override bool CanRenderTransparentBackground => false;
        public override bool IsActive => App.IsActive;
        public override bool IsMaximized => false;
        public override Point WindowPosition {
            get => App.Window.ClientBounds.Location;
            set {
            }
        }

        // TODO: Dark mode detection
        public override bool? DarkModePreferred => null;
        public override bool DarkMode { get; set; } = false;
        public override Color Accent => new(0x00, 0xad, 0xee, 0xff);
        public override Point SplashSize => _SplashSize;

        protected readonly object redrawSync = new();
        protected SDL.SDL_SysWMinfo SDL_info;
        
        // TODO: Make NativeWin32 and NativeLinux inherit NativeSplashable
        public Color SplashColorMain = new(0x3b, 0x2d, 0x4a, 0xff); // TODO: Move this to NativeImpl or SplashableImpl
        public Color SplashColorNeutral = new(0xff, 0xff, 0xff, 0xff);

        public override Color SplashColorBG => DarkMode ? SplashColorMain : SplashColorNeutral;
        public override Color SplashColorFG => DarkMode ? SplashColorNeutral : SplashColorMain;
        public override bool BackgroundBlur { get; set; } = false;
        public override bool ReduceBackBufferResizes { get; } = false;
        public override Padding Padding => default;
        public override ClientSideDecorationMode ClientSideDecoration => EnvFlags.UserCSD ?? ClientSideDecorationMode.None;
        public override bool IsMultiThreadInit => true;
        
        public override bool IsMouseFocus => SDL.SDL_GetMouseFocus() == Game.Window.Handle;
        
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

        public override Point MouseOffset => default;
        
        public BasicMesh? WindowBackgroundMesh;
        public float WindowBackgroundOpacity = 1f;

        public NativeLinux() {
        }

        public override void Run() {
            string? forceDriver = null;

            if (!string.IsNullOrEmpty(forceDriver))
                SDL.SDL_SetHintWithPriority("FNA3D_FORCE_DRIVER", forceDriver, SDL.SDL_HintPriority.SDL_HINT_OVERRIDE);

            using (App app = App = new()) {

                SDL.SDL_SetWindowMinimumSize(App.Window.Handle, 800, 600);
                SDL_info = new();
                SDL.SDL_VERSION(out SDL_info.version);
                SDL.SDL_GetWindowWMInfo(app.Window.Handle, ref SDL_info);
                SDL.SDL_SetWindowSize(App.Window.Handle, 800, 600);
                
                SplashThread = new Thread(SplashRoutine) {
                    Name = $"{App.Name} Linux Splash thread",
                    IsBackground = true,
                };
                SplashThread.Start();
                
                GraphicsDeviceManager gdm = (GraphicsDeviceManager) App.Services.GetService(typeof(IGraphicsDeviceManager))!;
                
                WrappedGraphicsDeviceManager wrappedGDM = new(gdm);

                App.Services.RemoveService(typeof(IGraphicsDeviceManager));
                App.Services.RemoveService(typeof(IGraphicsDeviceService));
                App.Services.AddService(typeof(IGraphicsDeviceManager), wrappedGDM);
                App.Services.AddService(typeof(IGraphicsDeviceService), wrappedGDM);

                wrappedGDM.CanCreateDevice = false;

                if (!string.IsNullOrEmpty(forceDriver) && FNAHooks.FNA3DDriver != forceDriver)
                    throw new Exception($"Tried to force FNA3D to use {forceDriver} but got {FNAHooks.FNA3DDriver}.");

                try {
                    Console.WriteLine("Game.Run() #1 - initializing the game engine");
                    Game.Run();
                } catch (Exception ex) when (ex.Message == ToString()) {
                    Console.WriteLine("Game.Run() #1 done");
                }

                // Thread.Sleep(2000);
                wrappedGDM.CanCreateDevice = true;
                lock (redrawSync) { // Bad things happen if we present and create a device at the same time
                    // XNA - and thus in turn FNA - love to re-center the window on device changes.
                    Point pos = WindowPosition;
                    FNAHooks.ApplyWindowChangesWithoutRestore = true;
                    FNAHooks.ApplyWindowChangesWithoutResize = true;
                    FNAHooks.ApplyWindowChangesWithoutCenter = true;
                    wrappedGDM.CreateDevice();
                    FNAHooks.ApplyWindowChangesWithoutRestore = false;
                    FNAHooks.ApplyWindowChangesWithoutResize = false;
                    FNAHooks.ApplyWindowChangesWithoutCenter = false;
                    if (WindowPosition != pos)
                        WindowPosition = FixWindowPositionDisplayDrag(pos);
                }

                Thread.Sleep(1000);
                
                Initialized = true;
                
                Console.WriteLine("Game.Run() #2 - running main loop on main thread");
                Game.Run();
            }
        }

        public override void Dispose() {
        }

        public override void PrepareEarly() {
            if (!Initialized) {
                throw new Exception(ToString());
            }

            Console.WriteLine($"Total time until PrepareEarly: {App.GlobalWatch.Elapsed}");
        }

        public override void PrepareLate() {
            Ready = true;
            SplashThread = null;
            // We really dont want to mess with fna, disable this just in case
            SDL.SDL_SetHint( SDL.SDL_HINT_RENDER_SCALE_QUALITY, "0" );
            
            Console.WriteLine($"Total time until PrepareLate: {App.GlobalWatch.Elapsed}");
            
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
                },
                MSAA = false,
                Texture = OlympUI.Assets.White,
                BlendState = BlendState.Opaque,
                SamplerState = SamplerState.PointClamp,
            };
            WindowBackgroundMesh.Reload();
        }

        public override Point FixWindowPositionDisplayDrag(Point pos) {
            return pos;
        }

        public override void Update(float dt) {
        }

        public override void BeginDrawRT(float dt) {
        }

        public override void EndDrawRT(float dt) {
        }

        public override void BeginDrawBB(float dt) {
        }

        public override void EndDrawBB(float dt) {
        }

        public override unsafe void BeginDrawDirect(float dt) {
            // TODO: Figure out what does this do
            // WindowBackgroundOpacity -= dt * 0.02f;
            // if (WindowBackgroundOpacity > 0f && WindowBackgroundMesh is not null) {
            //     // The "ideal" maximized dark bg is 0x2e2e2e BUT it's too bright for the overlay.
            //     // Light mode is too dark to be called light mode.
            //     float a = Math.Min(WindowBackgroundOpacity, 1f);
            //     a = a * a * a * a * a;
            //     WindowBackgroundMesh.Color =
            //         DarkMode ?
            //         (new Microsoft.Xna.Framework.Color(0x1e, 0x1e, 0x1e, 0xff) * a) :
            //         (new Microsoft.Xna.Framework.Color(0xe0, 0xe0, 0xe0, 0xff) * a);
            //     fixed (MiniVertex* vertices = &WindowBackgroundMesh.Vertices[0]) {
            //         vertices[1].XY = new(App.Width, 0);
            //         vertices[2].XY = new(0, App.Height);
            //         vertices[3].XY = new(App.Width, App.Height);
            //     }
            //     WindowBackgroundMesh.QueueNext();
            //     WindowBackgroundMesh.Draw();
            // }
            
        }

        public override void EndDrawDirect(float dt) {
        }


        private Stopwatch timer = new Stopwatch();

        private const int sleepDefault = 1000/60;
        private int currSleep = default;

        private void SplashRoutine() {
            SDL.SDL_SetHint( SDL.SDL_HINT_RENDER_SCALE_QUALITY, "1" );
            SDL.SDL_GetWindowSize(App.Window.Handle, out int winW, out int winH);
            IntPtr renderer = SDL.SDL_CreateRenderer(App.Window.Handle, -1,
                SDL.SDL_RendererFlags.SDL_RENDERER_ACCELERATED | SDL.SDL_RendererFlags.SDL_RENDERER_PRESENTVSYNC);

            IntPtr mountainPng = IMG_LoadTexture(renderer, OlympUI.Assets.GetPath("splash_main_win32.png")
                                                           ?? throw new Exception(
                                                               "Can't find splash_main_win32.png"));
            Point mountainSize = new();
            SDL.SDL_QueryTexture(mountainPng, out _, out _, out mountainSize.X, out mountainSize.Y);
            SDL.SDL_SetTextureColorMod(mountainPng, SplashColorFG.R, SplashColorFG.G, SplashColorFG.B);
            
            IntPtr wheelPng = IMG_LoadTexture(renderer, OlympUI.Assets.GetPath("splash_wheel_win32.png")
                                                                       ?? throw new Exception(
                                                                           "Can't find splash_wheel_win32.png"));
            Point wheelSize = new();
            SDL.SDL_QueryTexture(wheelPng, out _, out _, out wheelSize.X, out wheelSize.Y);
            SDL.SDL_SetTextureColorMod(wheelPng, SplashColorBG.R, SplashColorBG.G, SplashColorBG.B);

            // mountainSize == wheelSize should be always true, but be safe just in case, for the future
            _SplashSize = new Point(Math.Max(mountainSize.X, wheelSize.X), Math.Max(mountainSize.Y, wheelSize.Y));
            
            SDL.SDL_ShowWindow(App.Window.Handle); // Show window late so it doesnt flash hopefully
            
            while (SplashThread != null) {
                while (SDL.SDL_PollEvent(out SDL.SDL_Event sdl_ev) != 0) {
                    switch (sdl_ev.type) {
                        case SDL.SDL_EventType.SDL_QUIT:
                            Console.WriteLine("Exiting early...");
                            Environment.Exit(0);
                            break;
                        default:
                            break;
                    }
    
                }

                lock (redrawSync) {
                    SDL.SDL_GetWindowSize(App.Window.Handle, out winW, out winH);

                    SDL.SDL_SetRenderDrawColor(renderer, SplashColorBG.R, SplashColorBG.G, SplashColorBG.B,
                        SplashColorBG.A);
                    SDL.SDL_RenderClear(renderer);

                    SDL.SDL_Rect mountainRect = new SDL.SDL_Rect() {
                        x = winW / 2 - mountainSize.X / 2, 
                        y = winH / 2 - mountainSize.Y / 2, 
                        w = mountainSize.X, 
                        h = mountainSize.Y,
                    };
                    SDL.SDL_RenderCopy(renderer, mountainPng, IntPtr.Zero, ref mountainRect);
                    SDL.SDL_Rect wheelRect = new SDL.SDL_Rect() {
                        x = winW / 2 - wheelSize.X / 2, 
                        y = winH / 2 - wheelSize.Y / 2, 
                        w = wheelSize.X, 
                        h = wheelSize.Y,
                    };
                    SDL.SDL_Point wheelCenter = new SDL.SDL_Point() {
                        x = 128,
                        y = 156, 
                    };
                    double animTime = App.GlobalWatch.Elapsed.TotalSeconds;
                    SDL.SDL_RenderCopyEx(renderer, wheelPng, IntPtr.Zero, ref wheelRect, 2 * animTime / Math.PI * 180, 
                        ref wheelCenter, SDL.SDL_RendererFlip.SDL_FLIP_NONE);
                    if (!Initialized) {
                        animTime *= 1.3f;
                        animTime %= 2;
                        SDL.SDL_Rect barRect;
                        if (animTime < 1f) {
                            animTime = animTime * animTime * animTime;
                            barRect = new() {
                                x = winW / 2 - mountainSize.X / 2,
                                y = winH / 2 + mountainSize.Y / 2 - 16,
                                w = (int) (mountainSize.X * animTime),
                                h = 4,
                            };
                        } else {
                            animTime -= 1f;
                            animTime = 1f - animTime;
                            animTime = animTime * animTime * animTime;
                            animTime = 1f - animTime;
                            barRect = new() {
                                x = winW / 2 - mountainSize.X / 2 + (int) (mountainSize.X * animTime),
                                y = winH / 2 + mountainSize.Y / 2 - 16,
                                w = (int) (mountainSize.X * (1f - animTime)),
                                h = 4,
                            };
                        }
                        SDL.SDL_SetRenderDrawColor(renderer, SplashColorFG.R, SplashColorFG.G, SplashColorFG.B, 
                            SplashColorFG.A);
                        SDL.SDL_RenderFillRect(renderer, ref barRect);
                    }
                    SDL.SDL_RenderPresent(renderer);
                }
            }
        }
        
        /* Used for stack allocated string marshaling. */
        private static int Utf8Size(string str)
        {
        	if (str == null)
        	{
        		return 0;
        	}
        	return (str.Length * 4) + 1;
        }
        
        #region Copied SDL-CS Code
        // The following code is copied from SDL-CS, credit goes to its creators

        /* Used for heap allocated string marshaling.
		 * Returned byte* must be free'd with FreeHGlobal.
		 */
		private static unsafe byte* Utf8EncodeHeap(string str)
		{
			if (str == null)
			{
				return (byte*) 0;
			}

			int bufferSize = Utf8Size(str);
			byte* buffer = (byte*) Marshal.AllocHGlobal(bufferSize);
			fixed (char* strPtr = str)
			{
				Encoding.UTF8.GetBytes(strPtr, str.Length + 1, buffer, bufferSize);
			}
			return buffer;
		}
        
        /* IntPtr refers to an SDL_Texture*, renderer to an SDL_Renderer* */
        [DllImport("SDL2_image", EntryPoint = "IMG_LoadTexture", CallingConvention = CallingConvention.Cdecl)]
        private static extern unsafe IntPtr INTERNAL_IMG_LoadTexture(
            IntPtr renderer,
            byte* file
        );
        public static unsafe IntPtr IMG_LoadTexture(
            IntPtr renderer,
            string file
        ) {
            byte* utf8File = Utf8EncodeHeap(file);
            IntPtr handle = INTERNAL_IMG_LoadTexture(
                renderer,
                utf8File
            );
            Marshal.FreeHGlobal((IntPtr) utf8File);
            return handle;
        }
        
        #endregion


    }
}
