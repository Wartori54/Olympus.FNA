using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace OlympUI {
    public sealed partial class Root : Element {

        public override string? ID => "Root";

        internal bool Recollecting;
        internal bool ReflowingForce;

        private readonly AutoRotatingPool<List<Element>> AllPool = new(8);
        private readonly AutoRotatingPool<List<Element>> AllOffScreenPool = new(8);
        private readonly AutoRotatingPool<List<LayoutPass>> AllLayoutPassesPool = new(8);
        private readonly AutoRotatingPool<List<Element>> AllOnScreenPool = new(8);
        private readonly AutoRotatingPool<List<Element>> AllInteractivePool = new(8);

        public List<Element> All;
        public List<LayoutPass> AllLayoutPasses;
        public List<Element> AllOffScreen;
        public List<Element> AllOnScreen;
        public List<Element> AllInteractive;

        protected override bool IsComposited => true;

        public Root() {
            Cached = false;
            All = AllPool.Next();
            AllLayoutPasses = AllLayoutPassesPool.Next();
            AllOffScreen = AllOffScreenPool.Next();
            AllOnScreen = AllOnScreenPool.Next();
            AllInteractive = AllInteractivePool.Next();
        }

        public Element? GetInteractiveChildAt(Point xy) {
            List<Element> all = AllInteractive;
            for (int i = all.Count - 1; i >= 0; --i) {
                Element hit = all[i];
                if (!hit.Contains(xy))
                    continue;

                for (Element? el = hit.Parent; el is not null; el = el.Parent)
                    if (el.Clip && !el.Contains(xy))
                        continue;
                return hit;

            }

            return null;
        }

        public Element? GetSmallestChildAt(Point xy) {
            List<Element> all = AllOnScreen;
            if (all.Count == 0) return null;
            Element smallest = all[^1];
            for (int i = all.Count - 1; i >= 0; i--) { // Iterate in reverse order because its more likely that the smaller ones are at the bottom
                Element hit = all[i];

                if (hit.Contains(xy)) {
                    if (hit.WH.GetArea() <= smallest.WH.GetArea())
                        smallest = hit;
                }
            }
            return smallest;
        }

        public List<Element> GetAllChildrenAt(Point xy) {
            List<Element> hits = new();
            ObservableCollection<Element> target = Children;
            Element? targetChild = this;
            while (targetChild != null) {
                targetChild = null;
                foreach (Element child in target) {
                    if (!child.Contains(xy)) continue;
                    targetChild = child;
                    break;
                }

                if (targetChild != null) {
                    hits.Add(targetChild);
                    target = targetChild.Children;
                } 
            }

            return hits;
        }

        public void InvalidateForce() {
            ReflowingForce = true;
        }

        public void InvalidateCollect() {
            Recollecting = true;
        }

        public bool AutoCollect() {
            if (!Recollecting)
                return false;
            Collect();
            return true;
        }

        public void CollectLayoutPasses() {
            AllLayoutPasses = AllLayoutPassesPool.Next();
            AllLayoutPasses.Clear();
            AllLayoutPasses.Add((LayoutPass) int.MaxValue);
            CollectAllLayoutPasses(AllLayoutPasses, this);
        }

        public void Collect() {
            CollectLayoutPasses();
            All = AllPool.Next();
            All.Clear();
            AllOffScreen = AllOffScreenPool.Next();
            AllOffScreen.Clear();
            AllOnScreen = AllOnScreenPool.Next();
            AllOnScreen.Clear();
            AllInteractive = AllInteractivePool.Next();
            AllInteractive.Clear();
            CollectAllOnScreen(All, AllOffScreen, AllOnScreen, AllInteractive, this, Collection + 1, 0, 0, new() {
                Left = (int) RealXY.X,
                Top = (int) RealXY.Y,
                Right = WH.X,
                Bottom = WH.Y
            }, true);
        }

        private static void CollectAllLayoutPasses(List<LayoutPass> allLayoutPasses, Element el) {
            foreach (Element child in el.Children) {
                foreach (LayoutHandlers.HandlerList handlers in child.Layout.Handlers) {
                    LayoutPass pass = handlers.Pass;
                    if (!allLayoutPasses.Contains(pass)) {
                        int min = 0;
                        int max = allLayoutPasses.Count;
                        int index = 0;
                        if (max >= 0) {
                            while (max - min > 1) {
                                int mid = min + (int) Math.Ceiling((max - min) / 2D);
                                LayoutPass midPass = allLayoutPasses[mid];
                                if (pass <= midPass) {
                                    max = mid;
                                } else {
                                    min = mid;
                                    index = mid + 1;
                                }
                            }
                            if (max == 1) {
                                if (pass <= allLayoutPasses[0]) {
                                    index = 0;
                                } else {
                                    index = 1;
                                }
                            }
                        }
                        allLayoutPasses.Insert(index, pass);
                    }
                }
                CollectAllLayoutPasses(allLayoutPasses, child);
            }
        }

        private static void CollectAllOffScreen(List<Element> all, List<Element> allOffScreen, Element el, uint collection) {
            el.Collection = collection;
            foreach (Element child in el.Children) {
                child.InternalSetOnScreen(null);
                all.Add(child);
                allOffScreen.Add(child);
                CollectAllOffScreen(all, allOffScreen, child, collection);
            }
        }

        private static void CollectAllOnScreen(
            List<Element> all, List<Element> allOffScreen, List<Element> allOnScreen, List<Element> allInteractive,
            Element parent, uint collection,
            int parentX, int parentY, RectLTRB parentClip, bool interactive
        ) {
            foreach (Element child in parent.Children) {
                all.Add(child);

                Vector2 xy = child.RealXY;
                Point size = child.WH;
                RectLTRB rect = new();
                rect.Left = (int) xy.X + parentX;
                rect.Top = (int) xy.Y + parentY;
                rect.Right = rect.Left + size.X;
                rect.Bottom = rect.Top + size.Y;

                Padding extend = child.ClipExtend;
                RectLTRB rectExtend = new();
                rectExtend.Left = rect.Left - extend.Left;
                rectExtend.Top = rect.Top - extend.Top;
                rectExtend.Right = rect.Right + extend.Right;
                rectExtend.Bottom = rect.Bottom + extend.Bottom;

                if (
                    rectExtend.Right < parentClip.Left || parentClip.Right < rectExtend.Left ||
                    rectExtend.Bottom < parentClip.Top || parentClip.Bottom < rectExtend.Top
                ) {
                    // Fully off-screen.
                    child.InternalSetOnScreenExtended(null);
                    child.InternalSetOnScreen(null);
                    allOffScreen.Add(child);
                    CollectAllOffScreen(all, allOffScreen, child, collection);

                } else {
                    // Either fully visible, or visible via the extended area.
                    // Currently anything that needs to differentiate can do so via hit checks with the onscreen rectangle only.
                    RectLTRB visibleExtend = new();
                    visibleExtend.Left = Math.Max(parentClip.Left, rectExtend.Left);
                    visibleExtend.Top = Math.Max(parentClip.Top, rectExtend.Top);
                    visibleExtend.Right = Math.Min(parentClip.Right, rectExtend.Right);
                    visibleExtend.Bottom = Math.Min(parentClip.Bottom, rectExtend.Bottom);
                    child.InternalSetOnScreenExtended(new(
                        visibleExtend.Left, visibleExtend.Top,
                        visibleExtend.Right - visibleExtend.Left, visibleExtend.Bottom - visibleExtend.Top
                    ));
                    RectLTRB visible = new();
                    visible.Left = Math.Max(parentClip.Left, rect.Left);
                    visible.Top = Math.Max(parentClip.Top, rect.Top);
                    visible.Right = Math.Min(parentClip.Right, rect.Right);
                    visible.Bottom = Math.Min(parentClip.Bottom, rect.Bottom);
                    child.InternalSetOnScreen(new(
                        visible.Left, visible.Top,
                        visible.Right - visible.Left, visible.Bottom - visible.Top
                    ));
                    allOnScreen.Add(child);

                    RectLTRB clip = child.Clip ? visibleExtend : parentClip;

                    switch (interactive ? child.Interactive : InteractiveMode.Discard) {
                        case InteractiveMode.Discard:
                            CollectAllOnScreen(all, allOffScreen, allOnScreen, allInteractive, child, collection, rect.Left, rect.Top, clip, false);
                            break;

                        case InteractiveMode.Pass:
                            CollectAllOnScreen(all, allOffScreen, allOnScreen, allInteractive, child, collection, rect.Left, rect.Top, clip, true);
                            break;

                        case InteractiveMode.Process:
                            allInteractive.Add(child);
                            CollectAllOnScreen(all, allOffScreen, allOnScreen, allInteractive, child, collection, rect.Left, rect.Top, clip, true);
                            break;

                        case InteractiveMode.Block:
                            allInteractive.Add(child);
                            CollectAllOnScreen(all, allOffScreen, allOnScreen, allInteractive, child, collection, rect.Left, rect.Top, clip, false);
                            break;
                    }
                }
            }
        }

        private struct RectLTRB {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

    }
}
