using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using OlympUI;
using System;
using System.IO;

namespace Olympus {
    public class MainComponent : AppComponent {

        private Skin SkinDefault;
        private Skin? SkinForce;
        private Skin? SkinDark;
        private Skin? SkinLight;

        private Label DebugLabel;

        private bool AlwaysRepaint;

        public MainComponent(App app)
            : base(app) {
            SkinDefault = Skin.CreateDump();
            SkinDark = SkinDefault;
            SkinLight = Skin.CreateLight();

            UI.Initialize(App, Native, App);
            UI.Root.Children.Add(Scener.Get<MetaMainScene>().Root);
            UI.Root.Children.Add(Scener.Get<MetaAlertScene>().Generate());
            UI.Root.Children.Add(Scener.Get<MetaDebugScene>().Generate());
            UI.Root.Children.Add(DebugLabel = new Label("") {
                Style = {
                    Color.Red,
                    OlympUI.Assets.FontMonoOutlined,
                },
#if DEBUG
                Visible = true,
#else
                Visible = false,
#endif
            });
            Scener.Push<HomeScene>();

            App.FinderManager.Refresh();

#if DEBUG
            AlwaysRepaint = true;
            App.VSync = false;
#endif
        }

        protected override void LoadContent() {
            UI.LoadContent();

            // Otherwise it keeps fooling itself.
            DebugLabel.CachePool = new(UI.MegaCanvas, false);

            base.LoadContent();
        }

        public override void Update(GameTime gameTime) {
            float dt = gameTime.GetDeltaTime();

            if (UIInput.Pressed(Keys.Escape) && !(Scener.Front?.Locked ?? false)) {
                Scener.PopFront();
            }

            if (UIInput.Pressed(Keys.F1)) {
                DebugLabel.Visible = !DebugLabel.Visible;
                UI.Root.Repainting = true;
            }

            if (UIInput.Pressed(Keys.F2)) {
                if (UIInput.Down(Keys.LeftShift)) {
                    OlympUI.Assets.ReloadID++;
                } else {
                    Native.DarkMode = !Native.DarkMode;
                }
            }

            if (UIInput.Pressed(Keys.F3) && Scener.Front != null) {
                Type sceneType = Scener.Front.GetType();
                Scener.PopFront();
                Scener.Push(Scener.Regenerate(sceneType));
            }

            if (UIInput.Pressed(Keys.F5)) {
                string path = Path.Combine(Environment.CurrentDirectory, "skin.yaml");
                Console.WriteLine($"Loading skin from {path}");
                if (!File.Exists(path)) {
                    SkinForce = null;
                } else {
                    using StreamReader reader = new(path);
                    SkinForce = Skin.Deserialize(reader);
                }
            }

            if (UIInput.Pressed(Keys.F6)) {
                Scener.Front?.Refresh();
            }

            if (UIInput.Pressed(Keys.F7)) {
                if (UIInput.Down(Keys.LeftShift)) {
                    string path = Path.Combine(Environment.CurrentDirectory, "megacanvas");
                    Console.WriteLine($"Dumping megacanvas to {path}");
                    if (Directory.Exists(path))
                        Directory.Delete(path, true);
                    Directory.CreateDirectory(path);
                    UI.MegaCanvas.Dump(path);
                } else {
                    string path = Path.Combine(Environment.CurrentDirectory, "skin.yaml");
                    Console.WriteLine($"Dumping skin to {path}");
                    using StreamWriter writer = new(new FileStream(path, FileMode.Create));
                    Skin.Serialize(writer, Skin.CreateDump());
                }
            }

            if (UIInput.Pressed(Keys.F8)) {
                // Run debug code that prints to stdout here
            }

            if (UIInput.Pressed(Keys.F10)) {
                AlwaysRepaint = !AlwaysRepaint;
                App.VSync = !AlwaysRepaint;
            }

            if (UIInput.Pressed(Keys.F11)) {
                UI.GlobalDrawDebug = !UI.GlobalDrawDebug;
                UI.GlobalRepaintID++;
            }

            if (Skin.Current != (Skin.Current = SkinForce ?? (Native.DarkMode ? SkinDark : SkinLight))) {
                UI.GlobalRepaintID++;
            }

            // FIXME: WHY IS YET ANOTHER ROW OF PIXELS MISSING?! Is this an OlympUI bug or another Windows quirk?
            // FIXME: Size flickering in maximized mode on Windows when using viewport size for UI root size.
            // UI.Root.WH = new(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height - (Native.IsMaximized ? 8 : 1));
            UI.Root.WH = new(App.Width, App.Height - (Native.IsMaximized ? 8 : 1));

            if (DebugLabel.Visible) {
                DebugLabel.Text =
                    $"FPS: {App.FPS}\n" +
                    $"Repaint Mode: {(AlwaysRepaint ? "always (debug)" : "on demand")}\n" +
                    $"Mouse: {UIInput.Mouse}\n" +
                    $"Root Size: {UI.Root.WH.X} x {UI.Root.WH.Y}\n" +
                    $"App Size: {App.Width} x {App.Height} ({(Native.IsMaximized ? "maximized" : "windowed")})\n" +
                    $"Reloadable Texture2D Used: {TextureTracker.Instance.UsedCount} / {TextureTracker.Instance.TotalCount}\n" +
                    $"Reloadable Texture2D Memory: {GetHumanFriendlyBytes(TextureTracker.Instance.UsedMemory)} / {GetHumanFriendlyBytes(TextureTracker.Instance.TotalMemory)}\n" +
                    $"Pool MAIN Available: {UI.MegaCanvas.Pool.EntriesAlive}\n" +
                    $"Pool MAIN Used: {UI.MegaCanvas.Pool.Used.Count}\n" +
                    $"Pool MAIN Memory: {GetHumanFriendlyBytes(UI.MegaCanvas.Pool.UsedMemory)} / {GetHumanFriendlyBytes(UI.MegaCanvas.Pool.TotalMemory)} \n" +
                    $"Pool MSAA Available: {UI.MegaCanvas.PoolMSAA.EntriesAlive}\n" +
                    $"Pool MSAA Used: {UI.MegaCanvas.PoolMSAA.Used.Count}\n" +
                    $"Pool MSAA Memory: {GetHumanFriendlyBytes(UI.MegaCanvas.PoolMSAA.UsedMemory)} / {GetHumanFriendlyBytes(UI.MegaCanvas.PoolMSAA.TotalMemory)} \n" +
                    $"Atlas Pages: {UI.MegaCanvas.Pages.Count} x {GetHumanFriendlyBytes(UI.MegaCanvas.PageSize * UI.MegaCanvas.PageSize * 4)} \n" +
                    "";
            }

            Scener.Update(dt);
            UI.Update(dt);
        }

        public override bool UpdateDraw() {
            UI.UpdateDraw();

            return AlwaysRepaint || UI.Root.Repainting;
        }

        public override void Draw(GameTime gameTime) {
#if false
            SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.Default, RasterizerState.CullCounterClockwise);

            SpriteBatch.DrawString(Data.FontMono, $"{App.Window.ClientBounds.Width} x {App.Window.ClientBounds.Height}\n{GraphicsDevice.PresentationParameters.BackBufferWidth} x {GraphicsDevice.PresentationParameters.BackBufferHeight}\n{App.FPS} FPS", new Vector2(0, 0), Color.White);

            SpriteBatch.DrawString(Data.Font, " / / / /\n / / / /\n / / / /", new Vector2(0, 80), Color.Red);
            SpriteBatch.DrawString(Data.Font, "/ / / / \n/ / / / \n/ / / / ", new Vector2(4, 80), Color.Blue);
            SpriteBatch.DrawString(Data.Font, "The quick brown\nfox jumps over\nthe lazy dog", new Vector2(160, 80), Color.White);

#if false
            const int cornerSize = 8;
            SpriteBatch.Draw(Assets.White, new Rectangle(                           0,                             0, cornerSize, cornerSize), App.IsActive ? Color.Green : Color.Red);
            SpriteBatch.Draw(Assets.White, new Rectangle((int) App.Width - cornerSize,                             0, cornerSize, cornerSize), Native.IsActive ? Color.Green : Color.Red);
            SpriteBatch.Draw(Assets.White, new Rectangle(                           0, (int) App.Height - cornerSize, cornerSize, cornerSize), Color.Red);
            SpriteBatch.Draw(Assets.White, new Rectangle((int) App.Width - cornerSize, (int) App.Height - cornerSize, cornerSize, cornerSize), Color.Red);
#endif

            SpriteBatch.End();
#endif

            UI.Paint();
        }

        private static string GetHumanFriendlyBytes(long bytes) {
            return bytes switch {
                >= 1024 * 1024 * 1024 => $"{bytes / (1024f * 1024f * 1024f):N3} GB",
                >= 1024 * 1024 => $"{bytes / (1024f * 1024f):N3} MB",
                >= 1024 => $"{bytes / 1024f:N3} KB",
                _ => $"{bytes} B",
            };
        }

    }
}
