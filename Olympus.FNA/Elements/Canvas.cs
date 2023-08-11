using Microsoft.Xna.Framework;
using OlympUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace Olympus {

    public class Canvas : Element {
        

        private LayoutHandlers _Layout;

        private readonly Dictionary<uint, CanvasUpdateFunc> ChildrenUpdates = new();

        private ObservableCollection<(Element, CanvasUpdateFunc)> _Content;

        public float LastDt = 0f;
        public ObservableCollection<(Element, CanvasUpdateFunc)> Content {
            get => _Content;
            set {
                _Content.Clear();
                foreach ((Element, CanvasUpdateFunc) data in value) {
                    _Content.Add(data);
                }
            }
        }

        public override LayoutHandlers Layout {
            get => _Layout;
            set {
                throw new InvalidOperationException("Cannot change layout on canvas element");
            }
        }

        protected override bool IsComposited => false;

        public Canvas() {
            _Layout = new(this);
            _Layout.Add(Layouts.Fill(1,1));
            _Content = new ObservableCollection<(Element, CanvasUpdateFunc)>();

            Children.CollectionChanged += (sender, args) => {
                switch (args.Action)
                {
                    case NotifyCollectionChangedAction.Add when args.NewItems == null:
                        return;
                    case NotifyCollectionChangedAction.Add: {
                        foreach (Element? element in args.NewItems) {
                            if (element == null) continue;
                            ChildrenUpdates.Add(element.UniqueID,
                                (_, _, _) => throw new InvalidOperationException("Canvas action not generated yet"));
                            element.Layout.Add(CanvasLayout(this));
                        }

                        break;
                    }
                    case NotifyCollectionChangedAction.Reset:
                    {
                        if (sender is not ObservableCollection<Element> collection) return;
                        ChildrenUpdates.Clear();
                        foreach (Element element in collection) {
                            ChildrenUpdates.Add(element.UniqueID,
                                (_, _, _) => throw new InvalidOperationException("Canvas action not generated yet"));
                            element.Layout.AddUnique(CanvasLayout(this));
                        }

                        break;
                    }
                    default:
                        throw new InvalidOperationException("The " + args.Action + " is unsupported on Canvas children");
                }

                // if (args.Action is NotifyCollectionChangedAction.Remove or NotifyCollectionChangedAction.Replace) {
                //     if (args.OldItems == null) return;
                //     foreach (Element? element in args.OldItems) {
                //         if (element == null) continue;
                //         ChildrenUpdates.Remove(element.UniqueID);
                //     }
                // }
                //
                // if (args.Action == NotifyCollectionChangedAction.Reset) {
                //     if (sender is not ObservableCollection<Element> collection) return;
                //     ChildrenUpdates.Clear();
                //     foreach (Element element in collection) {
                //         ChildrenUpdates.Add(element.UniqueID,
                //             () => throw new InvalidOperationException("Canvas action not generated yet"));
                //         element.Layout.Add(CanvasLayout(this));
                //     }
                // }
            };

            Content.CollectionChanged += (sender, args) => {
                switch (args.Action) {
                    case NotifyCollectionChangedAction.Add when args.NewItems == null:
                        return;
                    case NotifyCollectionChangedAction.Add: {
                        for (int i = 0; i < args.NewItems.Count; i++) {
                            object? obj = args.NewItems[i];
                            if (obj is not ValueTuple<Element, CanvasUpdateFunc> data) return;
                            Children.Add(data.Item1);
                            ChildrenUpdates[data.Item1.UniqueID] = data.Item2;
                        }

                        break;
                    }
                    case NotifyCollectionChangedAction.Reset: {
                        if (sender is not ObservableCollection<(Element, CanvasUpdateFunc)> collection) return;
                        ChildrenUpdates.Clear();
                        foreach ((Element element, CanvasUpdateFunc func) in collection) {
                            Children.Add(element);
                            ChildrenUpdates[element.UniqueID] = func;
                        }

                        break;
                    }
                    default:
                        throw new InvalidOperationException("The " + args.Action +
                                                            " is unsupported on Canvas children");
                }
            };
        }

        public override void Update(float dt) {
            LastDt = dt;
            foreach ((Element element, CanvasUpdateFunc _) in _Content) {
                if (element.Reflowing) continue;
                element.ForceFullReflow();
            }
            
            InvalidatePaint();
            base.Update(dt);
        }

        private static (LayoutPass, LayoutSubpass, Action<LayoutEvent>) CanvasLayout(Canvas canvas)
        => (LayoutPass.Force, LayoutSubpass.Force, 
                e => {
                    if (e.Element.Parent == null)
                        return;
                    if (!canvas.ChildrenUpdates.TryGetValue(e.Element.UniqueID, out CanvasUpdateFunc? func)) {
                        return;
                    }
                    var data = func.Invoke(canvas.LastDt, canvas.WH, e.Element);
                    if (data.Item1 != new Vector2(-1, -1)) {
                        e.Element.XY = data.Item1;
                        e.Element.RealXY = e.Element.XY + e.Element.Parent.XY;
                    }

                    if (data.Item2 != new Point(-1, -1))
                        e.Element.WH = data.Item2;
                }
            );
        
        public delegate (Vector2, Point) CanvasUpdateFunc(float dt, Point size, Element el); 
    }
    
    
}