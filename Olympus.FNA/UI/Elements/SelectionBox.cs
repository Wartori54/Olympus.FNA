using Microsoft.Xna.Framework;
using Olympus;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;

namespace OlympUI {
    public partial class SelectionBox : ScrollBox {

        public new readonly ObservableCollection<ISelectionBoxEntry> Content = new();

        private BasicMesh BackgroundMesh;
        protected Style.Entry StyleBackground = new(new ColorFader(0x08, 0x08, 0x08, 0xd0));
        
        private Color PrevBackground;
        private bool PrevClip;
        private Point PrevWH;

        public int SelectedIdx { get; private set; } = -1;

        public ISelectionBoxEntry? Selected => SelectedIdx == -1 ? null : Content[SelectedIdx];

        public Action<SelectionBox>? Callback;

        public SelectionBox() {
            Content.CollectionChanged += ContentUpdate;
            base.Content = new Group() {
                 Style = {
                     { Group.StyleKeys.Spacing, 0 },
                 },
                 Layout = {
                     Layouts.Fill(1, 0),
                     Layouts.Column()
                 },
                 Children = {}
            };
            foreach (ISelectionBoxEntry elem in Content) {
                base.Content.Children.Add(GenerateEntry(elem));
            }

            BackgroundMesh = new BasicMesh(UI.Game);
        }

        private void ContentUpdate(object? sender, NotifyCollectionChangedEventArgs args) {
            switch (args.Action) {
                case NotifyCollectionChangedAction.Add: // Optimize for adding
                    if (args.NewItems != null)
                        foreach (ISelectionBoxEntry newEl in args.NewItems) {
                            base.Content.Children.Add(GenerateEntry(newEl));
                        }
                    break;
                case NotifyCollectionChangedAction.Move:
                case NotifyCollectionChangedAction.Remove:
                case NotifyCollectionChangedAction.Replace:
                case NotifyCollectionChangedAction.Reset: // Regenerate for everything else
                    base.Content.DisposeChildren();
                    SelectedIdx = -1;
                    foreach (ISelectionBoxEntry el in Content) {
                        base.Content.Children.Add(GenerateEntry(el));
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private static Element GenerateEntry(ISelectionBoxEntry entry) {
            SelectablePanel panel = new(click => false) { // ignore normal clicks
                Layout = {
                    Layouts.Column(),
                    Layouts.Fill(1, 0),
                },
                Style = {
                    { Group.StyleKeys.Spacing, 0 },
                    { Panel.StyleKeys.Radius, new FloatFader(0f) },
                },
                Children = {
                    new HeaderSmall(entry.GetTitle()),
                }
            };
            foreach (Element e in entry.GetContents()) {
                panel.Children.Add(e);
            }

            return panel;
        }
        
        private void OnClick(MouseEvent.Click e) {
            // using indexes with foreach may be dirty, but using .Select from LINQ seemed to cause lag 
            int childIdx = 0;
            foreach (Element element in base.Content.Children) {
                if (element.Contains(e.XY)) {
                    if (SelectedIdx == childIdx) {
                        // Unselect it instead 
                        SelectedIdx = -1;
                    } else
                        SelectedIdx = childIdx;
                    
                    int entryIdx = 0;
                    foreach (ISelectionBoxEntry entry in Content) {
                        entry.OnUpdate(entryIdx == SelectedIdx);
                        entryIdx++;
                    }

                    int elemIdx = 0;
                    foreach (Element elem in base.Content.Children) {
                        (elem as SelectablePanel)!.Selected = SelectedIdx == elemIdx;
                        elemIdx++;
                    }
                    
                    break;
                }
                childIdx++;
            }
            Callback?.Invoke(this);
        }

        public override void DrawContent() {
            Point wh = WH;
            StyleBackground.GetCurrent(out Color background);
            if (PrevBackground != background ||
                PrevClip != Clip ||
                PrevWH != wh) {
                BackgroundMesh.Shapes.Clear();
                BackgroundMesh.Shapes.Add(new MeshShapes.Rect() {
                    Color = Color.Black * 0.1f,
                    Size = new Vector2(WH.X, WH.Y),
                });
                BackgroundMesh.Shapes.AutoApply();
            }
            
            UIDraw.Recorder.Add(
                (BackgroundMesh, ScreenXY),
                static ((BasicMesh background, Vector2 xy) data)  => {
                    UI.SpriteBatch.End();

                    Matrix transform = UI.CreateTransform(data.xy);
                    data.background.Draw(transform);

                    UI.SpriteBatch.BeginUI();
                }
            );
            
            base.DrawContent();
            PrevBackground = background;
            PrevClip = Clip;
            PrevWH = wh;
        }
        protected override void Dispose(bool disposing) {
            if (IsDisposed)
                return;
            base.Dispose(disposing);

            BackgroundMesh?.Dispose();
        }
    }

    public interface ISelectionBoxEntry {
        public string GetTitle(); 
        public IEnumerable<Element> GetContents();
        public void OnUpdate(bool b); // Return an action to be run after selecting or unselecting
        // the bool indicates whether its currently selected

    }
}