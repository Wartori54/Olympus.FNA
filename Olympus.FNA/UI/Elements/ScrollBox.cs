using Microsoft.Xna.Framework;
using System;
using System.Collections.Specialized;

namespace OlympUI {
    public partial class ScrollBox : Group {

        public Element Content {
            get => this[0];
            set {
                Children.Clear();
                Children.Add(value);
            }
        }

        public ScrollHandle ScrollHandleX;
        public ScrollHandle ScrollHandleY;

        public int Wiggle = 4;

        private bool bottomSticky = false; 
        public bool BottomSticky {
            get => bottomSticky;
            set {
                stuckBottom = value;
                bottomSticky = value;
            }
        }

        private bool stuckBottom;

        public Vector2 ScrollDXY;
        private Vector2 ScrollDXYPrev;
        private Vector2 ScrollDXYMax;
        private float ScrollDXYTime;

        protected Style.Entry StyleBarPadding = new(4);

        public ScrollBox() {
            Cached = false;
            ClipExtend = 0;
            Interactive = InteractiveMode.Process;
            Content = new NullElement();
            ScrollHandleX = new(ScrollAxis.X);
            ScrollHandleY = new(ScrollAxis.Y);
            stuckBottom = BottomSticky;
        }

        public override void Update(float dt) {
            int index;
            if ((index = Children.IndexOf(ScrollHandleX)) != 1) {
                if (index != -1)
                    Children.RemoveAt(index);
                Children.Insert(1, ScrollHandleX);
            }
            if ((index = Children.IndexOf(ScrollHandleY)) != 2) {
                if (index != -1)
                    Children.RemoveAt(index);
                Children.Insert(2, ScrollHandleY);
            }

            if (ScrollDXY != default) {
                if (ScrollDXY != ScrollDXYPrev) {
                    ScrollDXYMax = ScrollDXY * 0.1f;
                    ScrollDXYTime = 0f;
                    if (BottomSticky && stuckBottom && ScrollDXY.Y < ScrollDXYPrev.Y) {
                        stuckBottom = false;
                    }
                }
                ScrollDXYTime += dt * 4f;
                if (ScrollDXYTime > 1f)
                    ScrollDXYTime = 1f;
                ScrollDXYPrev = ScrollDXY = ScrollDXYMax * (1f - Ease.CubeOut(ScrollDXYTime));

                if (Math.Abs(ScrollDXY.X) < 1f)
                    ScrollDXY.X = 0f;
                if (Math.Abs(ScrollDXY.Y) < 1f)
                    ScrollDXY.Y = 0f;

                ForceScroll(ScrollDXY.ToPoint());
            }
            
            if (stuckBottom)
                ForceScroll(new Point(0, int.MaxValue));

            Element content = Content;
            Vector2 origXY = content.XY;
            Vector2 xy = origXY;
            Point wh = content.WH;
            Point boxWH = WH;

            xy.X = Math.Min(0, Math.Max(xy.X + wh.X, boxWH.X) - wh.X);
            xy.Y = Math.Min(0, Math.Max(xy.Y + wh.Y, boxWH.Y) - wh.Y);

            if (xy != origXY) {
                content.XY = xy;
                AfterScroll();
            }

            base.Update(dt);
        }

        public void AfterScroll() {
            InvalidateFull();
#if false
            // Can fail as it gets stuck in a loop. Doesn't seem to be necessary anymore anyways.
            Content.InvalidatePaint();
            Content.ForceFullReflow();
            ScrollHandleX.InvalidatePaint();
            ScrollHandleX.ForceFullReflow();
            ScrollHandleY.InvalidatePaint();
            ScrollHandleY.ForceFullReflow();
#else
            Content.InvalidateFull();
            ScrollHandleX.InvalidateFull();
            ScrollHandleY.InvalidateFull();
#endif
        }

        public void ForceScroll(Point dxy) {
            if (dxy == default)
                return;

            Element content = Content;
            Vector2 xy = -content.XY;
            Vector2 wh = content.WH.ToVector2();
            Vector2 boxWH = WH.ToVector2();

            xy += dxy.ToVector2();

            if (xy.X < 0) {
                xy.X = 0;
            } else if (wh.X < xy.X + boxWH.X) {
                xy.X = wh.X - boxWH.X;
            }

            if (xy.Y < 0) {
                xy.Y = 0;
            } else if (wh.Y < xy.Y + boxWH.Y) {
                xy.Y = wh.Y - boxWH.Y;
                if (BottomSticky)
                    stuckBottom = true;
            }

            content.XY = (-xy).Round();

            AfterScroll();
        }

        private void OnScroll(MouseEvent.Scroll e) {
            if (!Contains(e.XY))
                return;

            Point dxy = e.ScrollDXY;
            dxy = new(
                dxy.X * -32,
                dxy.Y * -32
            );
            ScrollDXY += dxy.ToVector2();
            ForceScroll(dxy);
            e.Cancel();
        }

        public override void Resize(Point wh) {
            // TODO: The Lua counterpart entirely no-op'd this. How should this behave?
            if (wh.X != WHFalse) {
                if (wh.Y != WHFalse) {
                    WH = wh;
                } else {
                    WH.X = wh.X;
                }
            } else if (wh.Y != WHFalse) {
                WH.Y = wh.Y;
            }
        }

    }

    public enum ScrollAxis {
        X,
        Y,
    }

    public partial class ScrollHandle : Element {

        public static readonly new Style DefaultStyle = new() {
            {
                StyleKeys.Normal,
                new Style() {
                    new Color(0x80, 0x80, 0x80, 0xa0),
                    { StyleKeys.Width, 3f },
                    { StyleKeys.Radius, 3f },
                }
            },

            {
                StyleKeys.Disabled,
                new Style() {
                    new Color(0x00, 0x00, 0x00, 0x00),
                    { StyleKeys.Width, 3f },
                    { StyleKeys.Radius, 3f },
                }
            },

            {
                StyleKeys.Hovered,
                new Style() {
                    new Color(0xa0, 0xa0, 0xa0, 0xff),
                    { StyleKeys.Width, 6f },
                    { StyleKeys.Radius, 3f },
                }
            },

            {
                StyleKeys.Pressed,
                new Style() {
                    new Color(0x90, 0x90, 0x90, 0xff),
                    { StyleKeys.Width, 6f },
                    { StyleKeys.Radius, 3f },
                }
            },
        };

        private BasicMesh Mesh;
        private Color PrevColor;
        private float PrevWidth;
        private float PrevRadius;
        private Point PrevWH;

        public bool? Enabled;
        protected bool IsNeeded;

        public readonly ScrollAxis Axis;

        protected Style.Entry StyleColor = new(new ColorFader());
        protected Style.Entry StyleWidth = new(new FloatFader());
        protected Style.Entry StyleWidthMax = new(6);
        protected Style.Entry StyleRadius = new(new FloatFader());

        protected override bool IsComposited => false;

        public ScrollHandle(ScrollAxis axis) {
            Cached = false;
            Interactive = InteractiveMode.Process;
            Mesh = new BasicMesh(UI.Game) {
                Texture = Assets.GradientQuadY
            };

            Axis = axis;
            switch (axis) {
                case ScrollAxis.X:
                    Layout.Add(LayoutPass.Pre, LayoutSubpass.AfterChildren, AxisX_LayoutReset);
                    Layout.Add(LayoutPass.Post, LayoutSubpass.AfterChildren, AxisX_LayoutNormal);
                    Events.Add<MouseEvent.Drag>(AxisX_OnDrag);
                    break;

                case ScrollAxis.Y:
                    Layout.Add(LayoutPass.Pre, LayoutSubpass.AfterChildren, AxisY_LayoutReset);
                    Layout.Add(LayoutPass.Post, LayoutSubpass.AfterChildren, AxisY_LayoutNormal);
                    Events.Add<MouseEvent.Drag>(AxisY_OnDrag);
                    break;

                default:
                    throw new ArgumentException($"Unknown scroll axis: {axis}");
            }
        }

        public override void Update(float dt) {
            bool enabled = Enabled ?? IsNeeded;

            Style.Apply(
                !enabled ? StyleKeys.Disabled :
                (Pressed || Dragged) ? StyleKeys.Pressed :
                Hovered ? StyleKeys.Hovered :
                StyleKeys.Normal
            );

            base.Update(dt);
        }

        public override void DrawContent() {
            if (!(Enabled ?? IsNeeded))
                return;

            Vector2 xy = ScreenXY;
            Point wh = WH;

            StyleColor.GetCurrent(out Color color);
            StyleWidth.GetCurrent(out float width);
            StyleRadius.GetCurrent(out float radius);

            if (PrevColor != color ||
                PrevWidth != width ||
                PrevRadius != radius ||
                PrevWH != wh) {
                PrevColor = color;
                PrevWidth = width;
                PrevRadius = radius;
                PrevWH = wh;

                MeshShapes<MiniVertex> shapes = Mesh.Shapes;
                shapes.Clear();

                if (color != default) {
                    shapes.Add(new MeshShapes.Rect() {
                        Color = color,
                        Size = new(wh.X, wh.Y),
                        Radius = radius,
                    });
                }

                // Fix UVs manually as we're using a gradient texture.
                for (int i = 0; i < shapes.VerticesMax; i++) {
                    ref MiniVertex vertex = ref shapes.Vertices[i];
                    vertex.UV = new(1f, 1f);
                }

                shapes.AutoApply();
            }

            UIDraw.Recorder.Add((Mesh, xy), static ((BasicMesh mesh, Vector2 xy) data) => {
                UI.SpriteBatch.End();
                data.mesh.Draw(UI.CreateTransform(data.xy));
                UI.SpriteBatch.BeginUI();
            });

            base.DrawContent();
        }

        // Mostly ported from the Lua counterpart as-is.

#region X axis scroll layout

        private void AxisX_LayoutReset(LayoutEvent e) {
            StyleWidthMax.GetReal(out int widthMax);
            XY = RealXY = default;
            WH = new(0, widthMax);
        }

        private void AxisX_LayoutNormal(LayoutEvent e) {
            ScrollBox box = Parent as ScrollBox ?? throw new Exception("Scroll handles belong into scroll boxes!");
            Element content = box.Content;

            StyleWidthMax.GetReal(out int widthMax);
            box.Style.GetReal(out int barPadding);

            int boxSize = box.WH.X;
            int contentSize = content.WH.X;
            if (contentSize == 0)
                contentSize = 1;
            int pos = (int) -content.XY.X;

            pos = boxSize * pos / contentSize;
            int size = boxSize * boxSize / contentSize;
            int tail = pos + size;

            if (pos < 1) {
                pos = 1;
            } else if (tail > boxSize - 1) {
                tail = boxSize - 1;
                if (pos > tail) {
                    pos = tail - 1;
                }
            }

            size = Math.Max(1, tail - pos - barPadding * 2);

            if (size + 1 + barPadding * 2 + box.Wiggle < contentSize) {
                IsNeeded = true;
                XY = RealXY = new(
                    pos + barPadding,
                    box.WH.Y - widthMax - 1 - barPadding
                );
                WH = new(
                    size,
                    widthMax
                );
            } else {
                IsNeeded = false;
                XY = RealXY = default;
                WH = default;
            }
        }

        private void AxisX_OnDrag(MouseEvent.Drag e) {
            ScrollBox box = Parent as ScrollBox ?? throw new Exception("Scroll handles belong into scroll boxes!");
            box.ForceScroll(new(e.DXY.X * box.Content.WH.X / box.WH.X, 0));
        }

#endregion

#region Y axis scroll layout

        private void AxisY_LayoutReset(LayoutEvent e) {
            StyleWidthMax.GetReal(out int widthMax);
            XY = RealXY = default;
            WH = new(widthMax, 0);
        }

        private void AxisY_LayoutNormal(LayoutEvent e) {
            ScrollBox box = Parent as ScrollBox ?? throw new Exception("Scroll handles belong into scroll boxes!");
            Element content = box.Content;

            StyleWidthMax.GetReal(out int widthMax);
            box.Style.GetReal(out int barPadding);

            int boxSize = box.WH.Y;
            int contentSize = content.WH.Y;
            if (contentSize == 0)
                contentSize = 1;
            int pos = (int) -content.XY.Y;

            pos = boxSize * pos / contentSize;
            int size = boxSize * boxSize / contentSize;
            int tail = pos + size;

            if (pos < 1) {
                pos = 1;
            } else if (tail > boxSize - 1) {
                tail = boxSize - 1;
                if (pos > tail) {
                    pos = tail - 1;
                }
            }

            size = Math.Max(1, tail - pos - barPadding * 2);

            if (size + 1 + barPadding * 2 + box.Wiggle < contentSize) {
                IsNeeded = true;
                XY = RealXY = new(
                    box.WH.X - widthMax - 1 - barPadding,
                    pos + barPadding
                );
                WH = new(
                    widthMax,
                    size
                );
            } else {
                IsNeeded = false;
                XY = RealXY = default;
                WH = default;
            }
        }

        private void AxisY_OnDrag(MouseEvent.Drag e) {
            ScrollBox box = Parent as ScrollBox ?? throw new Exception("Scroll handles belong into scroll boxes!");
            box.ForceScroll(new(0, e.DXY.Y * box.Content.WH.Y / box.WH.Y));
        }

        #endregion

        public new abstract partial class StyleKeys {

            public static readonly Style.Key Normal = new("Normal");
            public static readonly Style.Key Disabled = new("Disabled");
            public static readonly Style.Key Hovered = new("Hovered");
            public static readonly Style.Key Pressed = new("Pressed");
        }

    }
}
