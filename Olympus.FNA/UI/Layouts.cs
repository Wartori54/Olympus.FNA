using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace OlympUI {
    public static class Layouts {

        private static (int whole, float fract) Fract(float num) {
            const float threshold = 0.005f;
            int whole;
            float fract;

            if (num < 0f) {
                (whole, fract) = Fract(-num);
                return (-whole, -0.9999f <= num || num <= 0.9999f ? -fract : fract);
            }

            fract = num % 1f;
            if (fract <= threshold) {
                fract = 0f;
            } else if (fract >= 1 - threshold) {
                fract = 1f;
            }

            return ((int) num, fract);
        }

        private static int ResolveConstsX(Element el, Element p, int value) {
            switch (value) {
                case LayoutConsts.Prev:
                    {
                        value = 0;
                        if (!p.Style.TryGetCurrent(Group.StyleKeys.Spacing, out int spacing))
                            spacing = 0;
                        foreach (Element sibling in p.Children) {
                            if (sibling == el)
                                break;
                            value += sibling.W + spacing;
                        }
                        return value;
                    }

                case LayoutConsts.Next: {
                        bool invalidated = false;
                        value = 0;
                        if (!p.Style.TryGetCurrent(Panel.StyleKeys.Spacing, out int spacing))
                            spacing = 0;
                        bool skip = true;
                        foreach (Element sibling in p.Children) {
                            if (sibling == el) {
                                skip = false;
                                continue;
                            } else if (skip) {
                                continue;
                            }

                            // We depend on elements that will be relfowed later, as such if those aren't yet, just queue a new update
                            if (!sibling.HasBeenReflowed && !invalidated) {
                                invalidated = true;
                                p.InvalidateFull();
                            }
                                
                            value += sibling.W + spacing;
                        }
                        return value;
                    }

                case LayoutConsts.Free:
                    int offs = ResolveConstsX(el, p, LayoutConsts.Prev);
                    return p.InnerWH.X - offs;

                case LayoutConsts.Pos:
                    return (int) MathF.Floor(el.XY.X);

                default:
                    return value;
            }
        }

        private static int ResolveConstsY(Element el, Element p, int value) {
            switch (value) {
                case LayoutConsts.Prev:
                    {
                        value = 0;
                        if (!p.Style.TryGetCurrent(Group.StyleKeys.Spacing, out int spacing))
                            spacing = 0;
                        foreach (Element sibling in p.Children) {
                            if (sibling == el)
                                break;
                            value += sibling.H + spacing;
                        }
                        return value;
                    }

                case LayoutConsts.Next:
                    {
                        value = 0;
                        if (!p.Style.TryGetCurrent(Group.StyleKeys.Spacing, out int spacing))
                            spacing = 0;
                        bool skip = true;
                        foreach (Element sibling in p.Children) {
                            if (sibling == el) {
                                skip = false;
                                continue;
                            } else if (skip) {
                                continue;
                            }
                            value += sibling.H + spacing;
                        }
                        return value;
                    }

                case LayoutConsts.Free:
                    return p.InnerWH.Y - ResolveConstsY(el, p, LayoutConsts.Prev);

                case LayoutConsts.Pos:
                    return (int) MathF.Floor(el.XY.Y);

                default:
                    return value;
            }
        }

        public static (LayoutPass, LayoutSubpass, Action<LayoutEvent>) Prio(LayoutPass pass, (LayoutPass, LayoutSubpass, Action<LayoutEvent>) layout) {
            layout.Item1 = pass;
            return layout;
        }

        public static (LayoutPass, LayoutSubpass, Action<LayoutEvent>) Prio(LayoutSubpass subpass, (LayoutPass, LayoutSubpass, Action<LayoutEvent>) layout) {
            layout.Item2 = subpass;
            return layout;
        }

        public static (LayoutPass, LayoutSubpass, Action<LayoutEvent>) Prio(LayoutPass pass, LayoutSubpass subpass, (LayoutPass, LayoutSubpass, Action<LayoutEvent>) layout) {
            layout.Item1 = pass;
            layout.Item2 = subpass;
            return layout;
        }

        public static (LayoutPass, LayoutSubpass, Action<LayoutEvent>, PositionerData) Column(OrdererBehavior behavior) =>
            Column(null, behavior);

        public static (LayoutPass, LayoutSubpass, Action<LayoutEvent>, PositionerData) Column(int? spacing = null, OrdererBehavior behavior = OrdererBehavior.Resize) => (
            LayoutPass.Normal, LayoutSubpass.Late,
            e => DoColumn(e.Element, spacing, behavior),
            new PositionerData(e => { // Fix overflows and overlaps
                return;
                // If last element does not overflow return early, elements are correct
                if (e.Element.Children.Count == 0 || e.Element.Children[^1].RealXYWH.Bottom < e.Element.H) {
                    return;
                }
                
                Element el = e.Element;
                List<(int idx, FillData data)> fillTargets = new(el.Children.Count);

                int fakeHeight = el.InnerWH.Y;
                // Calculate the length that we have to fill
                // And index all the elements that have to be re-filled for later use
                for (int i = 0; i < el.Children.Count; i++) {
                    Element child = el.Children[i];
                    // If it has a fill continue
                    if (child.Layout.LayoutInfo.TryGetValue(LayoutHandlers.LayoutDataType.Fill, out LayoutHandlers.LayoutData? fillData)) {
                        fillTargets.Add((i, (FillData) fillData));
                        continue;
                    }

                    fakeHeight -= child.H;
                }

                // Invoke fill on all the others
                for (int i = 0; i < fillTargets.Count; i++) {
                    Element child = el.Children[fillTargets[i].idx];
                    FillData data = fillTargets[i].data;
                    
                    DoFill(data.FractX, data.FractY, child, new Point(el.InnerWH.X, fakeHeight), el.Padding.LT,
                        Point.Zero);
                    child.WH -= new Point(data.OffsX > 0 ? data.OffsX : 0, data.OffsY > 0 ? data.OffsY : 0);
                }
                
                DoColumn(e.Element, spacing, behavior);
            }, spacing)
        );

        private static void DoColumn(Element el, int? spacing, OrdererBehavior behavior) {
            int spacingReal;
            if (spacing is not null) {
                spacingReal = spacing.Value;
            } else if (!el.Style.TryGetCurrent(Group.StyleKeys.Spacing, out spacingReal)) {
                spacingReal = 0;
            }
            Padding padding = el.Padding;
            Vector2 offs = padding.LT.ToVector2();
            int y = 0;
            foreach (Element child in el.Children) {
                child.RealXY = child.XY + offs + new Vector2(0f, y);
                y += child.H + spacingReal;
            }
            if (behavior == OrdererBehavior.Resize || (behavior == OrdererBehavior.Fit && el.H < y + padding.B))
                el.H = y + padding.B;
                
        }

        public static (LayoutPass, LayoutSubpass, Action<LayoutEvent>, PositionerData) Row(OrdererBehavior behavior) =>
            Row(null, behavior);

        public static (LayoutPass, LayoutSubpass, Action<LayoutEvent>, PositionerData) Row(int? spacing = null, OrdererBehavior behavior = OrdererBehavior.Resize) => (
            LayoutPass.Normal, LayoutSubpass.Late,
            e => DoRow(e.Element, spacing, behavior),
            new PositionerData(e => { // Fix overflows and overlaps
                return;
                // If last element does not overflow return early, elements are correct
                if (e.Element.Children.Count == 0 || e.Element.Children[^1].RealXYWH.Right < e.Element.W) {
                    return;
                }
                
                Element el = e.Element;
                List<(int idx, FillData data)> fillTargets = new(el.Children.Count);

                int fakeWidth = el.InnerWH.X;
                // Calculate the length that we have to fill
                // And index all the elements that have to be re-filled for later use
                for (int i = 0; i < el.Children.Count; i++) {
                    Element child = el.Children[i];
                    // If it has a fill continue
                    if (child.Layout.LayoutInfo.TryGetValue(LayoutHandlers.LayoutDataType.Fill, out LayoutHandlers.LayoutData? fillData)) {
                        fillTargets.Add((i, (FillData) fillData));
                        continue;
                    }

                    fakeWidth -= child.W;
                }

                bool didChanges = false;
                // Invoke fill on all the others
                for (int i = 0; i < fillTargets.Count; i++) {
                    Element child = el.Children[fillTargets[i].idx];
                    FillData data = fillTargets[i].data;
                    
                    DoFill(data.FractX, data.FractY, child, new Point(fakeWidth, el.InnerWH.Y), el.Padding.LT,
                        Point.Zero);
                    didChanges = true;
                }
                
                if (didChanges)
                    DoRow(e.Element, spacing, behavior);
            }, spacing)
        );

        private static void DoRow(Element el, int? spacing, OrdererBehavior behavior) {
            int spacingReal;
            if (spacing is not null) {
                spacingReal = spacing.Value;
            } else if (!el.Style.TryGetCurrent(Group.StyleKeys.Spacing, out spacingReal)) {
                spacingReal = 0;
            }
            Padding padding = el.Padding;
            Vector2 offs = padding.LT.ToVector2();
            int x = 0;
            foreach (Element child in el.Children) {
                child.RealXY = child.XY + offs + new Vector2(x, 0f);
                x += child.W + spacingReal;
            }
            if (behavior == OrdererBehavior.Resize || (behavior == OrdererBehavior.Fit && el.W < x + padding.R))
                el.W = x + padding.R;
        }

        public static (LayoutPass, LayoutSubpass, Action<LayoutEvent>) Left(int offs = 0) => (
            LayoutPass.Post, LayoutSubpass.AfterChildren,
            e => {
                Element el = e.Element;
                Element? p = el.Parent;
                if (p is null)
                    return;
                el.X = ResolveConstsX(el, p, offs);
                el.RealX = p.Padding.L + el.X;
            }
        );

        public static (LayoutPass, LayoutSubpass, Action<LayoutEvent>) Left(float fract, float offs = 0f) => (
            LayoutPass.Post, LayoutSubpass.AfterChildren,
            e => {
                Element el = e.Element;
                Element? p = el.Parent;
                if (p is null)
                    return;
                (int offsWhole, float offsFract) = Fract(offs);
                el.X = (int) Math.Floor(p.InnerWH.X * fract + offsWhole + el.W * offsFract);
                el.RealX = p.Padding.L + el.X;
            }
        );

        public static (LayoutPass, LayoutSubpass, Action<LayoutEvent>) Top(int offs = 0) => (
            LayoutPass.Post, LayoutSubpass.AfterChildren,
            e => {
                Element el = e.Element;
                Element? p = el.Parent;
                if (p is null)
                    return;
                el.Y = ResolveConstsY(el, p, offs);
                el.RealY = p.Padding.T + el.Y;
            }
        );

        public static (LayoutPass, LayoutSubpass, Action<LayoutEvent>) Top(float fract, float offs = 0f) => (
            LayoutPass.Post, LayoutSubpass.AfterChildren,
            e => {
                Element el = e.Element;
                Element? p = el.Parent;
                if (p is null)
                    return;
                (int offsWhole, float offsFract) = Fract(offs);
                el.Y = (int) Math.Floor(p.InnerWH.Y * fract + offsWhole + el.H * offsFract);
                el.RealY = p.Padding.T + el.Y;
            }
        );

        public static (LayoutPass, LayoutSubpass, Action<LayoutEvent>) Right(int offs = 0) => (
            LayoutPass.Post, LayoutSubpass.AfterChildren,
            e => {
                Element el = e.Element;
                Element? p = el.Parent;
                if (p is null)
                    return;
                el.X = p.InnerWH.X - el.W - offs;
                el.RealX = p.Padding.L + el.X;
                if (el.ID == "MetaAlertSceneClose") {
                    Console.WriteLine(el.X);
                    Console.WriteLine(el.RealX);
                }
            }
        );

        public static (LayoutPass, LayoutSubpass, Action<LayoutEvent>) Bottom(int offs = 0) => (
            LayoutPass.Post, LayoutSubpass.AfterChildren,
            e => {
                Element el = e.Element;
                Element? p = el.Parent;
                if (p is null)
                    return;
                el.Y = p.InnerWH.Y - el.H - offs;
                el.RealY = p.Padding.T + el.Y;
            }
        );

        public static (LayoutPass, LayoutSubpass, Action<LayoutEvent>) Move(int offsX = 0, int offsY = 0) => (
            LayoutPass.Post, LayoutSubpass.AfterChildren,
            e => {
                Element el = e.Element;
                Element? p = el.Parent;
                if (p is null)
                    return;
                if (offsX != 0 && offsY != 0) {
                    el.XY += new Vector2(offsX, offsY);
                    el.RealXY += new Vector2(offsX, offsY);
                } else if (offsX != 0) {
                    el.X += offsX;
                    el.RealXY = new(el.RealXY.X + offsX, el.RealXY.Y);
                } else if (offsY != 0) {
                    el.Y += offsY;
                    el.RealXY = new(el.RealXY.X, el.RealXY.Y + offsY);
                }
            }
        );
        public static (LayoutPass, LayoutSubpass, Action<LayoutEvent>) FitChildren(bool horizontally, bool vertically) => (
            LayoutPass.Post, LayoutSubpass.AfterChildren,
            e => {
                Element el = e.Element;
                Point wh = new();
                if (!horizontally)
                    wh.X = el.WH.X;
                if (!vertically)
                    wh.Y = el.WH.Y;
                foreach (Element child in el.Children) {
                    if (horizontally && child.X + child.W > wh.X) {
                        wh.X = child.X + child.W;
                    }

                    if (vertically && child.Y + child.H > wh.Y) {
                        wh.Y = child.Y + child.H;
                    }
                }

                el.WH = wh;
            }
        );

        public static (LayoutPass, LayoutSubpass, Action<LayoutEvent>, FillData) Fill(float fractX = 1f, float fractY = 1f, int offsX = 0, int offsY = 0) {
            return (
                LayoutPass.Normal, LayoutSubpass.Pre + 1,
                e => {
                    Element? p = e.Element.Parent;
                    if (p is null) return;
                    DoFill(fractX, fractY, e.Element, p.InnerWH, p.Padding.LT, Point.Zero);
                    int roffsX = ResolveConstsX(e.Element, p, offsX);
                    // offsX = roffsX;
                    int roffsY = ResolveConstsY(e.Element, p, offsY);
                    e.Element.WH -= new Point(roffsX, roffsY);
                },
                new FillData(fractX, fractY, offsX, offsY, false)
            );
        }

        private static void DoFill(float fractX, float fractY, Element el, Point parentBounds, Point parentPadding, Point parentOrigin) {
            // int roffsX = ResolveConstsX(el, p, offsX);
            // offsX = roffsX;
            // int roffsY = ResolveConstsY(el, p, offsY);
            if (fractX > 0f && fractY > 0f) {
                el.XY = parentOrigin.ToVector2();
                el.RealXY = parentPadding.ToVector2();
                el.WH = (parentBounds.ToVector2() * new Vector2(fractX, fractY)).ToPoint();// -
                        // new Point(roffsX, roffsY);
            } else if (fractX > 0f) {
                el.X = parentOrigin.X;
                el.RealXY = new(parentPadding.X, el.RealXY.Y);
                el.W = (int) (parentBounds.X * fractX);// - roffsX;
            } else if (fractY > 0f) {
                el.Y = parentOrigin.Y;
                el.RealXY = new(el.RealXY.X, parentPadding.Y);
                el.H = (int) (parentBounds.Y * fractY);// - roffsY;
            }
        }

        public static (LayoutPass, LayoutSubpass, Action<LayoutEvent>) FillFull(float fractX = 1f, float fractY = 1f, int offsX = 0, int offsY = 0) => (
            LayoutPass.Normal, LayoutSubpass.Pre + 1,
            e => {
                Element? p = e.Element.Parent;
                if (p is null) return;
                DoFill(fractX, fractY, e.Element, p.WH, Point.Zero, p.Padding.LT * new Point(-1, -1));
                // offsX = ResolveConstsX(el, p, offsX);
                // offsY = ResolveConstsY(el, p, offsY);
                // if (fractX > 0f && fractY > 0f) {
                //     el.XY = -p.Padding.LT.ToVector2();
                //     el.RealXY = new(0, 0);
                //     el.WH = (p.WH.ToVector2() * new Vector2(fractX, fractY)).ToPoint() - new Point(offsX, offsY);
                // } else if (fractX > 0f) {
                //     el.XY.X = -p.Padding.L;
                //     el.RealXY = new(0, el.RealXY.Y);
                //     el.W = (int) (p.WH.X * fractX) - offsX;
                // } else if (fractY > 0f) {
                //     el.XY.Y = -p.Padding.T;
                //     el.RealXY = new(el.RealXY.X, 0);
                //     el.H = (int) (p.WH.Y * fractY) - offsY;
                // }
            }
        );

        public static (LayoutPass, LayoutSubpass, Action<LayoutEvent>) Grow(int offsX = 0, int offsY = 0) => (
            LayoutPass.Normal, LayoutSubpass.Pre + 1,
            e => {
                Element el = e.Element;
                Element? p = el.Parent;
                if (p is null)
                    return;
                if (offsX != 0 && offsY != 0) {
                    el.WH += new Point(offsX, offsY);
                } else if (offsX != 0) {
                    el.W += offsX;
                } else if (offsY != 0) {
                    el.H += offsY;
                }
            }
        );

        public static (LayoutPass, LayoutSubpass, Action<LayoutEvent>) LayoutRespect(int tries = 2) => (
            LayoutPass.Normal,
            LayoutSubpass.Force,
            e => {
                if (tries == 0) return;
                bool success = true;
                Rectangle bounds = Point.Zero.WithSize(e.Element.InnerWH);
                foreach (Element child in e.Element.Children) {
                    if (!bounds.Contains(child.RealXYWH)) {
                        child.InvalidateFull();
                        success = false;
                    }
                }

                if (success) {
                    tries = 2;
                } else {
                    tries--;
                    if (tries == 0) {
                        Console.WriteLine(e.Element + " failed to respect");
                    }
                }
            });

    }

    // Could be an enum but building more overloads is pain.
    public static class LayoutConsts {
        public const int ConstOffset = int.MinValue;
        public const int Prev = ConstOffset + 1;
        public const int Next = ConstOffset + 2;
        public const int Free = ConstOffset + 3;
        public const int Pos = ConstOffset + 4;
        public const int Largest = Pos;
    }

    public enum OrdererBehavior {
        None,
        Fit,
        Resize,
    }

    public record FillData(float FractX, float FractY, int OffsX, int OffsY, bool IsFull) 
        : LayoutHandlers.LayoutData(LayoutHandlers.LayoutDataType.Fill);

    public record PositionerData(Action<LayoutEvent> Fixer, int? Spacing)
        : LayoutHandlers.LayoutData(LayoutHandlers.LayoutDataType.Positioner);
}
