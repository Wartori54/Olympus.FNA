using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OlympUI;
using OlympUI.Animations;
using Olympus.NativeImpls;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Olympus; 

public record Notification {
    public required string Message;

    public TimeSpan Duration = TimeSpan.FromSeconds(3);
    private TimeSpan Elapsed = TimeSpan.Zero;
    
    public bool Finished = false;
    public float Progress => (float)(Elapsed / Duration);
    
    public void Update(float dt) {
        Elapsed += TimeSpan.FromSeconds(dt);
    }
}

public class MetaNotificationScene : Scene  {
    public static readonly TimeSpan NotificationFadeout = TimeSpan.FromSeconds(0.4f);

    private static MetaNotificationScene instance = null!; // Will be initialized at startup
    private static readonly List<Notification> notifications = new();
    
    public static void PushNotification(Notification notification) {
        notifications.Add(notification);
        instance.Refresh();
    }
    
    public MetaNotificationScene() {
        instance = this;
    }

    public override Element Generate()
        => new Group {
            Layout = {
                Layouts.Bottom(10),
                Layouts.Right(10),
                Layouts.Column(10),
            },
            Init = RegisterRefresh<Group>(el => {
                UI.Run(() => {
                    Console.WriteLine(el);
                    el.DisposeChildren();
                    foreach (var notification in notifications) {
                        el.Add(new NotificationPanel(notification) { W = 300, });
                    }
                });
                return Task.CompletedTask;
            }),
        };

    public override void Update(float dt) {
        foreach (var notification in notifications) {
            notification.Update(dt);
        }
        notifications.RemoveAll(notification => notification.Finished);

        base.Update(dt);
    }
    
    public class NotificationPanel : Panel {
        public new static readonly Style DefaultStyle = new() {
            { StyleKeys.Background, new ColorFader(AddColor(new Color(15, 15, 15, 255), NativeImpl.Native.Accent * 0.8f)) },
            { StyleKeys.Shadow, 5f },
        };
        
        private readonly Notification Notification;
        private bool startedFadeout = false;
        
        public NotificationPanel(Notification notification) {
            Clip = true;
            Notification = notification;
            
            var close = new CloseButton("close") {
                W = 24, H = 24,
                Callback = _ => {
                    if (!startedFadeout) StartFadeout();
                },
                Layout = {
                    Layouts.Fill(0.0f, 0.0f),
                    Layouts.Right(0),
                },
            };
            Children.Add(new Group {
                Layout = {
                    Layouts.Fill(1.0f, 0.0f, close.W),
                },
                Children = {
                    new Label(Notification.Message) { Wrap = true, }
                }
            });
            Children.Add(close);
            Children.Add(new NotificationProgress(Notification) {
                H = 5,
                Layout = {
                    Layouts.Fill(1.0f, 0.0f),
                    Layouts.Bottom(0),
                },
            });
        }

        public override void Update(float dt) {
            if (Notification.Progress > 1.0f && !startedFadeout) StartFadeout();
            base.Update(dt);
        }
        
        private void StartFadeout() {
            startedFadeout = true;
            Modifiers.Add(new FadeOutAnimation((float)NotificationFadeout.TotalSeconds));
            Modifiers.Add(new OffsetOutAnimation(-Vector2.UnitY * 20, (float)NotificationFadeout.TotalSeconds));
            // Remove ourselves once the animation is done
            Task.Run(async () => {
                await Task.Delay(NotificationFadeout);  
                RemoveSelf();
                Notification.Finished = true;
            });
        }

        private static Color AddColor(Color a, Color b) => new(a.R + b.R, a.G + b.G, a.B + b.B, a.A + b.A);
    }
    
    public class NotificationProgress : Element {
        protected override bool IsComposited => false;

        private readonly Notification Notification;
        private readonly BasicMesh Mesh;
        
        public NotificationProgress(Notification notification) {
            Notification = notification;
            Mesh = new BasicMesh(UI.Game) {
                Texture = OlympUI.Assets.White
            };
        }

        public override void Update(float dt) {
            InvalidatePaint();
            base.Update(dt);
        }

        public override void DrawContent() {
            MeshShapes<MiniVertex> shapes = Mesh.Shapes;
            shapes.Clear();
            shapes.Add(new MeshShapes.Rect() {
                Color = Color.White,
                XY1 = new Vector2(-Parent?.Padding.L ?? 0.0f, Parent?.Padding.B ?? 0.0f),
                Size = new Vector2((W + (Parent?.Padding.R ?? 0.0f) * 2.0f) * (1.0f - Notification.Progress), H),
            });
            shapes.AutoApply();

            UIDraw.Recorder.Add((Mesh, ScreenXY), static ((BasicMesh mesh, Vector2 xy) data) => {
                UI.SpriteBatch.End();

                Matrix transform = UI.CreateTransform(data.xy);
                data.mesh.Draw(transform);

                UI.SpriteBatch.BeginUI();
            });
            
            base.DrawContent();
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
            IconGen = iconGen;
            IReloadable<Texture2D, Texture2DMeta> icon = iconGen();

            Icon iconi = Icon = new(icon) {
                W = 16, H = 16,
                ID = "icon",
                Style = {
                    { ImageBase.StyleKeys.Color, Style.GetLink(Button.StyleKeys.Foreground) },
                },
                Layout = {
                    Layouts.Left(0.5f, -0.5f),
                    Layouts.Top(0.5f, -0.5f),
                }
            };
            // Texture2DMeta icont = icon.Meta;
            // if (icont.Width > icont.Height) {
            //     iconi.AutoW = 16;
            // } else {
            //     iconi.AutoH = 16;
            // }
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