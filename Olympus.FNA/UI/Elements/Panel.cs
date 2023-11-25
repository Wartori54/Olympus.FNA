using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OlympUI.MegaCanvas;
using System;
using System.Diagnostics;

namespace OlympUI {
    public unsafe partial class Panel : Group {

        protected Style.Entry StyleBackground = new(new ColorFader(0x08, 0x08, 0x08, 0xd0));
        protected Style.Entry StyleBorder = new(new ColorFader(0x08, 0x08, 0x08, 0xd0));
        protected Style.Entry StyleBorderSize = new(new FloatFader(0f));
        protected Style.Entry StyleShadow = new(new FloatFader(1f));
        protected Style.Entry StyleRadius = new(new FloatFader(8f));
        protected new Style.Entry StylePadding = new(8);
        protected new Style.Entry StyleSpacing = new(8);

        private BasicMesh BackgroundMesh;
        private BasicMesh? ContentsMesh;
        private (IReloadable<RenderTarget2DRegion, RenderTarget2DRegionMeta> Region, IReloadable<RenderTarget2D, Texture2DMeta> RT)? ContentsWrap;
        private IReloadable<Texture2D, Texture2DMeta>? ContentsCurrent;
        private Color PrevBackground;
        private Color PrevBorder;
        private float PrevBorderSize;
        private float PrevShadow;
        private float PrevRadius;
        private bool PrevClip;
        private Point PrevWH;
        private Point PrevContentsXY;
        private Point PrevContentsWH;

        public Panel() {
            ClipExtend = 16;
            BackgroundMesh = new(UI.Game) {
                Texture = Assets.GradientQuadY
            };
        }

        protected override void Dispose(bool disposing) {
            if (IsDisposed)
                return;
            base.Dispose(disposing);

            BackgroundMesh?.Dispose();
            ContentsMesh?.Dispose();
            ContentsWrap?.RT.Dispose();
            ContentsWrap = null;
        }

        public override void DrawContent() {
            Vector2 xy = ScreenXY;
            Point wh = WH;

            StyleBackground.GetCurrent(out Color background);
            StyleBorder.GetCurrent(out Color border);
            StyleBorderSize.GetCurrent(out float borderSize);
            StyleShadow.GetCurrent(out float shadow);
            StyleRadius.GetCurrent(out float radius);

            int padding = (int) MathF.Ceiling(10 * shadow);
            Point whPadded = new(wh.X + padding * 2, wh.Y + padding * 2);

            if (PrevBackground != background ||
                PrevBorder != border ||
                PrevBorderSize != borderSize ||
                PrevShadow != shadow ||
                PrevRadius != radius ||
                PrevClip != Clip ||
                PrevWH != wh) {
                MeshShapes<MiniVertex> shapes = BackgroundMesh.Shapes;
                shapes.Clear();

                int shadowIndex = -1;
                int shadowEnd = -1;

                if (shadow >= 0.01f) {
                    float shadowWidth = 8f * shadow;
                    shapes.Prepare(out shadowIndex, out _);
                    shapes.Add(new MeshShapes.Rect() {
                        Color = Color.Black * (0.1f + 0.3f * Math.Min(1f, shadow * 0.125f)),
                        XY1 = new(-shadowWidth, -shadowWidth),
                        Size = new(wh.X + shadowWidth * 2f, wh.Y + shadowWidth * 2f),
                        Radius = Math.Max(shadowWidth, radius),
                        Border = shadowWidth,
                    });
                    shadowEnd = shapes.VerticesMax;

                    // Turn outer edge transparent and move it if necessary.
                    for (int i = shadowIndex; i < shapes.VerticesMax; i += 2) {
                        ref MiniVertex vertex = ref shapes.Vertices[i];
                        vertex.Color = Color.Transparent;
                        vertex.UV = new(0f, 0f);
                        // vertex.Position.X += Math.Sign(vertex.Position.X - shadowWidth) * shadowWidth * 0.125f;
                        if (vertex.Y <= shadowWidth) {
                            // vertex.Y += shadowWidth * 0.5f;
                        } else {
                            vertex.Y += shadowWidth * 1.5f;
                        }
                    }

                    // Fix shadow inner vs rect outer radius gap by adjusting the shadow inner edge to the rect outer edge.
                    // While at it, turn inner edge more transparent at the top.
                    MeshShapes<MiniVertex> tmp = new() {
                        AutoPoints = shapes.AutoPoints
                    };
                    tmp.Add(new MeshShapes.Rect() {
                        Size = new(wh.X, wh.Y),
                        Radius = Math.Max(0.0001f, radius),
                        Border = 1f,
                    });
                    for (int i = shadowIndex + 1; i < shapes.VerticesMax; i += 2) {
                        ref MiniVertex vertex = ref shapes.Vertices[i];
                        ref MiniVertex gen = ref tmp.Vertices[i - shadowIndex - 1];
                        if ((shadowWidth * 2f + 8f) > wh.Y && (float) vertex.Y <= shadowWidth + 1)
                            vertex.Color *= 0.9f + 0.1f * Math.Min(1f, shadow - 1f);
                        vertex.UV = new(1f, 1f);
                        vertex.X = gen.X;
                        vertex.Y = gen.Y;
                    }
                }

                if (background != default) {
                    shapes.Add(new MeshShapes.Rect() {
                        Color = background,
                        Size = new(wh.X, wh.Y),
                        Radius = radius,
                    });
                }

                if (border != default && borderSize >= 0.01f && border.A >= 1) {
                    shapes.Add(new MeshShapes.Rect() {
                        Color = border,
                        Size = new(wh.X, wh.Y),
                        Radius = radius,
                        Border = borderSize,
                    });
                } else if (border != default && borderSize <= -0.01f && border.A >= 1) {
                    float paddedSize = borderSize + 0.25f; // Apply slight padding to avoid seams
                    shapes.Add(new MeshShapes.Rect() {
                        Color = border,
                        XY1 = new(paddedSize, paddedSize),
                        Size = new(wh.X + Math.Abs(2 * paddedSize), wh.Y + Math.Abs(2 * paddedSize)),
                        Radius = radius,
                        Border = Math.Abs(borderSize),
                    });
                }

                // Fix UVs manually as we're using a gradient texture.
                for (int i = 0; i < shapes.VerticesMax; i++) {
                    ref MiniVertex vertex = ref shapes.Vertices[i];
                    if (shadowIndex <= i && i < shadowEnd)
                        continue;
                    vertex.UV = new(1f, 1f);
                }

                shapes.AutoApply();
            }

            if (Clip) {
                Padding selfPadding = Padding;

                Point contentsXY;

                if (Children.Count == 1 && Children[0] is { } child &&
                    child.PaintToCache(selfPadding) is { } childCache) {
                    ContentsWrap?.RT.Dispose();
                    ContentsWrap = null;

                    ContentsCurrent = new ReloadableAs<RenderTarget2D, Texture2D, Texture2DMeta>(childCache.UnpackRT(false));
                    contentsXY = child.RealXY.ToPoint() - selfPadding.LT;

                } else {
                    if (ContentsWrap is not null && (ContentsWrap?.RT.ValueValid is not { } rt || rt.Width < wh.X || rt.Height < wh.Y)) {
                        ContentsWrap?.RT.Dispose();
                        ContentsWrap = null;
                        if (ContentsMesh is not null)
                            ContentsMesh.Texture = Assets.White;
                    }

                    if (ContentsWrap is null) {
                        IReloadable<RenderTarget2DRegion, RenderTarget2DRegionMeta> region =
                            Reloadable.Temporary(default(RenderTarget2DRegionMeta), () => UI.MegaCanvas.PoolMSAA.Get(wh.X, wh.Y), true);
                        ContentsWrap = (region, region.UnpackRT(true));
                        PrevContentsWH = default;
                    }

                    UIDraw.Push(ContentsWrap.Value.Region.Value, -xy);

                    base.DrawContent();

                    UIDraw.Pop();

                    ContentsCurrent = new ReloadableAs<RenderTarget2D, Texture2D, Texture2DMeta>(ContentsWrap.Value.RT);
                    contentsXY = default;
                }

                Point contentsWH = new(ContentsCurrent.Meta.Width, ContentsCurrent.Meta.Height);

                if (ContentsMesh is null ||
                    PrevBackground != background ||
                    PrevRadius != radius ||
                    PrevContentsXY != contentsXY ||
                    PrevContentsWH != contentsWH ||
                    PrevWH != wh) {
                    PrevContentsXY = contentsXY;
                    PrevContentsWH = contentsWH;

                    if (ContentsMesh is null) {
                        ContentsMesh = new BasicMesh(UI.Game);
                        ContentsMesh.Reload();
                    }

                    MeshShapes<MiniVertex> shapes = ContentsMesh.Shapes;
                    shapes.Clear();

                    shapes.Add(new MeshShapes.Rect() {
                        Color = Color.White,
                        Size = new(wh.X, wh.Y),
                        UVXYMin = new(0, 0),
                        UVXYMax = new(contentsWH.X, contentsWH.Y),
                        Radius = radius,
                    });

                    shapes.AutoApply();
                }

            } else {
                ContentsCurrent = null;
                ContentsWrap?.RT.Dispose();
                ContentsWrap = null;
                ContentsMesh?.Dispose();
                ContentsMesh = null;
            }

            if (ContentsCurrent is not null) {
                // ContentsMesh is created earlier in this method if it's null and if clipping is enabled.
                Debug.Assert(ContentsMesh is not null);
                ContentsMesh.Texture = ContentsCurrent;
            }

            UIDraw.Recorder.Add(
                (BackgroundMesh, ContentsMesh, xy),
                static ((BasicMesh background, BasicMesh? contents, Vector2 xy) data) => {
                    UI.SpriteBatch.End();

                    Matrix transform = UI.CreateTransform(data.xy);
                    data.background.Draw(transform);
                    data.contents?.Draw(transform);

                    UI.SpriteBatch.BeginUI();
                }
            );

            if (!Clip)
                base.DrawContent();

            PrevBackground = background;
            PrevBorder = border;
            PrevBorderSize = borderSize;
            PrevShadow = shadow;
            PrevRadius = radius;
            PrevClip = Clip;
            PrevWH = wh;
        }

    }
}
