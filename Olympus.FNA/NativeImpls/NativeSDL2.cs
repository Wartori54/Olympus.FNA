using Microsoft.Xna.Framework;
using OlympUI;
using SDL2;

namespace Olympus.NativeImpls {
    public unsafe partial class NativeSDL2 : NativeImpl {

        public override bool CanRenderTransparentBackground => false;
        public override bool IsActive => App.IsActive;
        public override bool IsMaximized => false;
        public override Point WindowPosition {
            get => App.Window.ClientBounds.Location;
            set {
            }
        }

        public override bool? DarkModePreferred => null;
        public override bool DarkMode {
            get => Config.Instance.Theme == "dark";
            set => Config.Instance.Theme = value ? "dark" : "light";
        }

        public override Color Accent => new(0x00, 0xad, 0xee, 0xff);

        public override Point SplashSize => default;
        public override Color SplashColorFG => default;
        public override Color SplashColorBG => default;

        public override bool BackgroundBlur {
            get => false;
            set {
            }
        }

        public override bool ReduceBackBufferResizes => false;

        public override Padding Padding => default;
        public override ClientSideDecorationMode ClientSideDecoration => EnvFlags.UserCSD ?? ClientSideDecorationMode.None;

        public override bool IsMultiThreadInit => false;


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


        public override void Run() {
            using App app = App = new();
            app.Run();
        }

        public override void Dispose() {
            App.Dispose();
        }

        public override void PrepareEarly() {
        }

        public override void PrepareLate() {
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

        public override void BeginDrawDirect(float dt) {
        }

        public override void EndDrawDirect(float dt) {
        }

    }
}
