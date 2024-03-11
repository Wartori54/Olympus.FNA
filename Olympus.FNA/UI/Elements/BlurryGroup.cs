using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OlympUI.MegaCanvas;
using System;

namespace OlympUI {
    public partial class BlurryGroup : Group {

        protected Style.Entry StyleRadius = new(new FloatFader(0f));
        protected Style.Entry StyleStrength = new(new FloatFader(1f));
        protected Style.Entry StyleScale = new(new FloatFader(2f));
        protected Style.Entry StyleNoise = new(new FloatFader(0f));

        public override bool? Cached => true;
        public override bool Clip => true;
        public override Padding ClipExtend => new(0);

        private uint PrevCachedPaintID;

        private RenderTarget2DRegion? BlurredX;
        private RenderTarget2DRegion? BlurredXY;
        private RenderTarget2DRegion? Noised;
        private RenderTarget2DRegion? Display;

        private Rectangle DisplayBounds;

        public BlurryGroup() {
        }

        protected override void Dispose(bool disposing) {
            if (IsDisposed)
                return;
            base.Dispose(disposing);

            BlurredX?.Dispose();
            BlurredXY?.Dispose();
            Noised?.Dispose();
        }

        protected override void DrawCachedTexture(RenderTarget2DRegion rt, Vector2 xy, Padding padding, Point size) {
            StyleRadius.GetCurrent(out int radius);
            if (radius <= 0) {
                base.DrawCachedTexture(rt, xy, padding, size);
                return;
            }

            UIDraw.AddDependency(rt);

            StyleScale.GetCurrent(out float scale);
            Point whFull = WH;
            Point whMin = new((int) (whFull.X / scale), (int) (whFull.Y / scale));

            RenderTarget2DRegion? display = Display;
            if (PrevCachedPaintID != CachedPaintID || display is null) {
                PrevCachedPaintID = CachedPaintID;

                StyleStrength.GetCurrent(out float strength);
                StyleNoise.GetCurrent(out float noise);

                BlurEffect blurrer = (BlurEffect) BlurEffect.Cache.GetEffect(() => UI.Game.GraphicsDevice, radius).Value;
                blurrer.Radius = radius;
                blurrer.Strength = strength;

                RenderTarget2DRegion? blurredX = BlurredX;
                if (blurredX is not null && (blurredX.RT.IsDisposed || blurredX.RT.Width < whMin.X || blurredX.RT.Height < whFull.Y)) {
                    blurredX?.Dispose();
                    BlurredX = blurredX = null;
                }
                if (blurredX is null) {
                    BlurredX = blurredX = (CachePool ?? UI.MegaCanvas.PoolMSAA).Get(whMin.X, whFull.Y);
                }
                if (blurredX is null) {
                    throw new Exception($"{nameof(BlurryGroup)} tried obtaining blurred X render target but got null");
                }

                RenderTarget2DRegion? blurredXY = BlurredXY;
                if (blurredXY is not null && (blurredXY.RT.IsDisposed || blurredXY.RT.Width < whMin.X || blurredXY.RT.Height < whMin.Y)) {
                    blurredXY?.Dispose();
                    BlurredXY = blurredXY = null;
                }
                if (blurredXY is null) {
                    BlurredXY = blurredXY = (CachePool ?? UI.MegaCanvas.PoolMSAA).Get(whMin.X, whMin.Y);
                }
                if (blurredXY is null) {
                    throw new Exception($"{nameof(BlurryGroup)} tried obtaining blurred XY render target but got null");
                }

                static void DrawBlurStep((RenderTarget2DRegion rt, RenderTarget2DRegion blurred, BlurEffect blurrer, BlurAxis axis, Rectangle src, Rectangle dest) data) {
                    GraphicsDevice gd = UI.Game.GraphicsDevice;
                    SpriteBatch spriteBatch = UI.SpriteBatch;

                    data.blurred.RT.SetRenderTargetUsage(RenderTargetUsage.PlatformContents);
                    gd.SetRenderTarget(data.blurred.RT);
                    data.blurrer.Axis = data.axis;
                    data.blurrer.TransformParam.Value = UI.CreateTransform(-UI.TransformOffset);
                    data.blurred.RT.SetRenderTargetUsage(RenderTargetUsage.PreserveContents);

                    spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.LinearClamp, DepthStencilState.None, RasterizerState.CullCounterClockwise, data.blurrer);
                    spriteBatch.Draw(data.rt.RT, data.dest, data.src, Color.White);
                    spriteBatch.End();
                }

                UIDraw.Push(blurredX, null);
                UIDraw.Recorder.Add(
                    (rt, blurredX, blurrer, BlurAxis.X, rt.Region.WithSize(size), new Rectangle(0, 0, whMin.X, whFull.Y)),
                    DrawBlurStep
                );
                UIDraw.Pop();

                UIDraw.Push(blurredXY, null);
                UIDraw.AddDependency(blurredX);
                UIDraw.Recorder.Add(
                    (blurredX, blurredXY, blurrer, BlurAxis.Y, new Rectangle(0, 0, whMin.X, whFull.Y), new Rectangle(0, 0, whMin.X, whMin.Y)),
                    DrawBlurStep
                );
                UIDraw.Pop();

                if (noise >= 0f) {
                    RenderTarget2DRegion? noised = Noised;
                    if (noised is not null && (noised.RT.IsDisposed || noised.RT.Width < whFull.X || noised.RT.Height < whFull.Y)) {
                        noised?.Dispose();
                        Noised = noised = null;
                    }
                    if (noised is null) {
                        Noised = noised = (CachePool ?? UI.MegaCanvas.PoolMSAA).Get(whFull.X, whFull.Y);
                    }
                    if (noised is null) {
                        throw new Exception($"{nameof(BlurryGroup)} tried obtaining noised render target but got null");
                    }

                    NoiseEffect noiser = (NoiseEffect) NoiseEffect.Cache.GetEffect(() => UI.Game.GraphicsDevice).Value;
                    noiser.Spread = new(
                        noise * (whMin.X / (float) noised.RT.Width) * 0.01f,
                        noise * (whMin.Y / (float) noised.RT.Height) * 0.01f
                    );
                    noiser.Blend = noise * 0.8f;

                    UIDraw.Push(noised, null);
                    UIDraw.AddDependency(blurredXY);
                    UIDraw.Recorder.Add(
                        (blurredXY, noised, noiser, new Rectangle(0, 0, whMin.X, whMin.Y), new Rectangle(0, 0, whFull.X, whFull.Y)),
                        static ((RenderTarget2DRegion blurred, RenderTarget2DRegion noised, NoiseEffect noiser, Rectangle src, Rectangle dest) data) => {
                            GraphicsDevice gd = UI.Game.GraphicsDevice;
                            SpriteBatch spriteBatch = UI.SpriteBatch;

                            data.noised.RT.SetRenderTargetUsage(RenderTargetUsage.PlatformContents);
                            gd.SetRenderTarget(data.noised.RT);
                            data.noiser.TransformParam.Value = UI.CreateTransform(-UI.TransformOffset);
                            data.noised.RT.SetRenderTargetUsage(RenderTargetUsage.PreserveContents);
                            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.LinearClamp, DepthStencilState.None, RasterizerState.CullCounterClockwise, data.noiser);
                            spriteBatch.Draw(data.blurred.RT, data.dest, data.src, Color.White);
                            spriteBatch.End();
                        }
                    );
                    UIDraw.Pop();

                    Display = display = noised;
                    DisplayBounds = new(0, 0, whFull.X, whFull.Y);
                } else {
                    Noised?.Dispose();
                    Noised = null;
                    Display = display = blurredXY;
                    DisplayBounds = new(0, 0, whMin.X, whMin.Y);
                }
            }

            DrawModifiable(new UICmd.Sprite(
                display.RT,
                DisplayBounds,
                (xy.ToPoint() - padding.LT).WithSize(size),
                Color.White
            ));
        }

    }
}
