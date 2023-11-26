using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using SDL2;
using System;
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

        public bool Active => Start >= 0 && End >= 0 && Start != End;
    }
    
    public new static readonly Style DefaultStyle = new() {
        {
            StyleKeys.Normal,
            new Style {
                { Panel.StyleKeys.Background, new Color(0x30, 0x30, 0x30, 0xff) },
                { StyleKeys.Foreground, new Color(0xe8, 0xe8, 0xe8, 0xff) },
                { StyleKeys.Placeholder, new Color(0x88, 0x88, 0x88, 0xff) },
                { StyleKeys.Cursor, new Color(0xe8, 0xe8, 0xe8, 0xff) },
                { StyleKeys.Selection, new Color(0x00, 0x33, 0x55, 0x7f) },
                { Panel.StyleKeys.Border, new Color(0x38, 0x38, 0x38, 0x80) },
                { Panel.StyleKeys.Shadow, 0.5f },
            }
        },
        {
            StyleKeys.Disabled,
            new Style {
                { Panel.StyleKeys.Background, new Color(0x70, 0x70, 0x70, 0xff) },
                { StyleKeys.Foreground, new Color(0x30, 0x30, 0x30, 0xff) },
                { Panel.StyleKeys.Border, new Color(0x28, 0x28, 0x28, 0x70) },
                { Panel.StyleKeys.Shadow, 0.2f },
            }
        },

        { Panel.StyleKeys.BorderSize, 1f },
        { Panel.StyleKeys.Radius, 4f },
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
        set => TextLabel.Text = MaxLength > 0 ? value.Substring(0, Math.Min(value.Length, MaxLength)) : value;
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
        }
    }

    public Action<TextInput>? ClickCallback;
    public Action<TextInput>? TextCallback;
    
    private readonly Label TextLabel;
    private readonly Label PlaceholderLabel;
    private readonly BasicMesh CursorMesh;

    private int PrevCursor = -1;
    private SelectionArea PrevSelection = new() { Start = -1 };
    private Color PrevCursorColor = Color.Transparent;
    private Color PrevSelectionColor = Color.Transparent;
    
    public TextInput(string text, string placeholder = "") {
        CursorMesh = new BasicMesh(UI.Game) {
            Texture = Assets.White
        };
        
        Style.Apply(Enabled ? StyleKeys.Normal : StyleKeys.Disabled);
        
        Children.Add(TextLabel = new Label(text) {
            Style = {
                { Label.StyleKeys.Color, Style.GetLink(StyleKeys.Foreground) }
            }
        });
        Children.Add(PlaceholderLabel = new Label(placeholder) {
            Style = {
                //FIXME: Using the StyleKey just doesnt work?
                // { Label.StyleKeys.Color, Style.GetLink(StyleKeys.Placeholder) }
                { Label.StyleKeys.Color, new Color(0x88, 0x88, 0x88, 0xff) }
            },
            Visible = text.Length == 0,
        });
        
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
    private void AfterCursorMove() {
        Cursor = Math.Clamp(Cursor, 0, Text.Length);

        if (UIInput.Down(Keys.LeftShift) || UIInput.Down(Keys.RightShift)) {
            Selection.End = Cursor;
        }
    }

    private void OnTextInput(char chr) {
        // Taken from an ASCII table
        const char BACKSPACE = (char)0x08;
        const char DELETE = (char)0x7F;
        const char HOME = (char)0x02;
        const char END = (char)0x03;
        const char SYNCHRONOUS_IDLE = (char)0x16;
        
        // Ctrl+V seems to trigger this for some reason..
        if (chr == SYNCHRONOUS_IDLE) return;
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
            AfterCursorMove();
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
            AfterCursorMove();
            InvalidatePaint();
        } else if (chr == END) {
            BeforeCursorMove(Action.MoveRight);
            Cursor = Text.Length;
            AfterCursorMove();
            InvalidatePaint();
        } else {
            BeforeCursorMove(Action.Edit);
            Text = Text.Insert(Cursor, chr.ToString());
            Cursor++;
            AfterCursorMove();
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

        Vector2 dxy = e.XY.ToVector2() - ScreenXY - StylePadding.GetCurrent<Padding>().LT.ToVector2();
        Console.WriteLine(dxy);
        
        TextLabel.Style.GetCurrent(out DynamicSpriteFont font);
        Bounds bounds = new();
        //TODO: Optimize this somehow
        for (int i = 0; i <= Text.Length; i++) {
            font.TextBounds(Text.Substring(0, i), Vector2.Zero, ref bounds, Vector2.One);
            Cursor = i;
            Selection.Start = i;
            Selection.End = -1;
            
            if (bounds.X2 > dxy.X) break;
        }
    }
    private void OnDrag(MouseEvent.Drag e) {
        if (!Enabled) return;
        ClickCallback?.Invoke(this);
    
        Vector2 dxy = e.XY.ToVector2() - ScreenXY - StylePadding.GetCurrent<Padding>().LT.ToVector2();
        
        TextLabel.Style.GetCurrent(out DynamicSpriteFont font);
        Bounds bounds = new();
        //TODO: Optimize this somehow
        for (int i = 0; i <= Text.Length; i++) {
            font.TextBounds(Text.Substring(0, i), Vector2.Zero, ref bounds, Vector2.One);
            Selection.End = i;
            
            if (bounds.X2 > dxy.X) break;
        }
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
            AfterCursorMove();
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
            AfterCursorMove();
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
            AfterCursorMove();
            InvalidatePaint();
        }
        if (UIInput.Pressed(Keys.A) && (UIInput.Down(Keys.LeftControl) || UIInput.Down(Keys.RightControl))) {
            // Select everything
            Selection.Start = 0;
            Selection.End = Text.Length;
            InvalidatePaint();
        }
        
        PlaceholderLabel.Visible = Text.Length == 0;
        Style.Apply(Enabled ? StyleKeys.Normal : StyleKeys.Disabled);
        InvalidatePaint();
        
        base.Update(dt);
    }
    
    public override void DrawContent() {
        StyleCursor.GetCurrent(out Color cursorColor);
        StyleSelection.GetCurrent(out Color selectionColor);
        
        if (PrevCursor != Cursor || Selection != PrevSelection || PrevCursorColor != cursorColor || PrevSelectionColor != selectionColor) {
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
            } else {
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
        
        if (Focused) {
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
    }

    public new abstract partial class StyleKeys {
        public static readonly Style.Key Normal = new("Normal");
        public static readonly Style.Key Disabled = new("Disabled");
    }
}