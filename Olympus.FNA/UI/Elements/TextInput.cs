using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using SDL2;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OlympUI; 

public partial class TextInput : Panel {
    public record struct SelectionArea() {
        public static readonly SelectionArea Inactive = new();
        
        public int Start = -1;
        public int End = -1;
        
        public int Min => Start < End ? Start : End;
        public int Max => Start < End ? End : Start;
        public int Length => Max - Min;

        public SelectTarget SelectionTarget;

        public bool Active => Start >= 0 && End >= 0 && Start != End;

        public enum SelectTarget {
            Char,
            Word,
            Line,
            
            Highest = Line + 1, // Describes the total entries
        }
    }
    
    public new static readonly Style DefaultStyle = new() {
        {
            StyleKeys.Normal,
            new Style {
                { Panel.StyleKeys.Background, new Color(0x30, 0x30, 0x30, 0xff) },
                { Panel.StyleKeys.Shadow, 1f },
                { StyleKeys.Foreground, new Color(0xe8, 0xe8, 0xe8, 0xff) },
                { StyleKeys.Placeholder, new Color(0x88, 0x88, 0x88, 0xff) },
                { StyleKeys.Cursor, new Color(0xe8, 0xe8, 0xe8, 0xff) },
                { StyleKeys.Selection, new Color(0x00, 0x33, 0x55, 0x7f) },
            }
        },
        {
            StyleKeys.Hovered,
            new Style {
                { Panel.StyleKeys.Background, new Color(0x20, 0x20, 0x20, 0xff) },
                { Panel.StyleKeys.Shadow, 2f },
                { StyleKeys.Foreground, new Color(0xe8, 0xe8, 0xe8, 0xff) },
                { StyleKeys.Placeholder, new Color(0x88, 0x88, 0x88, 0xff) },
                { StyleKeys.Cursor, new Color(0xe8, 0xe8, 0xe8, 0xff) },
                { StyleKeys.Selection, new Color(0x00, 0x33, 0x55, 0x7f) },
            }
        },
        {
            StyleKeys.Focused,
            new Style {
                { Panel.StyleKeys.Background, new Color(0x10, 0x10, 0x10, 0xff) },
                { Panel.StyleKeys.Shadow, 3f },
                { StyleKeys.Foreground, new Color(0xe8, 0xe8, 0xe8, 0xff) },
                { StyleKeys.Placeholder, new Color(0x88, 0x88, 0x88, 0xff) },
                { StyleKeys.Cursor, new Color(0xe8, 0xe8, 0xe8, 0xff) },
                { StyleKeys.Selection, new Color(0x00, 0x33, 0x55, 0x7f) },
            }
        },
        {
            StyleKeys.Disabled,
            new Style {
                { Panel.StyleKeys.Background, new Color(0x08, 0x08, 0x08, 0xd0) },
                { Panel.StyleKeys.Shadow, 3f },
                { StyleKeys.Foreground, new Color(0x98, 0x98, 0x98, 0xff) },
                { StyleKeys.Placeholder, new Color(0x88, 0x88, 0x88, 0xff) },
                { StyleKeys.Cursor, new Color(0xe8, 0xe8, 0xe8, 0xff) },
                { StyleKeys.Selection, new Color(0x00, 0x33, 0x55, 0x7f) },
            }
        },

        // { StyleKeys.Foreground, new Color(0xe8, 0xe8, 0xe8, 0xff) },
        // { StyleKeys.Placeholder, new Color(0x88, 0x88, 0x88, 0xff) },
        // { StyleKeys.Cursor, new Color(0xe8, 0xe8, 0xe8, 0xff) },
        // { StyleKeys.Selection, new Color(0x00, 0x33, 0x55, 0x7f) },
    };

    protected Style.Entry StyleForeground = new(new ColorFader());
    protected Style.Entry StylePlaceholder = new(new ColorFader());
    protected Style.Entry StyleSelection = new(new ColorFader());
    protected Style.Entry StyleCursor = new(new ColorFader());
    
    public int Cursor = 0;
    public SelectionArea Selection = new() { Start = -1 };

    public int MaxLength = -1;
    
    public string Text {
        get => TextLabel.Text;
        set { 
            TextLabel.Text = MaxLength > 0 ? value.Substring(0, Math.Min(value.Length, MaxLength)) : value;
            TextCallback?.Invoke(this);
        }
    }
    public string Placeholder {
        get => PlaceholderLabel.Text;
        set => PlaceholderLabel.Text = value;
    }

    private bool _enabled = true;
    public bool Enabled {
        get => _enabled;
        set {
            // Unfocus ourselves when disabled
            if (value == false && Focused) {
                OnUnfocus(new FocusEvent.Unfocus());
                UI.Focusing = null;
            }
            _enabled = value;
            InvalidatePaint();
        }
    }

    private readonly Dictionary<char, Rectangle> charSizes = new();

    public Action<TextInput>? ClickCallback;
    public Action<TextInput>? TextCallback;
    
    private readonly Label TextLabel;
    private readonly Label PlaceholderLabel;
    private readonly BasicMesh CursorMesh;

    private static readonly TimeSpan CursorBlinkSpeed = TimeSpan.FromSeconds(0.5f);
    private TimeSpan CursorBlinkTimer = TimeSpan.Zero;
    private bool CursorBlinkState = true;
    
    private int PrevCursor = -1;
    private SelectionArea PrevSelection = new() { Start = -1 };
    private Color PrevCursorColor = Color.Transparent;
    private Color PrevSelectionColor = Color.Transparent;
    private bool PrevCursorBlinkState = false;
    
    private Style.Key StyleState =>
        !Enabled ? StyleKeys.Disabled :
        Focused ? StyleKeys.Focused :
        Hovered ? StyleKeys.Hovered :
        StyleKeys.Normal;
    
    public TextInput(Label text, Label placeholderLabel) {
        CursorMesh = new BasicMesh(UI.Game) {
            Texture = Assets.White
        };
        
        Style.Apply(StyleState);
        
        //FIXME: Using the Style.GetLink(StyleKey) just doesnt work?
        text.Style.Add(Label.StyleKeys.Color, () => Style.GetCurrent<Color>(StyleKeys.Foreground));
        TextLabel = text;
        Children.Add(TextLabel);

        //FIXME: Using the Style.GetLink(StyleKey) just doesnt work?
        placeholderLabel.Style.Add(Label.StyleKeys.Color, () => Style.GetCurrent<Color>(StyleKeys.Placeholder));
        PlaceholderLabel = placeholderLabel;
        Children.Add(PlaceholderLabel);
        
        Cursor = Text.Length;
    }

    private enum Action {
        MoveLeft, MoveRight, Edit
    }
    private bool BeforeCursorMove(Action action) {
        if ((UIInput.Down(Keys.LeftShift) || UIInput.Down(Keys.RightShift)) && action != Action.Edit) {
            if (!Selection.Active) {
                Selection = new SelectionArea { Start = Cursor };        
            }
            return true;
        }
        if (Selection.Active) {
            switch (action) {
                case Action.MoveLeft:
                    Cursor = Selection.Min;
                    break;
                case Action.MoveRight:
                    Cursor = Selection.Max;
                    break;
                case Action.Edit:
                    Cursor = Selection.Min;
                    Text = Text.Remove(Selection.Min, Selection.Length);
                    break;
            }
            Selection = SelectionArea.Inactive;
            return false;
        }
        return true;
    }
    private void AfterCursorMove(Action action) {
        Cursor = Math.Clamp(Cursor, 0, Text.Length);

        if ((UIInput.Down(Keys.LeftShift) || UIInput.Down(Keys.RightShift)) && action != Action.Edit) {
            Selection.End = Cursor;
        }
    }

    private void OnTextInput(char chr) {
        // Taken from an ASCII table
        const char BACKSPACE = (char)0x08;
        const char DELETE = (char)0x7F;
        const char HOME = (char)0x02;
        const char END = (char)0x03;
        
        // Ignore enter (at least for now)
        if (chr == '\n' || chr == '\r') return;
        
        if (chr == BACKSPACE) {
            BeforeCursorMove(Action.Edit);
            if (Cursor <= 0) {
                Cursor = Math.Max(Cursor, 0);
                return;
            }
            if (UIInput.Down(Keys.LeftControl) || UIInput.Down(Keys.RightControl)) {
                // Delete word left
                string left = string.Join(' ', Text.Substring(0, Cursor - 1).TrimEnd().Split(' ').SkipLast(1));
                string right = Text.Substring(Cursor);
                Text = $"{left}{right}";
                Cursor = left.Length;
            } else {
                // Delete char left
                Text = Text.Remove(Cursor - 1, 1);
                Cursor--;
            }
            AfterCursorMove(Action.Edit);
            InvalidatePaint();
        } else if (chr == DELETE) {
            BeforeCursorMove(Action.Edit);
            if (Cursor >= Text.Length) {
                Cursor = Math.Min(Cursor, Text.Length);
                return;
            }
            if (UIInput.Down(Keys.LeftControl) || UIInput.Down(Keys.RightControl)) {
                // Delete word right
                string left = Text.Substring(0, Cursor);
                string right = string.Join(' ', Text.Substring(Cursor).TrimStart().Split(' ').Skip(1));
                Text = $"{left}{right}";
            } else {
                // Delete char right
                Text = Text.Remove(Cursor, 1);
            }
        } else if (chr == HOME) {
            BeforeCursorMove(Action.MoveLeft);
            Cursor = 0;
            AfterCursorMove(Action.MoveLeft);
            InvalidatePaint();
        } else if (chr == END) {
            BeforeCursorMove(Action.MoveRight);
            Cursor = Text.Length;
            AfterCursorMove(Action.MoveRight);
            InvalidatePaint();
        } else if (char.IsLetterOrDigit(chr) || char.IsPunctuation(chr) || char.IsSymbol(chr) || (chr == ' ')) {
            BeforeCursorMove(Action.Edit);
            Text = Text.Insert(Cursor, chr.ToString());
            Cursor++;
            AfterCursorMove(Action.Edit);
            InvalidatePaint();
        }

        Cursor = Math.Clamp(Cursor, 0, Text.Length);
    }
    private void OnTextEditing(string text, int start, int length) {
        //TODO: Support IMEs
    }

    private void OnPress(MouseEvent.Press e) {
        if (!Enabled) return;
        ClickCallback?.Invoke(this);

        Selection.SelectionTarget = (SelectionArea.SelectTarget)
            ((e.ConsecutiveClicks-1) % (int)SelectionArea.SelectTarget.Highest); // Woo funny casting!!

        Vector2 dxy = e.XY.ToVector2() - ScreenXY - StylePadding.GetCurrent<Padding>().LT.ToVector2();
        
        bool shouldRedraw = false;
        
        TextLabel.Style.GetCurrent(out DynamicSpriteFont font);
        Bounds bounds = new();
        
        if (UIInput.Down(Keys.LeftShift) || UIInput.Down(Keys.RightShift)) {
            if (!Selection.Active) Selection.Start = Cursor;
            //TODO: Optimize this somehow
            for (int i = 0; i <= Text.Length; i++) {
                font.TextBounds(Text[..i], Vector2.Zero, ref bounds, Vector2.One);
                Selection.End = i;

                shouldRedraw = true;
                if (bounds.X2 > dxy.X) break;
            }
        } else {
            Func<int, int> inc = GetPositionIncrement(Selection.SelectionTarget, Text);
            int prevI = 0;
            //TODO: Optimize this somehow
            for (int i = 0; i <= Text.Length; i = inc(i)) {
                font.TextBounds(Text[..i], Vector2.Zero, ref bounds, Vector2.One);
                Cursor = i;
                Selection.Start = i;
                if (Selection.SelectionTarget == SelectionArea.SelectTarget.Char) { // Skip selection for chars
                    Selection.End = -1;
                } else {
                    if (char.IsWhiteSpace(Text[prevI])) prevI++; // prevI will point to the previous space, move it to the next char
                    Selection.End = prevI;
                }
    
                shouldRedraw = true;
    
                prevI = i;
                if (bounds.X2 > dxy.X) break;
            }
        }
        
        if (shouldRedraw) InvalidatePaint();
    }
    private void OnDrag(MouseEvent.Drag e) {
        if (!Enabled) return;
        ClickCallback?.Invoke(this);
    
        Vector2 dxy = e.XY.ToVector2() - ScreenXY - StylePadding.GetCurrent<Padding>().LT.ToVector2();
        
        bool shouldRedraw = false;
        
        TextLabel.Style.GetCurrent(out DynamicSpriteFont font);
        Bounds bounds = new();
        //TODO: Optimize this somehow
        for (int i = 0; i <= Text.Length; i++) {
            font.TextBounds(Text.Substring(0, i), Vector2.Zero, ref bounds, Vector2.One);
            Selection.End = i;
            shouldRedraw = true;
            
            if (bounds.X2 > dxy.X) break;
        }
        
        if (shouldRedraw) InvalidatePaint();
    }

    private void OnFocus(FocusEvent.Focus e) {
        if (!Enabled) return;
        TextInputEXT.StartTextInput();
        TextInputEXT.SetInputRectangle(new(X, Y, W, H));
        TextInputEXT.TextInput += OnTextInput;
        TextInputEXT.TextEditing += OnTextEditing;
        InvalidatePaint();
    }
    private void OnUnfocus(FocusEvent.Unfocus e) {
        if (!Enabled) return;
        TextInputEXT.TextInput -= OnTextInput;
        TextInputEXT.TextEditing -= OnTextEditing;
        TextInputEXT.StopTextInput();
        InvalidatePaint();
    }

    public override void Update(float dt) {
        if (UIInput.PressedWithRepeat(Keys.Left)) {
            if (BeforeCursorMove(Action.MoveLeft) && Cursor > 0) {
                if (UIInput.Down(Keys.LeftControl) || UIInput.Down(Keys.RightControl)) {
                    // Move word left
                    while (Cursor > 0 && char.IsWhiteSpace(Text[Cursor - 1])) { Cursor--; } // Skip initial whitespace
                    Cursor = Text.LastIndexOf(' ', Cursor - 1) + 1;
                } else {
                    // Move char left
                    Cursor--;
                }
            }
            AfterCursorMove(Action.MoveLeft);
            InvalidatePaint();
        }
        if (UIInput.PressedWithRepeat(Keys.Right)) {
            if (BeforeCursorMove(Action.MoveRight) && Cursor < Text.Length) {
                if (UIInput.Down(Keys.LeftControl) || UIInput.Down(Keys.RightControl)) {
                    // Move word right
                    while (Cursor < Text.Length && char.IsWhiteSpace(Text[Cursor])) { Cursor++; } // Skip initial whitespace
                    Cursor = Text.IndexOf(' ', Cursor);
                    if (Cursor == -1) Cursor = Text.Length;
                } else {
                    // Move char right
                    Cursor++;
                }
            }
            AfterCursorMove(Action.MoveRight);
            InvalidatePaint();
        }
        if (Selection.Active && UIInput.PressedWithRepeat(Keys.C) && (UIInput.Down(Keys.LeftControl) || UIInput.Down(Keys.RightControl))) {
            // Copy clipboard
            SDL.SDL_SetClipboardText(Text.Substring(Selection.Min, Selection.Length));
        }
        if (UIInput.PressedWithRepeat(Keys.V) && (UIInput.Down(Keys.LeftControl) || UIInput.Down(Keys.RightControl))) {
            // Paste clipboard
            BeforeCursorMove(Action.Edit);
            string clipboard = SDL.SDL_GetClipboardText();
            Text = Text.Insert(Cursor, clipboard);
            Cursor += clipboard.Length;
            AfterCursorMove(Action.Edit);
            InvalidatePaint();
        }
        if (UIInput.Pressed(Keys.A) && (UIInput.Down(Keys.LeftControl) || UIInput.Down(Keys.RightControl))) {
            // Select everything
            Selection.Start = 0;
            Selection.End = Text.Length;
            InvalidatePaint();
        }
        
        PlaceholderLabel.Visible = Text.Length == 0;
        Style.Apply(StyleState);
        
        CursorBlinkTimer += TimeSpan.FromSeconds(dt);
        if (CursorBlinkTimer > CursorBlinkSpeed) {
            CursorBlinkTimer -= CursorBlinkSpeed;
            CursorBlinkState = !CursorBlinkState;
            InvalidatePaint();
        }
        
        base.Update(dt);
    }
    
    public override void DrawContent() {
        StyleCursor.GetCurrent(out Color cursorColor);
        StyleSelection.GetCurrent(out Color selectionColor);
        
        if (PrevCursor != Cursor || Selection != PrevSelection || PrevCursorColor != cursorColor || PrevSelectionColor != selectionColor || PrevCursorBlinkState != CursorBlinkState) {
            MeshShapes<MiniVertex> shapes = CursorMesh.Shapes;
            shapes.Clear();

            TextLabel.Style.GetCurrent(out DynamicSpriteFont font);
            
            if (Selection.Active) {
                Bounds selStartBounds = new();
                Bounds selEndBounds = new();
                font.TextBounds(Text.Substring(0, Selection.Min), Vector2.Zero, ref selStartBounds, Vector2.One);
                font.TextBounds(Text.Substring(0, Selection.Max), Vector2.Zero, ref selEndBounds, Vector2.One);
                
                const float Margin = 1f;
                shapes.Add(new MeshShapes.Rect() {
                    Color = selectionColor,
                    XY1 = new(TextLabel.RealX + selStartBounds.X2 - Margin, TextLabel.RealY - Margin),
                    XY2 = new(TextLabel.RealX + selEndBounds.X2 + Margin, TextLabel.RealY + TextLabel.H + Margin),
                    Radius = 5f,
                });
            } else if (CursorBlinkState) {
                Bounds cursorBounds = new();
                font.TextBounds(Text.Substring(0, Cursor), Vector2.Zero, ref cursorBounds, Vector2.One);
        
                shapes.Add(new MeshShapes.Rect() {
                    Color = cursorColor,
                    XY1 = new(TextLabel.RealX + cursorBounds.X2, TextLabel.RealY),
                    Size = new(1.5f, TextLabel.H),
                    Radius = 50f,
                });
            }
            
            shapes.AutoApply();
        }
        
        base.DrawContent();
        
        if (Focused && Enabled) {
            UIDraw.Recorder.Add((CursorMesh, ScreenXY), static ((BasicMesh mesh, Vector2 xy) data) => {
                UI.SpriteBatch.End();

                Matrix transform = UI.CreateTransform(data.xy);
                data.mesh.Draw(transform);

                UI.SpriteBatch.BeginUI();
            });
        }

        PrevCursor = Cursor;
        PrevSelection = Selection;
        PrevCursorColor = cursorColor;
        PrevSelectionColor = selectionColor;
        PrevCursorBlinkState = CursorBlinkState;
    }

    private static Func<int, int> GetPositionIncrement(SelectionArea.SelectTarget selectTarget, string text) {
        return selectTarget switch {
            SelectionArea.SelectTarget.Char => i => i + 1,
            SelectionArea.SelectTarget.Word => i => {
                if (i >= text.Length) return i + 1; // if `i` was already outside, make sure it really is
                if (char.IsWhiteSpace(text[i])) i++;
                while (i < text.Length && !char.IsWhiteSpace(text[i])) i++;
                return i;
            },
            SelectionArea.SelectTarget.Line => i => {
                while (i < text.Length && text[i] != Environment.NewLine[0]) i++;
                return i + Environment.NewLine.Length - 1;
            },
            SelectionArea.SelectTarget.Highest => i => throw new ArgumentOutOfRangeException(nameof(selectTarget), selectTarget, null),
            _ => throw new ArgumentOutOfRangeException(nameof(selectTarget), selectTarget, null)
        };
    }

    public new abstract partial class StyleKeys {
        public static readonly Style.Key Normal = new("Normal");
        public static readonly Style.Key Hovered = new("Hovered");
        public static readonly Style.Key Focused = new("Focused");
        public static readonly Style.Key Disabled = new("Disabled");
    }
}