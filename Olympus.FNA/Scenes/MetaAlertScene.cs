using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OlympUI;
using OlympUI.Events;
using OlympUI.MegaCanvas;
using Olympus.NativeImpls;
using System;

namespace Olympus {
    public partial class MetaAlertScene : Scene {

        public override Element Generate()
            => new AlertContainer() {
                ID = "MetaAlertScene",
                Layout = {
                    Layouts.Fill(1, 1, 0, 0),
                    Layouts.Grow(0, NativeImpl.Native.ClientSideDecoration < ClientSideDecorationMode.Title ? 0 : (-48 + 8)),
                    Layouts.Top(NativeImpl.Native.ClientSideDecoration < ClientSideDecorationMode.Title ? 0 : 48),
                },
                Style = {
                    { Group.StyleKeys.Padding, new Padding() {
                        L = 64,
                        T = 64,
                        R = 64,
                        B = NativeImpl.Native.ClientSideDecoration < ClientSideDecorationMode.Title ? 64 : (64 + 8)
                    } },
                    { Panel.StyleKeys.Radius, NativeImpl.Native.ClientSideDecoration < ClientSideDecorationMode.Title ? 0f : 8f },
                },
                Children = {
                    new Group() {
                        ID = "ContentContainer",
                        Cached = false,
                        Clip = false,
                        Interactive = InteractiveMode.Process,
                        Layout = {
                            Layouts.Fill(),
                        },
                        Children = {
                            Scener.AlertContainer,
                        }
                    },
                    new CloseButton("close") {
                        ID = "MetaAlertSceneClose",
                        Callback = _ => {
                            if (!(Scener.Alert?.Locked ?? false)) {
                                Scener.PopAlert();
                            }
                        },
                        Layout = {
                            Layouts.Top(-8),
                            Layouts.Right(8),
                        }
                    },
                }
            };

        public partial class AlertContainer : Panel, IMouseEventReceiver {

            public static readonly new Style DefaultStyle = new() {
                {
                    StyleKeys.Disabled,
                    new Style() {
                        { StyleKeys.Opacity, 0f },
                        { Panel.StyleKeys.Shadow, 0f },
                    }
                },

                {
                    StyleKeys.Enabled,
                    new Style() {
                        { StyleKeys.Opacity, 1f },
                        { Panel.StyleKeys.Shadow, 4f },
                    }
                },

                { Panel.StyleKeys.Background, new ColorFader(0x10, 0x10, 0x10, 0xd0) },
            };

            private BlurryGroup? MainBox;

            protected Style.Entry StyleOpacity = new(new FloatFader(0f));

            public AlertContainer() {
                Cached = true;
                Clip = true;
                Interactive = InteractiveMode.Process;
            }

            public override void Awake() {
                base.Awake();

                Scener.AlertContainer.Cached = false;
                Scener.AlertContainer.Clip = false;
            }

            public override void Update(float dt) {
                BlurryGroup mainBox = MainBox ??= UI.Root.FindChild<BlurryGroup>("MainBox");

                bool enabled = Scener.Alerts.Count != 0;

                Style.Apply(enabled ? StyleKeys.Enabled : StyleKeys.Disabled);
                mainBox.Style.Add(BlurryGroup.StyleKeys.Radius, enabled ? 32f : 0f);
                mainBox.Style.Add(BlurryGroup.StyleKeys.Strength, enabled ? 2f : 1f);
                mainBox.Style.Add(BlurryGroup.StyleKeys.Scale, enabled ? 6f : 2f);
                mainBox.Style.Add(BlurryGroup.StyleKeys.Noise, enabled ? 1f : 0f);

                InteractiveMode mode = enabled ? InteractiveMode.Process : InteractiveMode.Discard;
                if (Interactive != mode) {
                    Interactive = mode;
                    UI.Root.InvalidateCollect();
                }

                base.Update(dt);
            }

            protected override void PaintContent(bool paintToCache, bool paintToScreen, Padding padding) {
                if (StyleOpacity.GetCurrent<float>() < 0.004f) {
                    return;
                }

                base.PaintContent(paintToCache, paintToScreen, padding);
            }

            protected override void DrawCachedTexture(RenderTarget2DRegion rt, Vector2 xy, Padding padding, Point size) {
                UIDraw.AddDependency(rt);
                DrawModifiable(new UICmd.Sprite(
                    rt.RT,
                    rt.Region.WithSize(size),
                    (xy.ToPoint() - padding.LT).WithSize(size),
                    Color.White * StyleOpacity.GetCurrent<float>()
                ));
            }

            public void OnClick(MouseEvent.Click e) {
                foreach (Element child in Children)
                    if (child.Contains(e.XY))
                        return;

                if (!(Scener.Alert?.Locked ?? false)) {
                    Scener.PopAlert();
                }
            }

            public new abstract partial class StyleKeys {
                public static readonly Style.Key Enabled = new("Enabled");
                public static readonly Style.Key Disabled = new("Disabled");
            }

        }

        public partial class CloseButton : Button {

            public static readonly new Style DefaultStyle = new() {
                {
                    Button.StyleKeys.Normal,
                    new Style() {
                        { Panel.StyleKeys.Background, new Color(0x00, 0x00, 0x00, 0x50) },
                        { Panel.StyleKeys.Shadow, 0f },
                    }
                },

                {
                    Button.StyleKeys.Disabled,
                    new Style() {
                        { Panel.StyleKeys.Background, new Color(0x70, 0x70, 0x70, 0x70) },
                        { Panel.StyleKeys.Shadow, 0f },
                    }
                },

                {
                    Button.StyleKeys.Hovered,
                    new Style() {
                        { Panel.StyleKeys.Background, new Color(0x60, 0x60, 0x60, 0x70) },
                        { Panel.StyleKeys.Shadow, 0f },
                    }
                },

                {
                    Button.StyleKeys.Pressed,
                    new Style() {
                        { Panel.StyleKeys.Background, new Color(0x30, 0x30, 0x30, 0x70) },
                        { Panel.StyleKeys.Shadow, 0f },
                    }
                },

                { Panel.StyleKeys.Radius, 32f },
                { Panel.StyleKeys.Padding, 0 },
            };

            public Func<IReloadable<Texture2D, Texture2DMeta>> IconGen;
            private Icon Icon;

            public CloseButton(string icon)
                : this(OlympUI.Assets.GetTexture($"icons/{icon}")) {
            }

            public CloseButton(IReloadable<Texture2D, Texture2DMeta> icon)
                : this(() => icon) {
            }

            public CloseButton(Func<string> icon)
                : this(() => OlympUI.Assets.GetTexture($"icons/{icon()}")) {
            }

            public CloseButton(Func<IReloadable<Texture2D, Texture2DMeta>> iconGen)
                : base() {
                WH = new(48, 48);

                IconGen = iconGen;
                IReloadable<Texture2D, Texture2DMeta> icon = iconGen();

                Icon iconi = Icon = new(icon) {
                    ID = "icon",
                    Style = {
                        { ImageBase.StyleKeys.Color, Style.GetLink(Button.StyleKeys.Foreground) },
                    },
                    Layout = {
                        Layouts.Left(0.5f, -0.5f),
                        Layouts.Top(0.5f, -0.5f),
                    }
                };
                Texture2DMeta icont = icon.Meta;
                if (icont.Width > icont.Height) {
                    iconi.AutoW = 24;
                } else {
                    iconi.AutoH = 24;
                }
                Children.Add(iconi);
            }

            public override void Update(float dt) {
                IReloadable<Texture2D, Texture2DMeta> next = IconGen();
                if (next != Icon.Texture) {
                    Icon.Texture = next;
                    Icon.InvalidatePaint();
                }

                Visible = !(Scener.Alert?.Locked ?? false);

                base.Update(dt);
            }

        }

    }
}
