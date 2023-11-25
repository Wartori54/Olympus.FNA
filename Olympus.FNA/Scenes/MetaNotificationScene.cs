using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OlympUI;
using OlympUI.Animations;
using System;
using System.Threading.Tasks;

namespace Olympus {

public record Notification {
    public enum SeverityLevel {
        Information, Warning, Error
    }
    
    public required string Message;
    public TimeSpan Duration = TimeSpan.FromSeconds(5);
    public SeverityLevel Level = SeverityLevel.Information;

    private TimeSpan Elapsed = TimeSpan.Zero;

    public float Progress => (float)(Elapsed / Duration);
    
    public void Update(float dt) {
        Elapsed += TimeSpan.FromSeconds(dt);
    }
}

public partial class MetaNotificationScene : Scene  {
    public static readonly TimeSpan NotificationFadeout = TimeSpan.FromSeconds(0.4f);
    private const int Spacing = 10;

    public static void PushNotification(Notification notification) {
        UI.Run(() => {
            UI.Root.GetChild(nameof(MetaNotificationScene))?.Children.Insert(0, new NotificationPanel(notification) {
                W = 300,
            });
        });
    }

    public override Element Generate()
        => new Group {
            ID = nameof(MetaNotificationScene),
            Layout = {
                Layouts.Bottom(10),
                Layouts.Right(10),
                Layouts.Column(Spacing),
            },
        };
    
    private static readonly Color WarningColor = new(0xE8, 0x9A, 0x14, 0xFF);
    private static readonly Color ErrorColor = new(0x6F, 0x10, 0x10, 0xFF);
    private static readonly Color ErrorColorHighlight = new(0xBF, 0x00, 0x00, 0xFF);
    
    public class NotificationPanel : Panel {
        public enum Lifecycle {
            Show, FadeOut, Remove,
        }
        
        private const int Shadow = 5;

        public new static readonly Style DefaultStyle = new() {
            {
                StyleKeys.Information,
                new Style { }
            },
            {
                StyleKeys.Warning,
                new Style {
                    { Panel.StyleKeys.Border, WarningColor },
                    { Panel.StyleKeys.BorderSize, -2f }
                }
            },
            {
                StyleKeys.Error,
                new Style {
                    { Panel.StyleKeys.Background, ErrorColor },                    
                }
            },
            
            { Panel.StyleKeys.Background, new Color(0x1F, 0x1F, 0x1F, 0xFF) },
            { Panel.StyleKeys.Shadow, Shadow },
        };
        
        private readonly Notification Notification;
        private Lifecycle Status = Lifecycle.Show;
        
        public NotificationPanel(Notification notification) {
            Clip = true;
            ClipExtend = Shadow * 8;
            Notification = notification;
                
            var close = new NotificationCloseButton("close") {
                W = 24, H = 24,
                Callback = _ => {
                    if (Status == Lifecycle.Show) StartFadeout();
                },
                Layout = {
                    Layouts.Fill(0.0f, 0.0f),
                    Layouts.Right(0),
                },
            };
            Children.Add(new Group {
                Layout = {
                    Layouts.Fill(1.0f, 0.0f, LayoutConsts.Next),
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

        public override void Awake() {
            Modifiers.Add(new FadeInAnimation());
            Modifiers.Add(new OffsetInAnimation(Vector2.UnitX * W));
            base.Awake();
        }

        public override void Update(float dt) {
            Notification.Update(dt);
            
            Style.Apply(Notification.Level switch {
                Notification.SeverityLevel.Information => StyleKeys.Information,
                Notification.SeverityLevel.Warning => StyleKeys.Warning,
                Notification.SeverityLevel.Error => StyleKeys.Error,
                _ => throw new ArgumentOutOfRangeException()
            });
            InvalidatePaint();
            
            if (Status == Lifecycle.Remove)
                UI.Run(() => {
                    // Remove the move down animations again
                    if (Parent == null) return;
                    int idx = Parent.Children.IndexOf(this);
                    for (int i = idx - 1; i >= 0; i--) {
                        var element = Parent.Children[i];
                        int modIdx = -1;
                        for (int j = 0; j < element.Modifiers.Count; j++) {
                            if (element.Modifiers[j] is OffsetOutAnimation offset && Math.Abs(offset.Value - 1.0f) < 0.01f) {
                                modIdx = j;
                                break;
                            }
                        }
                        if (modIdx == -1) continue;
                        element.Modifiers.RemoveAt(modIdx);
                    }
                    RemoveSelf();
                });
            if (Notification.Progress >= 1.0f && Status == Lifecycle.Show) 
                StartFadeout();

            base.Update(dt);
        }
        
        private void StartFadeout() {
            Status = Lifecycle.FadeOut;
            Modifiers.Add(new FadeOutAnimation((float)NotificationFadeout.TotalSeconds));
            Modifiers.Add(new OffsetOutAnimation(Vector2.UnitX * W, (float)NotificationFadeout.TotalSeconds));
            // Move above notifications down
            if (Parent == null) return;
            int idx = Parent.Children.IndexOf(this);
            for (int i = idx - 1; i >= 0; i--) {
                var element = Parent.Children[i]; 
                element.Modifiers.Add(new OffsetOutAnimation(Vector2.UnitY * (H + Spacing), (float)NotificationFadeout.TotalSeconds));
            }
            // Remove ourselves once the animation is done
            Task.Run(async () => {
                await Task.Delay(NotificationFadeout);  
                Status = Lifecycle.Remove;
            });
        }

        public new abstract class StyleKeys {
            public static readonly Style.Key Information = new("Information");
            public static readonly Style.Key Warning = new("Warning");
            public static readonly Style.Key Error = new("Error");
        }
    }
    
    public partial class NotificationProgress : Element {
        public new static readonly Style DefaultStyle = new() {
            {
                StyleKeys.Information,
                new Style { }
            },
            {
                StyleKeys.Warning,
                new Style {
                    { StyleKeys.Color, WarningColor }
                }
            },
            {
                StyleKeys.Error,
                new Style {
                    { StyleKeys.Color, ErrorColorHighlight }
                }
            },
            
            { StyleKeys.Color, new Color(0x7F, 0x7F, 0x7F, 0xE0) }
        };

        protected Style.Entry StyleColor = new(new ColorFader());

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
            Style.Apply(Notification.Level switch {
                Notification.SeverityLevel.Information => StyleKeys.Information,
                Notification.SeverityLevel.Warning => StyleKeys.Warning,
                Notification.SeverityLevel.Error => StyleKeys.Error,
                _ => throw new ArgumentOutOfRangeException()
            });

            InvalidatePaint();
            base.Update(dt);
        }

        public override void DrawContent() {
            StyleColor.GetCurrent(out Color color);
            MeshShapes<MiniVertex> shapes = Mesh.Shapes;
            shapes.Clear();
            shapes.Add(new MeshShapes.Rect {
                Color = color,
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
        
        public new abstract partial class StyleKeys {
            public static readonly Style.Key Information = new("Information");
            public static readonly Style.Key Warning = new("Warning");
            public static readonly Style.Key Error = new("Error");
        }
    }
    
    public class NotificationCloseButton : Button {
        public new static readonly Style DefaultStyle = new() {
            {
                StyleKeys.Normal,
                new Style {
                    { Panel.StyleKeys.Background, Color.Transparent },
                    { Panel.StyleKeys.Shadow, 0f },
                }
            },
            {
                StyleKeys.Hovered,
                new Style {
                    { Panel.StyleKeys.Background, new Color(0x60, 0x60, 0x60, 0x70) },
                    { Panel.StyleKeys.Shadow, 0f },
                }
            },
            {
                StyleKeys.Pressed,
                new Style {
                    { Panel.StyleKeys.Background, new Color(0x30, 0x30, 0x30, 0x70) },
                    { Panel.StyleKeys.Shadow, 0f },
                }
            },

            { Panel.StyleKeys.BorderSize, 0f },
            { Panel.StyleKeys.Border, Color.Transparent },
            { Panel.StyleKeys.Padding, 0 },
        };

        public Func<IReloadable<Texture2D, Texture2DMeta>> IconGen;
        private Icon Icon;

        public NotificationCloseButton(string icon)
            : this(OlympUI.Assets.GetTexture($"icons/{icon}")) {
        }

        public NotificationCloseButton(IReloadable<Texture2D, Texture2DMeta> icon)
            : this(() => icon) {
        }

        public NotificationCloseButton(Func<string> icon)
            : this(() => OlympUI.Assets.GetTexture($"icons/{icon()}")) {
        }

        public NotificationCloseButton(Func<IReloadable<Texture2D, Texture2DMeta>> iconGen) {
            IconGen = iconGen;
            IReloadable<Texture2D, Texture2DMeta> icon = iconGen();

            Icon iconi = Icon = new(icon) {
                W = 16, H = 16,
                ID = "icon",
                Style = {
                    { ImageBase.StyleKeys.Color, Style.GetLink(StyleKeys.Foreground) },
                },
                Layout = {
                    Layouts.Left(0.5f, -0.5f),
                    Layouts.Top(0.5f, -0.5f),
                }
            };
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