using Microsoft.Xna.Framework;
using OlympUI.Events;
using System;

namespace OlympUI {
    public partial class Button : Panel, IMouseEventReceiver {

        public static readonly new Style DefaultStyle = new() {
            {
                StyleKeys.Normal,
                new Style() {
                    { Panel.StyleKeys.Background, new Color(0x30, 0x30, 0x30, 0xff) },
                    { StyleKeys.Foreground, new Color(0xe8, 0xe8, 0xe8, 0xff) },
                    { Panel.StyleKeys.Border, new Color(0x38, 0x38, 0x38, 0x80) },
                    { Panel.StyleKeys.Shadow, 0.5f },
                }
            },

            {
                StyleKeys.Disabled,
                new Style() {
                    { Panel.StyleKeys.Background, new Color(0x70, 0x70, 0x70, 0xff) },
                    { StyleKeys.Foreground, new Color(0x30, 0x30, 0x30, 0xff) },
                    { Panel.StyleKeys.Border, new Color(0x28, 0x28, 0x28, 0x70) },
                    { Panel.StyleKeys.Shadow, 0.2f },
                }
            },

            {
                StyleKeys.Hovered,
                new Style() {
                    { Panel.StyleKeys.Background, new Color(0x50, 0x50, 0x50, 0xff) },
                    { StyleKeys.Foreground, new Color(0xff, 0xff, 0xff, 0xff) },
                    { Panel.StyleKeys.Border, new Color(0x68, 0x68, 0x68, 0x90) },
                    { Panel.StyleKeys.Shadow, 0.8f },
                }
            },

            {
                StyleKeys.Pressed,
                new Style() {
                    { Panel.StyleKeys.Background, new Color(0x38, 0x38, 0x38, 0xff) },
                    { StyleKeys.Foreground, new Color(0xff, 0xff, 0xff, 0xff) },
                    { Panel.StyleKeys.Border, new Color(0x18, 0x18, 0x18, 0x90) },
                    { Panel.StyleKeys.Shadow, 0.35f },
                }
            },

            { Panel.StyleKeys.BorderSize, 1f },
            { Panel.StyleKeys.Radius, 4f },
        };

        public bool Enabled = true;
        public Action<Button>? Callback;

        protected Style.Entry StyleForeground = new(new ColorFader());

        public override CursorStyle CursorStyle => Enabled ? CursorStyle = CursorStyle.Pointer : CursorStyle.Normal;

        public string Text {
            get => GetChild<Label>()?.Text ?? "";
            set => _ = GetChild<Label>() is Label label ? label.Text = value : null;
        }

        public virtual Style.Key StyleState =>
            !Enabled ? StyleKeys.Disabled :
            Pressed ? StyleKeys.Pressed :
            Hovered ? StyleKeys.Hovered :
            StyleKeys.Normal;

        public Button() {
            Interactive = InteractiveMode.Process;
        }

        public Button(string text)
            : this() {
            Layout.Add(Layouts.Row(OrdererBehavior.None));

            Children.Add(new Label(text) {
                ID = "label",
                Style = {
                    { Label.StyleKeys.Color, Style.GetLink(StyleKeys.Foreground) }
                }
            });
        }

        public Button(Action<Button> cb)
            : this() {
            Callback = cb;
        }

        public Button(string text, Action<Button> cb)
            : this(text) {
            Callback = cb;
        }

        public override void Update(float dt) {
            Style.Apply(StyleState);

            base.Update(dt);
        }

        public void OnClick(MouseEvent.Click e) {
            if (Enabled)
                Callback?.Invoke(this);
        }

        public new abstract partial class StyleKeys {

            public static readonly Style.Key Normal = new("Normal");
            public static readonly Style.Key Disabled = new("Disabled");
            public static readonly Style.Key Hovered = new("Hovered");
            public static readonly Style.Key Pressed = new("Pressed");
        }
    }
}
