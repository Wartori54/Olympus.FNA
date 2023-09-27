using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OlympUI;
using Olympus.API;
using SDL2;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;

using static Olympus.NativeImpls.NativeImpl;

namespace Olympus {
    public unsafe class App : Game, IReloadableTemporaryContext {

#pragma warning disable CS8618 // Nullability is fun but can't see control flow.
        public static App Instance;
        public Web Web;
        public ModAPI.RemoteAPIManager APIManager;
        public FinderManager FinderManager;
#pragma warning restore CS8618

        public Config Config = new();

        public static readonly string Name = "Olympus.FNA";
        public static readonly Version Version = typeof(App).Assembly.GetName().Version ?? new Version(1, 0, 0, 0);

        public static readonly object[] EmptyArgs = new object[0];

        public static readonly MethodInfo m_GameWindow_OnClientSizeChanged =
            typeof(GameWindow).GetMethod("OnClientSizeChanged", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance) ??
            throw new Exception($"GameWindow.OnClientSizeChanged not found!");


        public GraphicsDeviceManager Graphics;
        public SpriteBatch SpriteBatch;


        public readonly Stopwatch GlobalWatch = Stopwatch.StartNew();
        public float Time => (float) GlobalWatch.Elapsed.TotalSeconds;


        private Rectangle PrevClientBounds = new();
        
        

        // Int representing the following states:
        // 0 -> draw has never been called
        // 1 -> draw has been called
        // 2 -> an update was called after the first draw
        private uint DrawState = 0;


        public int FPS;
        private int CountingFrames;
        private TimeSpan CountingFramesTime = new(0);
        private long CountingFramesWatchLast;

        public bool Resizing;
        public bool ManualUpdate;
        private bool ManuallyUpdated = true;
        public bool ManualUpdateSkip;
        private int UnresizedManualDrawCount = 0;
        private bool ForceBeginDraw;

        private readonly Dictionary<Type, object> ComponentCache = new();


        private int? WidthOverride;
        public int Width => WidthOverride ?? GraphicsDevice.PresentationParameters.BackBufferWidth;
        private int? HeightOverride;
        public int Height => HeightOverride ?? GraphicsDevice.PresentationParameters.BackBufferHeight;


        public float BackgroundOpacityTime = 0f;

        public static readonly TimeSpan UpdateDelaySpan = TimeSpan.FromMilliseconds(1000f/60f); // Target 60ups (updates per second)

        // Note: Even though VSync can result in a higher FPS, it can cause Windows to start dropping *displayed* frames...
#if DEBUG && false
        public bool VSync = false; // FIXME: DON'T SHIP WITH VSYNC OFF!
#else
        public bool VSync = true;
#endif


        private HashSet<IReloadable> TemporaryReloadables = new();
        private HashSet<IReloadable> TemporaryReloadablesDead = new();


#pragma warning disable CS8618 // Nullability is fun but can't see control flow.
        public App() {
#pragma warning restore CS8618
            Instance = this;

            Graphics = new GraphicsDeviceManager(this);
            Graphics.PreferredDepthStencilFormat = DepthFormat.None;
            Graphics.PreferMultiSampling = false;
            Graphics.PreferredBackBufferWidth = 1100;
            Graphics.PreferredBackBufferHeight = 600;
            SDL.SDL_SetWindowMinimumSize(Window.Handle, 800, 600);

            Graphics.PreparingDeviceSettings += (s, e) => {
                GraphicsDeviceInformation gdi = e.GraphicsDeviceInformation;

                if (EnvFlags.IsFullscreen) {
                    DisplayMode dm = gdi.Adapter.CurrentDisplayMode;

                    Graphics.IsFullScreen = true;

                    Graphics.PreferredBackBufferWidth = dm.Width;
                    Graphics.PreferredBackBufferHeight = dm.Height;
                    gdi.PresentationParameters.BackBufferWidth = dm.Width;
                    gdi.PresentationParameters.BackBufferHeight = dm.Height;
                }
            };

#if DEBUG
            Window.Title = $"{App.Name} (DEBUG)";
#else
            Window.Title = $"{App.Name}";
#endif
            Window.AllowUserResizing = true;
            IsMouseVisible = true;

            IsFixedTimeStep = false;
            Graphics.SynchronizeWithVerticalRetrace = true;

            Content.RootDirectory = "Content";

            UI.MainThread = Thread.CurrentThread;

            Web = new(this);
            // WebAPI = new ThunderstoreWebAPI(this, "enter-the-gungeon");
            // WebAPI = new CelesteWebAPI(this);
            APIManager = new ModAPI.RemoteAPIManager();
            FinderManager = new(this);
        }


        public T Get<T>() where T : AppComponent {
            if (ComponentCache.TryGetValue(typeof(T), out object? value))
                return (T) value;
            foreach (IGameComponent component in Components)
                if (component is T)
                    return (T) (ComponentCache[typeof(T)] = component);
            throw new Exception($"App component of type \"{typeof(T).FullName}\" not found");
        }


        public IReloadable<TValue, TMeta> MarkTemporary<TValue, TMeta>(IReloadable<TValue, TMeta> reloadable) where TMeta : struct {
            lock (TemporaryReloadables) {
                TemporaryReloadables.Add(reloadable);
                reloadable.LifeBump();
                return reloadable;
            }
        }

        public IReloadable<TValue, TMeta> UnmarkTemporary<TValue, TMeta>(IReloadable<TValue, TMeta> reloadable) where TMeta : struct {
            lock (TemporaryReloadables) {
                TemporaryReloadables.Remove(reloadable);
                return reloadable;
            }
        }


        protected override void BeginRun() {
            Native.PrepareEarly();

            base.BeginRun();
        }

        protected override void EndRun() {
            Config.Save(); // Extra save for safety
            base.EndRun();
        }


        protected override void Initialize() {
            Config.Load();
            Config.Save();

            Components.Add(new OverlayComponent(this));
            Components.Add(new SplashComponent(this));
            Components.Add(new MainComponent(this));
            Components.Add(new CodeWarmupComponent(this));

            Get<SplashComponent>().Locks.Add(this);

            base.Initialize();
        }


        protected override void LoadContent() {
            Graphics.GraphicsDevice.PresentationParameters.RenderTargetUsage = RenderTargetUsage.PlatformContents;
            // Graphics.GraphicsDevice.PresentationParameters.MultiSampleCount = UI.MultiSampleCount;

            SpriteBatch?.Dispose();
            SpriteBatch = new SpriteBatch(GraphicsDevice);

            base.LoadContent();
        }


        protected override void Update(GameTime gameTime) {
            // If we skipped drawing the last frame wait an arbitrary amount to not spam call Update and max cpu usage
            // IsFixedTimeStep is bad in this case since we don't care about sleeping precisely, only an arbitrary amount
            if (UI.GlobalUpdateID != UI.GlobalDrawID) {
                Thread.Sleep(UpdateDelaySpan);
            }
            
            Resizing = false;
            if (ManualUpdate) {
                ManualUpdate = false;
                // FNA has accumulated the elapsed game time for us, turning it unusuable.
                // Let's skip updating properly on one frame instead of having a huge delta time spike...
                gameTime = new();
            }

            if (DrawState == 1) {
                // This MUST happen immediately after the first update + draw + present!
                // Otherwise we risk flickering.
                Native.PrepareLate();
                // We can update multiple times before a draw.
                DrawState = 2;
            }


            if (EnvFlags.IsFullscreen) {
                DisplayMode dm = Graphics.GraphicsDevice.Adapter.CurrentDisplayMode;

                if (!Graphics.IsFullScreen || Graphics.PreferredBackBufferWidth != dm.Width || Graphics.PreferredBackBufferHeight != dm.Height) {
                    Graphics.IsFullScreen = true;

                    Graphics.PreferredBackBufferWidth = dm.Width;
                    Graphics.PreferredBackBufferHeight = dm.Height;
                    Graphics.GraphicsDevice.PresentationParameters.BackBufferWidth = dm.Width;
                    Graphics.GraphicsDevice.PresentationParameters.BackBufferHeight = dm.Height;
                    GraphicsDevice.Reset(Graphics.GraphicsDevice.PresentationParameters);
                }
            }

            if (Graphics.SynchronizeWithVerticalRetrace && !VSync) {
                Graphics.SynchronizeWithVerticalRetrace = false;
                Graphics.GraphicsDevice.PresentationParameters.PresentationInterval = PresentInterval.Immediate;
                GraphicsDevice.Reset(Graphics.GraphicsDevice.PresentationParameters);

            } else if (!Graphics.SynchronizeWithVerticalRetrace && VSync && !ManuallyUpdated) {
                Graphics.SynchronizeWithVerticalRetrace = true;
                Graphics.GraphicsDevice.PresentationParameters.PresentationInterval = PresentInterval.One;
                GraphicsDevice.Reset(Graphics.GraphicsDevice.PresentationParameters);
            }

            Native.Update((float) gameTime.ElapsedGameTime.TotalSeconds);

            lock (TemporaryReloadables) {
                if (TemporaryReloadables.Count > 0) {
                    foreach (IReloadable reloadable in TemporaryReloadables) {
                        if (!reloadable.LifeTick())
                            TemporaryReloadablesDead.Add(reloadable);
                    }
                    if (TemporaryReloadablesDead.Count > 0) {
                        foreach (IReloadable reloadable in TemporaryReloadablesDead) {
                            reloadable.Dispose();
                            TemporaryReloadables.Remove(reloadable);
                        }
                        TemporaryReloadablesDead.Clear();
                    }
                }
            }

            base.Update(gameTime);

            // SuppressDraw();
        }


        protected override void Draw(GameTime gameTime) {
            TimeSpan dtSpan = gameTime.ElapsedGameTime;
            if (dtSpan.Ticks == 0) {
                // User resized the window and FNA doesn't keep track of elapsed time.
                // Apparently not under linux?
                dtSpan = new(GlobalWatch.ElapsedTicks - CountingFramesWatchLast);
                gameTime = new(gameTime.TotalGameTime, dtSpan, gameTime.IsRunningSlowly);
                Resizing = true;
                ManualUpdate = true;
                ManuallyUpdated = true;
            }
            CountingFramesTime += dtSpan;
            CountingFramesWatchLast = GlobalWatch.ElapsedTicks;
            float dt = gameTime.GetDeltaTime();

            Rectangle clientBounds;

            if (Graphics.IsFullScreen) {
                DisplayMode dm = Graphics.GraphicsDevice.Adapter.CurrentDisplayMode;
                clientBounds = new(0, 0, dm.Width, dm.Height);
            } else {
                clientBounds = Window.ClientBounds;
            }

            Resizing &= PrevClientBounds != clientBounds && PrevClientBounds != default;
            if (PrevClientBounds != clientBounds) {
                // FNA resizes the client bounds, but not the backbuffer.
                // Let's help out by enforcing a backbuffer resize in an ugly way.
                // Also, disable forced vsync for smooth resizing, re-enable on next update.

                if (Graphics.SynchronizeWithVerticalRetrace) {
                    Graphics.SynchronizeWithVerticalRetrace = false;
                    Graphics.GraphicsDevice.PresentationParameters.PresentationInterval = PresentInterval.Immediate;
                }

                // Apparently some dual GPU setups can experience severe flickering when resizing the backbuffer repeatedly.
                if (PrevClientBounds.Width != clientBounds.Width ||
                    PrevClientBounds.Height != clientBounds.Height
                ) {
                    PresentationParameters pp = GraphicsDevice.PresentationParameters;
                    if (!Native.ReduceBackBufferResizes) {
                        WidthOverride = HeightOverride = null;
#if false
                        m_GameWindow_OnClientSizeChanged.Invoke(Window, EmptyArgs);
#else
                        pp.BackBufferWidth = clientBounds.Width;
                        pp.BackBufferHeight = clientBounds.Height;
                        GraphicsDevice.Reset(pp);
#endif
                        UnresizedManualDrawCount = -1;
                    } else {
                        WidthOverride = clientBounds.Width;
                        HeightOverride = clientBounds.Height;
                        if (UnresizedManualDrawCount == -1 ||
                            pp.BackBufferWidth < Width ||
                            pp.BackBufferHeight < Height
                        ) {
                            pp.BackBufferWidth = Math.Max(pp.BackBufferWidth, Width + 256);
                            pp.BackBufferHeight = Math.Max(pp.BackBufferHeight, Height + 256);
                            GraphicsDevice.Reset(pp);
                        }
                        UnresizedManualDrawCount = 0;
                    }
                }

                PrevClientBounds = clientBounds;

            } else if (ManuallyUpdated && !ManualUpdate) {
                ManuallyUpdated = false;

                if (!Graphics.SynchronizeWithVerticalRetrace && VSync) {
                    Graphics.SynchronizeWithVerticalRetrace = true;
                    Graphics.GraphicsDevice.PresentationParameters.PresentationInterval = PresentInterval.One;
                }

                // XNA - and thus in turn FNA - love to re-center the window on device changes.
                Point pos = Native.WindowPosition;
                FNAHooks.ApplyWindowChangesWithoutCenter = true;
                m_GameWindow_OnClientSizeChanged.Invoke(Window, EmptyArgs);
                FNAHooks.ApplyWindowChangesWithoutCenter = false;

                // In some circumstances, fixing the window position is required, but only on device changes.
                if (Native.WindowPosition != pos)
                    Native.WindowPosition = Native.FixWindowPositionDisplayDrag(pos);

                WidthOverride = HeightOverride = null;
                UnresizedManualDrawCount = -1;

            } else if (UnresizedManualDrawCount != -1 && UnresizedManualDrawCount++ >= 60) {
                // After enough time of manually drawing with a missized backbuffer, snap back to sharpness.
                // Ironically enough WPF seems to do something similar with text when resizing a window, albeit fading, not snapping.
                UnresizedManualDrawCount = -1;
                PresentationParameters pp = GraphicsDevice.PresentationParameters;
                pp.BackBufferWidth = Width;
                pp.BackBufferHeight = Height;
                GraphicsDevice.Reset(pp);
            }

            if (CountingFramesTime.Ticks >= TimeSpan.TicksPerSecond) {
                CountingFramesTime = new TimeSpan(CountingFramesTime.Ticks % TimeSpan.TicksPerSecond);
                FPS = CountingFrames;
                CountingFrames = 0;
            }
            CountingFrames++;

            if (!Native.CanRenderTransparentBackground) {
                BackgroundOpacityTime = 2.4f;
            } else if (Native.IsActive) {
                BackgroundOpacityTime -= dt * 4f;
                if (BackgroundOpacityTime < 0f)
                    BackgroundOpacityTime = 0f;
            } else {
                BackgroundOpacityTime += dt * 4f;
                if (BackgroundOpacityTime > 1f)
                    BackgroundOpacityTime = 1f;
            }

            GraphicsDevice.Viewport = new(0, 0, Width, Height);
            if (Native.CanRenderTransparentBackground) {
                GraphicsDevice.Clear(ClearOptions.Target, new Vector4(0f, 0f, 0f, 0f), 0, 0);
            } else if (Native.DarkMode) {
                GraphicsDevice.Clear(ClearOptions.Target, new Vector4(0.1f, 0.1f, 0.1f, 1f), 0, 0);
            } else {
                GraphicsDevice.Clear(ClearOptions.Target, new Vector4(0.9f, 0.9f, 0.9f, 1f), 0, 0);
            }
            Native.BeginDrawDirect(dt);

            // FIXME: This should be in a better spot, but Native can edit Viewport which UI relies on and ugh.
            if (ManualUpdate) {
                if (ManualUpdateSkip) {
                    ManualUpdateSkip = false;
                } else {
                    base.Update(gameTime);
                }
            }

            base.Draw(gameTime);
            Native.EndDrawDirect(dt);
            

            if (DrawState == 0) {
                // This needs to happen *after* the first draw, otherwise shader warmup delays the first draw even further.
                Get<SplashComponent>().Locks.Remove(this);
                Components.Add(new ShaderWarmupComponent(this));
                
                // This was the first draw
                DrawState = 1;
            }
        }

        public void ForceRedraw() {
            ForceBeginDraw = true;
            if (DrawState > 0 && BeginDraw()) {
                Draw(new GameTime());
                EndDraw();
            }
        }

        protected override bool BeginDraw() {
            bool shouldDraw = ForceBeginDraw;
            ForceBeginDraw = false;

            for (int i = 0; i < Components.Count; i++)
                if (Components[i] is AppComponent component && component.UpdateDraw())
                    shouldDraw = true;

            return shouldDraw && base.BeginDraw();
        }

        protected override void EndDraw() {
            // FNA calls GraphicsDeviceManager.EndDraw() which calls GraphicsDevice.Present(null, null, NULL)
            if (WidthOverride is null || HeightOverride is null) {
                base.EndDraw();
                return;
            }

            GraphicsDevice gd = GraphicsDevice;
            if (FNAHooks.FNA3DDevice is FNA3DOpenGLDeviceInfo) {
                // OpenGL starts bottom left.
                // In an ideal world FNA3D abstracts this difference away, but I don't have enough time to debug that right now. -ade
                PresentationParameters pp = gd.PresentationParameters;
                gd.Present(
                    new Rectangle(0, pp.BackBufferHeight - Height, Width, pp.BackBufferHeight),
                    null,
                    IntPtr.Zero
                );

            } else {
                // D3D11 starts top left.
                gd.Present(
                    new Rectangle(0, 0, Width, Height),
                    null,
                    IntPtr.Zero
                );
            }
        }

    }
}
