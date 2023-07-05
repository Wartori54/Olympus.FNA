using FontStashSharp;
using Microsoft.Xna.Framework;
using MonoMod.Utils;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace OlympUI {
    public partial class Label : Element {

        protected Style.Entry StyleColor = new(new ColorFader(0xe8, 0xe8, 0xe8, 0xff));
        protected Style.Entry StyleFont = new(Assets.Font);

        private string _Text;
        private string _TextDrawn = "";
        public string Text {
            get => _Text;
            [MemberNotNull(nameof(_Text))]
            set {
                if (value is null)
                    value = "";
                if (_Text == value)
                    return;
                _Text = value;
                InvalidateFull();
                LayoutNormal(LayoutEvent.Instance); // Recalculate size early
            }
        }

        private bool _Wrap;
        public bool Wrap {
            get => _Wrap;
            set {
                if (_Wrap == value)
                    return;
                _Wrap = value;
                InvalidateFull();
            }
        }

        protected override bool IsComposited => false;

        public Label(string text) {
            Text = text;
        }

        public override void DrawContent() {
            UIDraw.Recorder.Add(new UICmd.Text(StyleFont.GetCurrent<DynamicSpriteFont>(), _TextDrawn, ScreenXY, StyleColor.GetCurrent<Color>()));
        }

        private void LayoutNormal(LayoutEvent e) {
            // FIXME: FontStashSharp can't even do basic font maximum size precomputations...

            DynamicSpriteFont font = StyleFont.GetCurrent<DynamicSpriteFont>();

            Bounds bounds = new();
            string text = _Text;
            _TextDrawn = text;
            font.TextBounds(text, new(0f, 0f), ref bounds, new(1f, 1f));
            if (Wrap && Parent?.InnerWH.X is int max && bounds.X2 >= max) {
                StringBuilder full = new((int) (text.Length * 1.2f));
                StringBuilder line = new(text.Length);
                ReadOnlySpan<char> part;
                // First part shouldn't be shoved onto a new line.
                int iPrev = -1;
                int iSplit;
                int i = text.IndexOf(' ', 0);
                if (i != -1) {
                    part = text.AsSpan(iPrev + 1, i - (iPrev + 1));
                    full.Append(part);
                    line.Append(part);
                    iPrev = i;
                    while ((i = text.IndexOf(' ', i + 1)) != -1) {
                        part = text.AsSpan(iPrev + 1, i - (iPrev + 1));
                        iSplit = full.Length;
                        full.Append(' ').Append(part);
                        line.Append(' ').Append(part);
                        font.TextBounds(line, new(0f, 0f), ref bounds, new(1f, 1f));
                        if (bounds.X2 >= max) {
                            full[iSplit] = '\n';
                            line.Clear().Append(part);
                        }
                        iPrev = i;
                    }
                    // Last part. While I could squeeze it into the main loop, eh.
                    part = text.AsSpan(iPrev + 1);
                    iSplit = full.Length;
                    full.Append(' ').Append(part);
                    line.Append(' ').Append(part);
                    font.TextBounds(line, new(0f, 0f), ref bounds, new(1f, 1f));
                    if (bounds.X2 >= max) {
                        full[iSplit] = '\n';
                        line.Clear().Append(part);
                    }
                }
                _TextDrawn = text = full.ToString();
                font.TextBounds(text, new(0f, 0f), ref bounds, new(1f, 1f));
            }

            WH = new((int) MathF.Round(bounds.X2), (int) MathF.Round(bounds.Y2));

            DynamicData fontExtra = new(font);
            if (!fontExtra.TryGet("MaxHeight", out int? maxHeight)) {
                font.TextBounds("The quick brown fox jumps over the lazy dog.", new(0f, 0f), ref bounds, new(1f, 1f));
                maxHeight = (int) MathF.Round(bounds.Y2);
                fontExtra.Set("MaxHeight", maxHeight);
            }

            WH.Y = Math.Max(WH.Y, maxHeight ?? 0);
        }

        public Bounds GetTextBounds(string text = "The quick brown fox jumps over the lazy dog.") {
            Bounds bounds = new();
            DynamicSpriteFont font = StyleFont.GetCurrent<DynamicSpriteFont>();
            font.TextBounds(text, new(0f, 0f), ref bounds, new(1f, 1f));
            return bounds;
        }

    }
}
