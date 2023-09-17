using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OlympUI;
using System;
using System.Collections.Generic;

namespace Olympus {
    public class SplashComponent : AppComponent {

        public float Time;
        public float TimeLeft = 1f;

        public HashSet<object> Locks = new();

        private Reloadable<Texture2D, Texture2DMeta>? SplashMain;
        private Reloadable<Texture2D, Texture2DMeta>? SplashWheel;

        private bool Ready;

        public SplashComponent(App app)
            : base(app) {

            DrawOrder = 1000001;
        }

        public override void Initialize() {
            base.Initialize();

            SplashMain = OlympUI.Assets.GetTexture("splash_main");
            SplashWheel = OlympUI.Assets.GetTexture("splash_wheel");
        }

        public override bool UpdateDraw() {
            return true;
        }

        public override void Draw(GameTime gameTime) {
            float dt = gameTime.GetDeltaTime();

            if (TimeLeft < 0f && Ready) {
                App.Components.Remove(this);
                return;
            }

            Point size = Native.SplashSize;
            if (size == default) {
                TimeLeft = 0f;

            } else if (SplashMain is { } splashMain && SplashWheel is { } splashWheel) {
                Vector2 wheelOffset = new(551, 667);

                Texture2D main = splashMain.Value;
                Texture2D wheel = splashWheel.Value;
                float alpha = Ease.QuadInOut(Math.Max(0f, Math.Min(1f, TimeLeft)));
                float scale = 1f + 0.5f * (1f - Ease.QuadOut(Math.Max(0f, Math.Min(1f, TimeLeft))));
                float width = size.X * scale;
                float height = size.Y * scale;

                SpriteBatch.Begin();
                SpriteBatch.Draw(
                    OlympUI.Assets.White.Value,
                    new Vector2(0f, 0f),
                    null,
                    Native.SplashColorBG * alpha,
                    0f, Vector2.Zero,
                    new Vector2(App.Width, App.Height),
                    SpriteEffects.None, 0f
                );
                SpriteBatch.Draw(
                    main,
                    new Vector2(
                        (App.Width / 2) - width * 0.5f,
                        (App.Height / 2) - height * 0.5f 
                    ),
                    null,
                    Native.SplashColorFG * alpha * alpha,
                    0f,
                    Vector2.Zero,
                    new Vector2(
                        width / main.Width,
                        height / main.Height
                    ),
                    SpriteEffects.None, 0f
                );
                SpriteBatch.Draw(
                    wheel,
                    new Vector2(
                        (App.Width / 2) - width * 0.5f + wheelOffset.X / wheel.Width * width,
                        (App.Height / 2) - height * 0.5f + wheelOffset.Y / wheel.Height * height
                    ),
                    null,
                    Native.SplashColorBG * alpha * alpha,
                    Time * 2f,
                    wheelOffset,
                    new Vector2(
                        width / wheel.Width,
                        height / wheel.Height
                    ),
                    SpriteEffects.None, 0f
                );
                SpriteBatch.End();
            }

            Time += dt;
            if (Locks.Count == 0) {
                if (!Ready) {
                    Ready = true;
                    AppLogger.Log.Information($"Total time until ready: {App.GlobalWatch.Elapsed}");
                }

                TimeLeft -= dt * 3f;
            }
        }

    }
}
