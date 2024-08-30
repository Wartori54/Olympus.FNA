using Microsoft.Xna.Framework;
using OlympUI;
using OlympUI.Animations;
using OlympUI.Modifiers;
using Olympus.NativeImpls;
using System;

namespace Olympus {
    public partial class TestScene : Scene {

        private static readonly Style.Key StyleOpacity = new("Opacity");

        private Label? BottomText;

        public override Element Generate()
            => new Group() {
                Layout = {
                    Layouts.Fill()
                },
                Children = {
                    new Icon(OlympUI.Assets.GetTexture("header_olympus")) {
                        Modifiers = {
                            new OpacityModifier(0.5f)
                        }
                    },
                    new Panel() {
                        X = 100,
                        Y = 100,
                        Children = {
                            new Label("a") {
                                Style = {
                                    OlympUI.Assets.FontMono
                                }
                            },
                            new Label("b") {
                                Style = {
                                    Color.Red
                                },
                                Layout = {
                                    e => {
                                        Element el = e.Element;
                                        Element prev = el.Siblings[-1];
                                        el.XY = new(prev.X + prev.W, prev.Y);
                                    }
                                }
                            }
                        }
                    },
                    new Panel() {
                        X = 300,
                        Y = 100,
                        MinWH = new Point(32, 32),
                        Style = {
                            { Panel.StyleKeys.Background, new Color(0xf0, 0xf0, 0xf0, 0xff) },
                            { Panel.StyleKeys.Shadow, 2f }
                        }
                    },
                    new Panel() {
                        X = 500,
                        Y = 100,
                        MinWH = new Point(32, 32),
                        Style = {
                            { Panel.StyleKeys.Background, Color.Black },
                            { Panel.StyleKeys.Shadow, 1f }
                        },
                        Events = {
                            (MouseEvent.Enter e) => e.Element.Style = new() {
                                { Panel.StyleKeys.Background, Color.White },
                                { Panel.StyleKeys.Shadow, 3f }
                            },
                            (MouseEvent.Leave e) => e.Element.Style = new() {
                                { Panel.StyleKeys.Background, Color.Black },
                                { Panel.StyleKeys.Shadow, 1f }
                            },
                        }
                    },
                    new Panel() {
                        X = 700,
                        Y = 100,
                        MinWH = new Point(32, 32),
                        Style = {
                            { Panel.StyleKeys.Background, Color.Black },
                            { Panel.StyleKeys.Shadow, 1f }
                        },
                        Events = {
                            (MouseEvent.Press e) => e.Element.Style = new() {
                                { Panel.StyleKeys.Background, Color.White },
                                { Panel.StyleKeys.Shadow, 3f }
                            },
                            (MouseEvent.Release e) => e.Element.Style = new() {
                                { Panel.StyleKeys.Background, Color.Black },
                                { Panel.StyleKeys.Shadow, 1f }
                            },
                            (MouseEvent.Click e) => e.Element.Style = new() {
                                { Panel.StyleKeys.Background, Color.Red },
                                { Panel.StyleKeys.Shadow, 5f }
                            },
                            (MouseEvent.Scroll e) => e.Element.Style = new() {
                                { Panel.StyleKeys.Background, e.ScrollDXY.Y <= 0 ? Color.Green : Color.Blue },
                                { Panel.StyleKeys.Shadow, 5f }
                            },
                        }
                    },
                    new Panel() {
                        Layout = {
                            Layouts.Left(0.5f, -0.5f),
                            Layouts.Top(0.5f, -0.5f),
                            Layouts.Fill(0.5f, 0.5f),
                        },
                        // Cached = false,
                        Children = {
                            new ScrollBox() {
                                Layout = {
                                    Layouts.Fill()
                                },
                                Content = new Panel() {
                                    H = 800,
                                    Layout = {
                                        Layouts.Fill(1, 0),
                                        Layouts.Column()
                                    },
                                    Children = {
                                        new Label("Test Label"),
                                        (BottomText = new Label("jfshdagfgahjgafasfhsjska") {
                                            Style = {
                                                { StyleOpacity, new FloatFader(0.5f) }
                                            },
                                            Modifiers = {
                                                new RandomLabelModifier(),
                                                new OpacityModifier(StyleOpacity)
                                            }
                                        }),
                                        new Button("Test Button")  {
                                            Layout = { Layouts.Fill(1, 0) },
                                            Data = { 0 },
                                            Callback = el => el.Text = $"Pressed: {++el.Data.Ref<int>()}"
                                        },
                                        new Button("Toggle Clip") {
                                            Layout = { Layouts.Fill(1, 0) },
                                            Callback = el => {
                                                Element container = el.GetParent<ScrollBox>().GetParent<Element>();
                                                container.Clip = !container.Clip;
                                                container.Cached = container.Clip;
                                            }
                                        },
                                        new Button("Disabled Button") {
                                            Layout = { Layouts.Fill(1, 0) },
                                            Enabled = false
                                        },
                                        new Button("Scrambled Button") {
                                            Layout = { Layouts.Fill(1, 0) },
                                            Init = el => el.GetChild<Label>().Modifiers.Add(new RandomLabelModifier()),
                                            Callback = _ => BottomText!.Style.Add(StyleOpacity, Random.Shared.NextSingle())
                                        },
                                        new Button("Fancy Button") {
                                            Layout = { Layouts.Fill(1, 0) },
                                            Callback = el => el.Modifiers.Add(new SpinAnimation())
                                        },
                                        new PathTest() {
                                            H = 200,
                                            Layout = { Layouts.Fill(1, 0) },
                                        },
                                        new SelectablePanel() {
                                            Children = {
                                            new Label("text"),
                                            }
                                        },
                                        new TestElement() {
                                        },
                                        new Panel() {
                                            Layout = {
                                                Layouts.Fill(1, 0),
                                                Layouts.Row(OrdererBehavior.None),
                                                Layouts.LayoutRespect(),
                                            },
                                            ID = "DEBUGPANEL1",
                                            Children = {
                                                new Group() {
                                                    Layout = {
                                                        Layouts.Fill(1, 0, LayoutConsts.Next),
                                                    },
                                                    Children = {
                                                        new Label("this is just text bla bla that will wrap kjasdfñlkjsadñflkjasdñflkjsadñflkjsadñflkjsadflñkjsadfñlkjdsafñlkjsadfñlkjajfaljfw") {
                                                            Wrap = true,
                                                        },
                                                    }
                                                },
                                                new Button("Do stuff (actually nothing)", b => {
                                                    b.Parent!.Parent!.Add(new Panel() {
                                                        Layout = {
                                                            Layouts.Column(),
                                                            Layouts.Fill(1, 0),
                                                        },
                                                        Children = {
                                                            new Group() {
                                                                ID = "InterestingGID",
                                                                Children = {
                                                                    new Label("this is text")
                                                                },
                                                            },
                                                            new Label("This is more text"),
                                                        }
                                                    });
                                                }) {
                                                    ID = "DEBUGBUTTON1"
                                                },
                                            }
                                        },
                                        new Group() {
                                            Layout = {
                                                Layouts.Fill(0.5f, 0, 0, 0),
                                                Layouts.Row(OrdererBehavior.None),
                                                // Layouts.LayoutRespect(),
                                            },
                                            Children = {
                                                new Group() {
                                                    ID = "DEBUGGROUP1",
                                                    Layout = {
                                                        Layouts.Fill(1, 1),
                                                        // Layouts.LayoutRespect(),
                                                    },
                                                    Children = {
                                                        new ModManagerScene.SectionButton(null, null, "ABC") {
                                                            ID = "DEBUGBUTTON2",
                                                            Layout = {
                                                                Layouts.Left(0.5f, -0.5f),
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    },
                                }
                            }
                        }
                    },
                    new Panel() {
                        Layout = {
                            Layouts.Row(8),
                            Layouts.Bottom(8),
                            Layouts.Right(8),
                        },
                        Children = {
                            new Spinner() {
                                Style = { () => NativeImpl.Native.Accent }
                            },
                            new Label("bottom text") {
                                Layout = { Layouts.Top(0.5f, -0.5f) },
                                Style = { () => NativeImpl.Native.Accent }
                            }
                        }
                    },
                    
                }
            };

        public partial class PathTest : Element {

            protected Style.Entry StyleColor = new(new ColorFader(() => NativeImpl.Native.Accent * 0.5f));

            private BasicMesh Mesh;
            private Color PrevColor;
            private Point PrevWH;

            protected override bool IsComposited => false;

            public PathTest() {
                Cached = false;
                Mesh = new BasicMesh(UI.Game) {
                    Texture = OlympUI.Assets.White
                };
            }

            public override void DrawContent() {
                Style.GetCurrent(out Color color);
                Point wh = WH;

                if (PrevColor != color ||
                    PrevWH != wh) {
                    MeshShapes<MiniVertex> shapes = Mesh.Shapes;
                    shapes.Clear();

                    if (color != default) {
                        shapes.Add(new MeshShapes<MiniVertex>.Poly() {
                            color,
                            25f,
                            new Vector2(W * 0f, H * 0f),
                            new Vector2(W * 1f, H * 0f),
                            new Vector2(W * 1f, H * 1f),
                            new Vector2(W * 0.45f, H * 0.75f),
                            new Vector2(W * 0.65f, H * 0.25f),
                            new Vector2(W * 0f, H * 0f),
                        });

                        shapes.Add(new MeshShapes.Rect() {
                            Color = color,
                            XY1 = new(10f, 30f),
                            Size = new(50f, 50f),
                            Radius = 50f,
                        });
                    }

                    shapes.AutoApply();
                }

                UIDraw.Recorder.Add((Mesh, ScreenXY), static ((BasicMesh mesh, Vector2 xy) data) => {
                    UI.SpriteBatch.End();

                    Matrix transform = UI.CreateTransform(data.xy);

                    data.mesh.WireFrame = false;
                    data.mesh.Draw(transform);

                    data.mesh.WireFrame = true;
                    data.mesh.Draw(transform);

                    UI.SpriteBatch.BeginUI();
                });

                base.DrawContent();

                PrevColor = color;
                PrevWH = wh;
            }

        }

    }

}
