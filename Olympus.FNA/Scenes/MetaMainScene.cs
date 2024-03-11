using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OlympUI;
using Olympus.NativeImpls;
using Olympus.Utils;
using SDL2;
using System;
using System.Collections.ObjectModel;

namespace Olympus {
    public partial class MetaMainScene : Scene {

        public const int SidebarButtonWidth = 72;

        public bool Real = true;

        private Action<Installation?> InstallUpdateProxy {
            get => installUpdateProxy ??= i => {};
            set {
                if (installUpdateProxy == null) { // not subscribed yet
                    Config.Instance.SubscribeInstallUpdateNotify(i => {
                        InstallUpdateProxy.Invoke(i);
                    }); 
                }
                installUpdateProxy = value;
            }
        }
        private static Action<Installation?>? installUpdateProxy;

        public override Element Generate()
            => new Group() {
                ID = "MetaMainScene",
                Layout = {
                    Layouts.Fill(),
                    Layouts.Column(false)
                },
                Children = {
                    NativeImpl.Native.ClientSideDecoration < ClientSideDecorationMode.Title ? Element.Null :
                    new Group() {
                        ID = "Title",
                        H = 48,
                        Layout = {
                            Layouts.Fill(1, 0),
                        },
                        Children = {
                            new Icon(OlympUI.Assets.GetTexture("header_olympus")) {
                                XY = new(8, 8),
                                AutoH = 32,
                            },
                            NativeImpl.Native.ClientSideDecoration < ClientSideDecorationMode.Full ? Element.Null :
                            new Group() {
                                ID = "WindowButtons",
                                Style = {
                                    { Group.StyleKeys.Spacing, 2 },
                                },
                                Layout = {
                                    Layouts.Top(),
                                    Layouts.Right(8),
                                    Layouts.Row(),
                                },
                                Children = {
                                    new WindowButton("minimize") {
                                        Callback = _ => {
                                            App.Instance.ManualUpdateSkip = true;
                                            SDL.SDL_MinimizeWindow(App.Instance.Window.Handle);
                                        },
                                    },
                                    new WindowButton(() => NativeImpl.Native.IsMaximized ? "fullscreen_exit" : "fullscreen_enter") {
                                        Callback = _ => {
                                            App.Instance.ManualUpdateSkip = true;
                                            if (NativeImpl.Native.IsMaximized)
                                                SDL.SDL_RestoreWindow(App.Instance.Window.Handle);
                                            else
                                                SDL.SDL_MaximizeWindow(App.Instance.Window.Handle);
                                        },
                                    },
                                    new WindowCloseButton("close") {
                                        Callback = _ => App.Instance.Exit(),
                                    },
                                }
                            }
                        }
                    },

                    new BlurryGroup() {
                        ID = "MainBox",
                        Style = {
                            { Group.StyleKeys.Spacing, 0 },
                        },
                        Layout = {
                            Layouts.Fill(1, 1, 0, LayoutConsts.Prev),
                            Layouts.Row(false),
                        },
                        Children = {
                            new Group() {
                                ID = "SidebarBox",
                                W = SidebarButtonWidth + 16,
                                Layout = {
                                    Layouts.Fill(0, 1),
                                    Layouts.Grow(0, NativeImpl.Native.ClientSideDecoration < ClientSideDecorationMode.Title ? -16 : -8),
                                    Layouts.Top(NativeImpl.Native.ClientSideDecoration < ClientSideDecorationMode.Title ? 8 : 0),
                                },
                                Children = {
                                    new Group() {
                                        ID = "SidebarTop",
                                        Style = {
                                            { Group.StyleKeys.Spacing, 8 },
                                        },
                                        Layout = {
                                            Layouts.Fill(1, 0),
                                            Layouts.Column(),
                                            Layouts.Top(),
                                        },
                                        Children = {
                                            
                                        }
                                    },
                                    new Group() {
                                        ID = "SidebarBottom",
                                        Style = {
                                            { Group.StyleKeys.Spacing, 8 },
                                        },
                                        Layout = {
                                            Layouts.Fill(1, 0),
                                            Layouts.Column(),
                                            Layouts.Bottom(),
                                        },
                                        Children = {
                                            new SidebarDownloadsButton(el => { }),
                                            new SidebarNavButton("cogwheel", "Settings", Scener.Get<ConfigurationScene>()),
                                        }
                                    },
                                }
                            },

                            new ContentContainer() {
                                ID = "ContentBox",
                                Style = {
                                    { Group.StyleKeys.Padding, 0 },
                                },
                                Layout = {
                                    Layouts.Fill(1, 1, LayoutConsts.Prev, 0),
                                    Layouts.Grow(8, 8),
                                    Layouts.Column(false),
                                },
                                Children = {
                                    new Group() {
                                        ID = "Path",
                                        H = 48,
                                        Layout = {
                                            Layouts.Fill(1, 0, 16, 0),
                                            Layouts.Grow(-8 - NativeImpl.Native.Padding.Right * 2, 0),
                                            Layouts.Left(8 + NativeImpl.Native.Padding.Right),
                                            Layouts.Top(8 + NativeImpl.Native.Padding.Right),
                                            Layouts.Row(false),
                                        },
                                        Init = Element.Cast((Group pathBar) => {
                                            Scener.SceneChanged += (prev, next) => {
                                                pathBar.Children.Clear();
                                                foreach (Scene scene in Scener.Scenes) {
                                                    pathBar.Children.Add(new HeaderBig(scene.Name));
                                                }
                                            };
                                        })
                                    },
                                    new Group() {
                                        ID = "ContentContainer",
                                        Cached = true,
                                        Clip = true,
                                        ClipExtend = new() {
                                            Bottom = NativeImpl.Native.Padding.Bottom,
                                        },
                                        Layout = {
                                            Layouts.Fill(1, 1, 0, LayoutConsts.Prev),
                                            Layouts.Grow(-8 - NativeImpl.Native.Padding.Right * 2, -8 - NativeImpl.Native.Padding.Bottom),
                                            Layouts.Left(NativeImpl.Native.Padding.Right),
                                        },
                                        Children = {
                                            Real ? Scener.SceneContainer : new Label("recursion"),
                                        }
                                    }
                                }
                            },
                        }
                    },
                }
            };

        public override Element PostGenerate(Element root) {
            Element sidebarBox = root.GetChild<Group>("MainBox")
                .GetChild<Group>("SidebarBox").GetChild<Group>("SidebarTop");
            
            InstallUpdateProxy = i => {
                UI.Run(() => {
                    sidebarBox.DisposeChildren();
                    sidebarBox.Children = GenerateSidebarTop(i);
                });
            };
            sidebarBox.DisposeChildren();
            sidebarBox.Children = GenerateSidebarTop(Config.Instance.Installation);
            
            return root;
        }

        private static ObservableCollection<Element> GenerateSidebarTop(Installation? install) { //TODO: make an actual icon for "pin"
            //TODO: Text doesnt fit
            SidebarPlayButton? everestPlay;
            SidebarPlayButton? vanillaPlay;
            if (install == null) { // Install not selected
                everestPlay = new SidebarPlayButton("pin", "Missing install!", _ => Scener.Push<InstallManagerScene>());
                vanillaPlay = new SidebarPlayButton("pin", "Missing install!", _ => Scener.Push<InstallManagerScene>());
            } else {
                (bool _, string _, Version? Version, string? _, string? _,
                        Version? ModVersion)
                    = install.ScanVersion(false);
                if (Version == null) { // Install is broken
                    everestPlay = new SidebarPlayButton("pin", "Broken", _ => Scener.Push<InstallManagerScene>());
                    vanillaPlay = new SidebarPlayButton("pin", "Broken", _ => Scener.Push<InstallManagerScene>());
                } else {
                    if (ModVersion == null) // Install unmodded
                        everestPlay = new SidebarPlayButton("pin", "Install Everest",
                            _ => Scener.Push<EverestSimpleInstallScene>());
                    else
                        everestPlay = new SidebarPlayButton("play_wheel", "Everest",
                            _ => GameLauncher.LaunchCurrent(false));
                    
                    // Allow always launching vanilla
                    vanillaPlay = new SidebarPlayButton("play", "Vanilla", _ => GameLauncher.LaunchCurrent(true));
                }
            }

            ObservableCollection<Element> children = new() {
                everestPlay,
                vanillaPlay,
                new SidebarNavButton("everest", "Home", Scener.Get<HomeScene>()),
                new SidebarNavButton("gamebanana", "Find Mods", Scener.Get<TestScene>()),
                new SidebarNavButton("loenn", "Lönn", Scener.Get<LoennScene>()),
                // new SidebarNavButton("ahorn", "Ahorn", new MetaMainScene() { Real = false }),
                new SidebarNavButton("wiki", "Wiki", Scener.Get<TestScene>()),
            };

            return children;
        }

        public partial class ContentContainer : Panel {

            public new static readonly Style DefaultStyle = new() {
                { StyleKeys.Background, new ColorFader(0x08, 0x08, 0x08, 0xB0) },
                { StyleKeys.Border, new ColorFader(0x08, 0x08, 0x08, 0x40) },
                { StyleKeys.BorderSize, 1f },
                { StyleKeys.Shadow, 0f },
            };

        }

        public partial class SidebarButton : Button {

            public static readonly new Style DefaultStyle = new() {
                {
                    StyleKeys.Normal,
                    new Style() {
                        { Panel.StyleKeys.Background, new Color(0x00, 0x00, 0x00, 0x00) },
                        { StyleKeys.Foreground, new Color(0xf0, 0x50, 0x50, 0x50) },
                        { Panel.StyleKeys.Shadow, 0f },
                    }
                },

                {
                    StyleKeys.Disabled,
                    new Style() {
                        { Panel.StyleKeys.Background, new Color(0x70, 0x70, 0x70, 0x70) },
                        { StyleKeys.Foreground, new Color(0x30, 0x30, 0x30, 0xff) },
                        { Panel.StyleKeys.Shadow, 0f },
                    }
                },

                {
                    StyleKeys.Hovered,
                    new Style() {
                        { Panel.StyleKeys.Background, new Color(0x00, 0x00, 0x00, 0x50) },
                        { StyleKeys.Foreground, new Color(0xff, 0xff, 0xff, 0xff) },
                        { Panel.StyleKeys.Shadow, 0f },
                    }
                },

                {
                    StyleKeys.Pressed,
                    new Style() {
                        { Panel.StyleKeys.Background, new Color(0x30, 0x30, 0x30, 0x70) },
                        { StyleKeys.Foreground, new Color(0xff, 0xff, 0xff, 0xff) },
                        { Panel.StyleKeys.Shadow, 0f },
                    }
                },

                { Group.StyleKeys.Padding, 0 },
            };

            public Icon Icon;
            public Label Label;

            public SidebarButton(string icon, string text)
                : this(OlympUI.Assets.GetTexture($"icons/{icon}"), text) {
            }

            public SidebarButton(IReloadable<Texture2D, Texture2DMeta> icon, string text)
                : base() {
                X = 8;
                WH = new(SidebarButtonWidth, 64);
                Cached = true;

                Icon iconi = new(icon) {
                    ID = "icon",
                    Style = {
                        { ImageBase.StyleKeys.Color, Style.GetLink(StyleKeys.Foreground) },
                    },
                    Layout = {
                        Layouts.Left(0.5f, -0.5f),
                        Layouts.Top(8),
                    }
                };
                Texture2DMeta icont = icon.Meta;
                if (icont.Width > icont.Height) {
                    iconi.AutoW = 32;
                } else {
                    iconi.AutoH = 32;
                }
                Icon = Add(iconi);

                Label = Add(new LabelSmall(text) {
                    ID = "label",
                    Style = {
                        { Label.StyleKeys.Color, Style.GetLink(StyleKeys.Foreground) },
                    },
                    Layout = {
                        Layouts.Left(0.5f, -0.5f),
                        Layouts.Bottom(4),
                    }
                });
            }

        }

        public partial class SidebarNavButton : SidebarButton {

            public static readonly new Style DefaultStyle = new() {
                {
                    StyleKeys.Current,
                    new Style() {
                        { Panel.StyleKeys.Background, () => NativeImpl.Native.Accent * 0.2f },
                        // { Button.StyleKeys.Foreground, new Color(0xff, 0xff, 0xff, 0xff) },
                        { Button.StyleKeys.Foreground, () => NativeImpl.Native.Accent },
                        { Panel.StyleKeys.Shadow, 0f },
                    }
                },
            };

            public readonly Scene Scene;

            public bool Current => Scener.Scenes.Contains(Scene);

            public override Style.Key StyleState =>
                Current ? StyleKeys.Current :
                base.StyleState;

            public SidebarNavButton(string icon, string text, Scene scene)
                : this(OlympUI.Assets.GetTexture($"icons/{icon}"), text, scene) {
            }

            public SidebarNavButton(IReloadable<Texture2D, Texture2DMeta> icon, string text, Scene scene)
                : base(icon, text) {
                Scene = scene;
                Add(new SidebarNavButtonIndicator(() => Current) {
                    X = 4,
                    W = 4,
                    Layout = {
                        Layouts.Fill(0, 1, 0, 24 * 2),
                        Layouts.Top(0.5f, -0.5f),
                    }
                });
                Callback += b => Scener.Set(((SidebarNavButton) b).Scene);
            }

            public new abstract partial class StyleKeys : SidebarButton.StyleKeys {
                protected StyleKeys(Secret secret) : base(secret) { }

                public static readonly Style.Key Current = new("Current");
            }

        }

        public partial class SidebarNavButtonIndicator : Element {

            public static readonly new Style DefaultStyle = new() {
                {
                    StyleKeys.Normal,
                    new Style() {
                        { new Color(0x00, 0x00, 0x00, 0x00) },
                        { StyleKeys.Scale, 0f },
                    }
                },

                {
                    StyleKeys.Active,
                    new Style() {
                        { () => NativeImpl.Native.Accent },
                        { StyleKeys.Scale, 1f },
                    }
                },
            };

            public readonly Func<bool> IsCurrent;
            public bool IsHorizontal = false;

            protected Style.Entry StyleColor = new(new ColorFader());
            protected Style.Entry StyleScale = new(new FloatFader());

            private readonly BasicMesh Mesh;
            private Color PrevColor;
            private float PrevScale;
            private Point PrevWH;

            protected override bool IsComposited => false;

            public SidebarNavButtonIndicator(Func<bool> isCurrent) {
                Cached = false;
                IsCurrent = isCurrent;
                Mesh = new BasicMesh(UI.Game) {
                    Texture = OlympUI.Assets.GradientQuadY
                };
            }

            public override void Update(float dt) {
                Style.Apply(IsCurrent() ? StyleKeys.Active : StyleKeys.Normal);

                base.Update(dt);
            }

            public override void DrawContent() {
                StyleColor.GetCurrent(out Color color);
                StyleScale.GetCurrent(out float scale);
                Point wh = WH;

                if (PrevColor != color ||
                    PrevScale != scale ||
                    PrevWH != wh) {
                    MeshShapes<MiniVertex> shapes = Mesh.Shapes;
                    shapes.Clear();

                    if (color != default) {
                        if (!IsHorizontal) {
                            shapes.Add(new MeshShapes.Rect() {
                                Color = color,
                                XY1 = new(0f, wh.Y * (0.5f - 0.5f * scale)),
                                Size = new(wh.X, wh.Y * scale),
                                Radius = Math.Min(wh.X, wh.Y * scale) * 0.5f,
                            });
                        } else {
                            shapes.Add(new MeshShapes.Rect() {
                                Color = color,
                                XY1 = new(wh.X * (0.5f - 0.5f * scale), 0f),
                                Size = new(wh.X * scale, wh.Y),
                                Radius = Math.Min(wh.X * scale, wh.Y) * 0.5f,
                            });
                        }
                    }

                    // Fix UVs manually as we're using a gradient texture.
                    for (int i = 0; i < shapes.VerticesMax; i++) {
                        ref MiniVertex vertex = ref shapes.Vertices[i];
                        vertex.UV = new(1f, 1f);
                    }

                    shapes.AutoApply();
                }

                UIDraw.Recorder.Add((Mesh, ScreenXY), static ((BasicMesh mesh, Vector2 xy) data) => {
                    UI.SpriteBatch.End();
                    data.mesh.Draw(UI.CreateTransform(data.xy));
                    UI.SpriteBatch.BeginUI();
                });

                base.DrawContent();

                PrevColor = color;
                PrevScale = scale;
                PrevWH = wh;
            }

            public new abstract partial class StyleKeys {
                public static readonly Style.Key Normal = new("Normal");
                public static readonly Style.Key Active = new("Active");
            }

        }

        public partial class SidebarPlayButton : SidebarButton {

            public static readonly new Style DefaultStyle = new() {
                {
                    StyleKeys.Normal,
                    new Style() {
                        { Panel.StyleKeys.Background, new Color(0x00, 0x30, 0x10, 0x50) },
                        { Panel.StyleKeys.Shadow, 0f },
                    }
                },

                {
                    StyleKeys.Disabled,
                    new Style() {
                        { Panel.StyleKeys.Background, new Color(0x70, 0x70, 0x70, 0x70) },
                        { Panel.StyleKeys.Shadow, 0f },
                    }
                },

                {
                    StyleKeys.Hovered,
                    new Style() {
                        { Panel.StyleKeys.Background, new Color(0x40, 0x70, 0x45, 0x70) },
                        { Panel.StyleKeys.Shadow, 0f },
                    }
                },

                {
                    StyleKeys.Pressed,
                    new Style() {
                        { Panel.StyleKeys.Background, new Color(0x20, 0x50, 0x25, 0x70) },
                        { Panel.StyleKeys.Shadow, 0f },
                    }
                },
            };

            public SidebarPlayButton(string icon, string text, Action<SidebarPlayButton> cb)
                : this(OlympUI.Assets.GetTexture($"icons/{icon}"), text, cb) {
            }

            public SidebarPlayButton(IReloadable<Texture2D, Texture2DMeta> icon, string text, Action<SidebarPlayButton> cb)
                : base(icon, text) {
                Callback += b => cb((SidebarPlayButton) b);
            }

        }

        public partial class SidebarDownloadsButton : SidebarButton {

            public SidebarDownloadsButton(Action<SidebarDownloadsButton> cb)
                : base(OlympUI.Assets.GetTexture("icons/download"), "") {
                Callback += b => cb((SidebarDownloadsButton) b);
                H = 20;

                Icon.Layout.Reset();
                Icon.AutoH = 16;
                Icon.XY = new(
                    W / 2 - Icon.W / 2,
                    2
                );

                Label.Layout.Reset();
                Label.Layout.Add(Layouts.Left(0.5f, -0.5f));
                Label.Layout.Add(Layouts.Top(2));
                Label.Layout.Add(Layouts.Move(Icon.W / 2, 0));
            }

        }

        public partial class WindowButton : Button {

            public static readonly new Style DefaultStyle = new() {
                {
                    StyleKeys.Normal,
                    new Style() {
                        { Panel.StyleKeys.Background, new Color(0x00, 0x00, 0x00, 0x50) },
                        { Panel.StyleKeys.Shadow, 0f },
                    }
                },

                {
                    StyleKeys.Disabled,
                    new Style() {
                        { Panel.StyleKeys.Background, new Color(0x70, 0x70, 0x70, 0x70) },
                        { Panel.StyleKeys.Shadow, 0f },
                    }
                },

                {
                    StyleKeys.Hovered,
                    new Style() {
                        { Panel.StyleKeys.Background, new Color(0x60, 0x60, 0x60, 0x70) },
                        { Panel.StyleKeys.Shadow, 0f },
                    }
                },

                {
                    StyleKeys.Pressed,
                    new Style() {
                        { Panel.StyleKeys.Background, new Color(0x30, 0x30, 0x30, 0x70) },
                        { Panel.StyleKeys.Shadow, 0f },
                    }
                },

                { Panel.StyleKeys.Radius, 0f },
                { Panel.StyleKeys.Padding, 0 },
            };

            public Func<IReloadable<Texture2D, Texture2DMeta>> IconGen;
            private readonly Icon Icon;

            public WindowButton(string icon)
                : this(OlympUI.Assets.GetTexture($"icons/{icon}")) {
            }

            public WindowButton(IReloadable<Texture2D, Texture2DMeta> icon)
                : this(() => icon) {
            }

            public WindowButton(Func<string> icon)
                : this(() => OlympUI.Assets.GetTexture($"icons/{icon()}")) {
            }

            public WindowButton(Func<IReloadable<Texture2D, Texture2DMeta>> iconGen)
                : base() {
                X = 8;
                WH = new(48, 32);

                IconGen = iconGen;
                IReloadable<Texture2D, Texture2DMeta> icon = iconGen();

                Icon iconi = Icon = new(icon) {
                    ID = "icon",
                    Style = {
                        { ImageBase.StyleKeys.Color, Style.GetLink(StyleKeys.Foreground) },
                    },
                    Layout = {
                        Layouts.Left(0.5f, -0.5f),
                        Layouts.Top(0.5f, -0.5f),
                    }
                };
                Texture2DMeta icont = icon.Meta;
                if (icont.Width > icont.Height) {
                    iconi.AutoW = 16;
                } else {
                    iconi.AutoH = 16;
                }
                Children.Add(iconi);
            }

            public override void Update(float dt) {
                IReloadable<Texture2D, Texture2DMeta> next = IconGen();
                if (next != Icon.Texture) {
                    Icon.Texture = next;
                    Icon.InvalidatePaint();
                }

                base.Update(dt);
            }

        }

        public partial class WindowCloseButton : WindowButton {

            public static readonly new Style DefaultStyle = new() {
                {
                    StyleKeys.Hovered,
                    new Style() {
                        { Panel.StyleKeys.Background, new Color(0x90, 0x20, 0x20, 0x90) },
                    }
                },

                {
                    StyleKeys.Pressed,
                    new Style() {
                        { Panel.StyleKeys.Background, new Color(0x70, 0x10, 0x10, 0x90) },
                    }
                },
            };

            public WindowCloseButton(string icon)
                : base(icon) {
            }

            public WindowCloseButton(IReloadable<Texture2D, Texture2DMeta> icon)
                : base(icon) {
            }

            public WindowCloseButton(Func<string> icon)
                : base(icon) {
            }

            public WindowCloseButton(Func<IReloadable<Texture2D, Texture2DMeta>> iconGen)
                : base(iconGen) {
            }

        }

    }
}
