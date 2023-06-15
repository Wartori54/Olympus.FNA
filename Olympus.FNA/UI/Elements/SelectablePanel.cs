using Microsoft.Xna.Framework;
using System;

namespace OlympUI {
    public partial class SelectablePanel : Panel {
        
        public static readonly new Style DefaultStyle = new() {
            {
                StyleKeys.Normal,
                new Style() {
                    { Panel.StyleKeys.Background, new Color(0x08, 0x08, 0x08, 0xd0) },
                    { Panel.StyleKeys.Border, new Color(0x08, 0x08, 0x08, 0xd0) },
                }
            }, 
            {
                StyleKeys.Hovered,
                new Style() {
                    { Panel.StyleKeys.Background,  new Color(0x18, 0x18, 0x18, 0xd0) },
                    { Panel.StyleKeys.Border, new Color(0x08, 0x08, 0x08, 0xd0) },
                }
            },
            {
                StyleKeys.Selected,
                new Style() {
                    { Panel.StyleKeys.Background, new Color(0x38, 0x38, 0x38, 0xd0) },
                    { Panel.StyleKeys.Border, new Color(0x68, 0x68, 0x68, 0xd0) },
                }
            },
        };
        
        protected new Style.Entry StyleBackground = new(new ColorFader(0xf8, 0xf8, 0xf8, 0xd0));
        protected new Style.Entry StyleBorder = new(new ColorFader(0x08, 0x08, 0x08, 0xd0));

        public bool Selected = false;

        private Func<MouseEvent.Click, bool> callback;

        public SelectablePanel() : this(click => true) {}

        public SelectablePanel(Func<MouseEvent.Click, bool> callback) {
            this.callback = callback;
        }

        private void OnClick(MouseEvent.Click e) {
            if (callback.Invoke(e))
                Selected = !Selected;
        }

        public override void Update(float dt) {
            Style.Apply(Selected ? StyleKeys.Selected :
                        Hovered ? StyleKeys.Hovered :
                        StyleKeys.Normal);

            base.Update(dt);
        }

        public new abstract partial class StyleKeys {

            public static readonly Style.Key Normal = new("Normal");
            public static readonly Style.Key Selected = new("Selected");
            public static readonly Style.Key Hovered = new("Hovered");
        }
    }
}