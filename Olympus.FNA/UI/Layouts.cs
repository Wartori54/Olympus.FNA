using Microsoft.Xna.Framework;
using System;
using System.Linq;

namespace OlympUI {
    public static class Layouts {

        private static (int whole, float fract) Fract(float num, float fallback = 0f) {
            int whole;
            float fract;

            if (num < 0f) {
                (whole, fract) = Fract(-num, fallback);
                return (-whole, -0.9999f <= num || num <= 0.9999f ? -fract : fract);
            }

            fract = num % 1f;
            if (fract <= 0.0001 || fract >= 0.9999) {
                fract = fallback;
            } else if (fract <= 0.005) {
                fract = 0f;
            } else if (fract >= 0.995) {
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

                case LayoutConsts.Next:
                    {
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
                            value += sibling.W + spacing;
                        }
                        return value;
                    }

                case LayoutConsts.Free:
                    return p.InnerWH.X - ResolveConstsX(el, p, LayoutConsts.Prev);

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

        public static (LayoutPass, LayoutSubpass, Action<LayoutEvent>) Column(bool resize) =>
            Column(null, resize);

        public static (LayoutPass, LayoutSubpass, Action<LayoutEvent>) Column(int? spacing = null, bool resize = true) => (
            LayoutPass.Normal, LayoutSubpass.Late,
            (LayoutEvent e) => {
                Element el = e.Element;
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
                if (resize)
                    el.H = y + padding.B;
            }
        );

        public static (LayoutPass, LayoutSubpass, Action<LayoutEvent>) Row(bool resize) =>
            Row(null, resize);

        public static (LayoutPass, LayoutSubpass, Action<LayoutEvent>) Row(int? spacing = null, bool resize = true) => (
            LayoutPass.Normal, LayoutSubpass.Late,
            (LayoutEvent e) => {
                Element el = e.Element;
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
                if (resize)
                    el.W = x + padding.R;
            }
        );

        public static (LayoutPass, LayoutSubpass, Action<LayoutEvent>) Left(int offs = 0) => (
            LayoutPass.Post, LayoutSubpass.AfterChildren,
            (LayoutEvent e) => {
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
            (LayoutEvent e) => {
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
            (LayoutEvent e) => {
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
            (LayoutEvent e) => {
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
            (LayoutEvent e) => {
                Element el = e.Element;
                Element? p = el.Parent;
                if (p is null)
                    return;
                el.X = p.InnerWH.X - el.W - offs;
                el.RealX = p.Padding.L + el.X;
            }
        );

        public static (LayoutPass, LayoutSubpass, Action<LayoutEvent>) Bottom(int offs = 0) => (
            LayoutPass.Post, LayoutSubpass.AfterChildren,
            (LayoutEvent e) => {
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
            (LayoutEvent e) => {
                Element el = e.Element;
                Element? p = el.Parent;
                if (p is null)
                    return;
                if (offsX != 0 && offsY != 0) {
                    el.XY += new Vector2(offsX, offsY);
                    el.RealXY += new Vector2(offsX, offsY);
                } else if (offsX != 0) {
                    el.XY.X += offsX;
                    el.RealXY = new(el.RealXY.X + offsX, el.RealXY.Y);
                } else if (offsY != 0) {
                    el.XY.Y += offsY;
                    el.RealXY = new(el.RealXY.X, el.RealXY.Y + offsY);
                }
            }
        );
        public static (LayoutPass, LayoutSubpass, Action<LayoutEvent>) FitChildren(bool horizontally, bool vertically) => (
            LayoutPass.Post, LayoutSubpass.AfterChildren,
            (LayoutEvent e) => {
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

        public static (LayoutPass, LayoutSubpass, Action<LayoutEvent>) Fill(float fractX = 1f, float fractY = 1f, int offsX = 0, int offsY = 0) => (
            LayoutPass.Normal, LayoutSubpass.Pre + 1,
            (LayoutEvent e) => {
                Element el = e.Element;
                Element? p = el.Parent;
                if (p is null)
                    return;
                bool print = offsX == LayoutConsts.Next;
                int roffsX = ResolveConstsX(el, p, offsX);
                offsX = roffsX;
                if (el.ID == "DescLabelCont" && print) {
                    Console.WriteLine(roffsX + " " + el + " " + p.Children.Count);
                    if (p.Children.Count == 2) {
                        Console.WriteLine(p.Children[1] + " " + p.Children[1].WH);
                        Console.WriteLine(p.Children[1].Children[0] + " " + p.Children[1].Children[0].WH);
                    }
                }

                int roffsY = ResolveConstsY(el, p, offsY);
                if (fractX > 0f && fractY > 0f) {
                    el.XY = new(0, 0);
                    el.RealXY = p.Padding.LT.ToVector2();
                    el.WH = (p.InnerWH.ToVector2() * new Vector2(fractX, fractY)).ToPoint() - new Point(roffsX, roffsY);
                } else if (fractX > 0f) {
                    el.XY.X = 0;
                    el.RealXY = new(p.Padding.L, el.RealXY.Y);
                    el.WH.X = (int) (p.InnerWH.X * fractX) - roffsX;
                } else if (fractY > 0f) {
                    el.XY.Y = 0;
                    el.RealXY = new(el.RealXY.X, p.Padding.T);
                    el.WH.Y = (int) (p.InnerWH.Y * fractY) - roffsY;
                }
            }
        );

        public static (LayoutPass, LayoutSubpass, Action<LayoutEvent>) FillFull(float fractX = 1f, float fractY = 1f, int offsX = 0, int offsY = 0) => (
            LayoutPass.Normal, LayoutSubpass.Pre + 1,
            (LayoutEvent e) => {
                Element el = e.Element;
                Element? p = el.Parent;
                if (p is null)
                    return;
                offsX = ResolveConstsX(el, p, offsX);
                offsY = ResolveConstsY(el, p, offsY);
                if (fractX > 0f && fractY > 0f) {
                    el.XY = -p.Padding.LT.ToVector2();
                    el.RealXY = new(0, 0);
                    el.WH = (p.WH.ToVector2() * new Vector2(fractX, fractY)).ToPoint() - new Point(offsX, offsY);
                } else if (fractX > 0f) {
                    el.XY.X = -p.Padding.L;
                    el.RealXY = new(0, el.RealXY.Y);
                    el.WH.X = (int) (p.WH.X * fractX) - offsX;
                } else if (fractY > 0f) {
                    el.XY.Y = -p.Padding.T;
                    el.RealXY = new(el.RealXY.X, 0);
                    el.WH.Y = (int) (p.WH.Y * fractY) - offsY;
                }
            }
        );

        public static (LayoutPass, LayoutSubpass, Action<LayoutEvent>) Grow(int offsX = 0, int offsY = 0) => (
            LayoutPass.Normal, LayoutSubpass.Pre + 1,
            (LayoutEvent e) => {
                Element el = e.Element;
                Element? p = el.Parent;
                if (p is null)
                    return;
                if (offsX != 0 && offsY != 0) {
                    el.WH += new Point(offsX, offsY);
                } else if (offsX != 0) {
                    el.WH.X += offsX;
                } else if (offsY != 0) {
                    el.WH.Y += offsY;
                }
            }
        );

    }

    // Could be an enum but building more overloads is pain.
    public static class LayoutConsts {
        public const int ConstOffset = int.MinValue;
        public const int Prev = ConstOffset + 1;
        public const int Next = ConstOffset + 2;
        public const int Free = ConstOffset + 3;
        public const int Pos = ConstOffset + 4;
    }
}
