using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System;
using System.Linq;

namespace OlympUI; 

public partial class TextInput : Panel {
    public new static readonly Style DefaultStyle = new() {
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

        { Panel.StyleKeys.BorderSize, 1f },
        { Panel.StyleKeys.Radius, 4f },
    };

    protected Style.Entry StyleForeground = new(new ColorFader());
    
    private readonly Label label;
    private readonly BasicMesh CursorMesh;
    private int PrevCursor = -1;
    
    public int Cursor = 0;
    public string Text {
        get => label.Text;
        set => label.Text = value;
    }
    public string Placeholder = "";
    
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
    
    public TextInput(string text) {
        CursorMesh = new BasicMesh(UI.Game) {
            Texture = Assets.White
        };
        Children.Add(label = new Label(text) {
            Style = {
                { Label.StyleKeys.Color, Style.GetLink(StyleKeys.Foreground) }
            }
        });
        Cursor = Text.Length;
        Console.WriteLine(Cursor);
    }

    public override void DrawContent() {
        if (PrevCursor != Cursor) {
            label.Style.GetCurrent(out DynamicSpriteFont font);
            Bounds bounds = new();
            font.TextBounds(Text.Substring(0, Cursor), Vector2.Zero, ref bounds, Vector2.One);
            
            MeshShapes<MiniVertex> shapes = CursorMesh.Shapes;
            shapes.Clear();
            shapes.Add(new MeshShapes.Rect() {
                Color = Color.White,
                XY1 = new(label.RealX + bounds.X2, label.RealY),
                Size = new(2f, label.H),
                // Radius = 50f,
            });
            Console.WriteLine($"{label.RealX} {label.RealY} {bounds.X} {bounds.Y} {bounds.X2} {bounds.Y2}");

            shapes.AutoApply();
        }
        
        if (Focused) {
            UIDraw.Recorder.Add((CursorMesh, ScreenXY), static ((BasicMesh mesh, Vector2 xy) data) => {
                UI.SpriteBatch.End();

                Matrix transform = UI.CreateTransform(data.xy);
                data.mesh.Draw(transform);

                UI.SpriteBatch.BeginUI();
            });
        }

        base.DrawContent();
        
        PrevCursor = Cursor;
    }

    private void OnTextInput(char chr) {
        Console.WriteLine($"[{Cursor}] {chr} : {(int)chr}");
        
        // Taken from an ASCII table
        const char BACKSPACE = (char)0x08;
        const char DELETE = (char)0x7F;
        const char HOME = (char)0x02;
        const char END = (char)0x03;
        
        if (chr == BACKSPACE) {
            if (Cursor <= 0) return;

            if (UIInput.Down(Keys.LeftControl) || UIInput.Down(Keys.RightControl)) {
                // Delete word left
                string left = string.Join(' ', Text.Substring(0, Cursor - 1).TrimEnd().Split(' ').SkipLast(1));
                string right = Text.Substring(Cursor);
                Console.WriteLine($"Left: {left} Right: {right}");
                Text = $"{left}{right}";
                Cursor = left.Length;
            } else {
                // Delete char left
                Text = Text.Remove(Cursor - 1, 1);
                Cursor--;
            }
            InvalidatePaint();
        } else if (chr == DELETE) {
            if (Cursor >= Text.Length) return;

            if (UIInput.Down(Keys.LeftControl) || UIInput.Down(Keys.RightControl)) {
                // Delete word right
                string left = Text.Substring(0, Cursor);
                string right = string.Join(' ', Text.Substring(Cursor).TrimStart().Split(' ').Skip(1));
                Console.WriteLine($"Left: {left} Right: {right}");
                Text = $"{left}{right}";
            } else {
                // Delete char right
                Text = Text.Remove(Cursor, 1);
            }
        } else if (chr == HOME) {
            Cursor = 0;
            InvalidatePaint();
        } else if (chr == END) {
            Cursor = Text.Length;
            InvalidatePaint();
        } else {
            Text = Text.Insert(Cursor, chr.ToString());
            Cursor++;
            InvalidatePaint();
        }
        
        Cursor = Math.Clamp(Cursor, 0, Text.Length);
    }
    private void OnTextEditing(string text, int start, int length) {
        //TODO: Support IMEs
    }

    private void OnClick(MouseEvent.Click e) {
        if (!Enabled) return;
        ClickCallback?.Invoke(this);
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
            if (UIInput.Down(Keys.LeftControl) || UIInput.Down(Keys.RightControl)) {
                // Move word left
                Cursor = Text.Substring(0, Cursor).Trim().LastIndexOf(' ') + 1;
            } else {
                // Move char left
                Cursor--;
            }
            InvalidatePaint();
        }
        if (UIInput.PressedWithRepeat(Keys.Right)) {
            if (UIInput.Down(Keys.LeftControl) || UIInput.Down(Keys.RightControl)) {
                // Move word right
                Cursor = Text.Substring(Cursor).Trim().IndexOf(' ');
                if (Cursor == -1) Cursor = Text.Length;
            } else {
                // Move char right
                Cursor++;
                // 
            }
            InvalidatePaint();
        }
        
        Cursor = Math.Clamp(Cursor, 0, Text.Length);
        
        base.Update(dt);
    }

    public new abstract partial class StyleKeys {
        public static readonly Style.Key Normal = new("Normal");
        public static readonly Style.Key Disabled = new("Disabled");
    }
}