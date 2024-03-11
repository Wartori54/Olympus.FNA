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
        protected Style.Entry StyleFontEffect = new(FontSystemEffect.None);
        protected Style.Entry StyleFontEffectAmount = new(0);

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
            UIDraw.Recorder.Add(new UICmd.Text(StyleFont.GetCurrent<DynamicSpriteFont>(), _TextDrawn, ScreenXY, 
                StyleColor.GetCurrent<Color>(), StyleFontEffect.GetCurrent<FontSystemEffect>(), 
                StyleFontEffectAmount.GetCurrent<int>()));
        }

        [LayoutPass(LayoutPass.Normal)]
        private void LayoutNormal(LayoutEvent e) {
            // FIXME: FontStashSharp can't even do basic font maximum size precomputations...

            DynamicSpriteFont font = StyleFont.GetCurrent<DynamicSpriteFont>();
            FontSystemEffect effect = StyleFontEffect.GetCurrent<FontSystemEffect>();
            int effectAmount = StyleFontEffectAmount.GetCurrent<int>();

            string text = _Text;
            _TextDrawn = text;
            Bounds bounds = GetTextBounds(text, font, effect, effectAmount);
            if (Wrap && Parent?.InnerWH.X is { } max && bounds.X2 >= max) {
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
                        bounds = font.TextBounds(line, Vector2.Zero, Vector2.One, 0F, 0F, effect, effectAmount);
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
                    bounds = font.TextBounds(line, Vector2.Zero, Vector2.One, 0F, 0F, effect, effectAmount);
                    if (bounds.X2 >= max) {
                        full[iSplit] = '\n';
                        line.Clear().Append(part);
                    }
                }
                _TextDrawn = text = full.ToString();
                bounds = GetTextBounds(text, font, effect, effectAmount);
            }

            WH = new((int) MathF.Round(bounds.X2), (int) MathF.Round(bounds.Y2));

            DynamicData fontExtra = new(font);
            if (!fontExtra.TryGet("MaxHeight", out int? maxHeight)) {
                bounds = GetTextBounds(font: font, effectAmount: effectAmount, effect: effect);
                maxHeight = (int) MathF.Round(bounds.Y2);
                fontExtra.Set("MaxHeight", maxHeight);
            }

            WH.Y = Math.Max(WH.Y, maxHeight ?? 0);
        }

        public Bounds GetTextBounds(string text = "The quick brown fox jumps over the lazy dog.", 
            DynamicSpriteFont? font = null, FontSystemEffect? effect = null, int? effectAmount = null) {
            font ??= StyleFont.GetCurrent<DynamicSpriteFont>();
            effect ??= StyleFontEffect.GetCurrent<FontSystemEffect>();
            effectAmount ??= StyleFontEffectAmount.GetCurrent<int>();
            return font.TextBounds(text, Vector2.Zero, Vector2.One, 0F, 0F,
                effect.Value, effectAmount.Value);
        }

    }
}
