using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OlympUI;
using System;

namespace Olympus {
    public class CodeWarmupComponent : AppComponent {

        public CodeWarmupComponent(App app)
            : base(app) {
        }

        public override void Initialize() {
            base.Initialize();

            // Warm up certain things separately. This helps with multi-threaded init.

            // CreateDevice can wait for a while when FNA asks SDL2 for all adapters.
            GraphicsAdapter.DefaultAdapter.IsProfileSupported(0);

            if (UI.Game is null) {
                // Trying to run Olympus without the main component, let's initialize UI ourselves.
                UI.Initialize(App, Native, App);
            }

            // The first UI input update is chonky.
            UIInput.Update();

            // The first UI update is very chonky with forced relayouts, element inits, scans and whatnot.
            UI.Update(0f);

            // LibTessDotNet can be chonky.
            new MeshShapes<MiniVertex>().Add(new MeshShapes.Rect {
                Size = new Vector2(100, 100),
                Radius = 50
            });
        }

        public override bool UpdateDraw() {
            return true;
        }

        public override void Draw(GameTime gameTime) {
            App.Components.Remove(this);
        }

    }
}
