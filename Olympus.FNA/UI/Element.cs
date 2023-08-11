using Microsoft.Xna.Framework;
using OlympUI.MegaCanvas;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;

namespace OlympUI {
    public abstract class Element : IEnumerable<Element>, IDisposable {

        private static uint TotalElementCount;
        private static readonly Random RandIDGen = new(0);

        public static readonly Style DefaultStyle = new() {
        };

        public static Action<Element> Cast<T>(Action<T> cb) where T : Element
            => el => cb((T) el);

        public static readonly NullElement Null = new();

        protected bool IsDisposed;

        private readonly Style _Style;
        public Style Style {
            get => _Style;
            set => _Style.Apply(value);
        }

        private readonly Data _Data;
        public Data Data {
            get => _Data;
            set => _Data.Apply(value);
        }

        private EventHandlers _Events;
        public EventHandlers Events {
            get => _Events;
            set {
                if (value != _Events)
                    throw new InvalidOperationException();
            }
        }

        private LayoutHandlers _Layout;
        public virtual LayoutHandlers Layout {
            get => _Layout;
            set {
                if (value != _Layout)
                    throw new InvalidOperationException();
            }
        }

        public Action<Element> Init {
            get => throw new CompilerSatisfactionException();
            set => value(this);
        }

        public uint Collection;

        protected uint UpdateID;
        public bool UpdatePending {
            get => UpdateID != UI.GlobalUpdateID;
            set => UpdateID = value ? 0 : UI.GlobalUpdateID;
        }
        public bool RevivePending {
            get => UpdateID != UI.GlobalUpdateID - 1;
            set => UpdateID = value ? 0 : (UI.GlobalUpdateID - 1);
        }

        protected uint ReflowID;
        protected uint ReflowLoopCycles;
        protected uint ReflowLoopCyclesMax = 10;
        public bool Reflowing {
            get => ReflowID != UI.GlobalReflowID;
            set {
                ReflowLoopProtect();
                ReflowID = value ? 0 : UI.GlobalReflowID;
            }
        }

        protected uint RepaintID;
        public bool Repainting {
            get => RepaintID != UI.GlobalRepaintID;
            set => RepaintID = value ? 0 : UI.GlobalRepaintID;
        }

        protected abstract bool IsComposited { get; }

        protected uint CachedPaintID;

        protected uint ConsecutiveCachedPaints;
        protected uint ConsecutiveUncachedPaints;
        protected uint EffectiveCachedPaints;

        protected IReloadable<RenderTarget2DRegion, RenderTarget2DRegionMeta>? CachedTexture;


        internal bool Awakened;

        public float UpdateHiddenTime;


        private readonly ObservableCollection<Modifier> _Modifiers = new();
        private readonly List<Modifier> _ModifiersUpdate = new();
        private readonly List<Modifier> _ModifiersUpdateAdd = new();
        private readonly List<Modifier> _ModifiersUpdateRemove = new();
        private readonly List<Modifier> _ModifiersDraw = new();
        public ObservableCollection<Modifier> Modifiers {
            get => _Modifiers;
            set {
                _Modifiers.Clear();
                if (value is null)
                    return;
                foreach (Modifier modifier in value)
                    _Modifiers.Add(modifier);
            }
        }


        #region Parameters

        /// <summary>
        /// User-defined or otherwise expected position.
        /// </summary>
        public Vector2 XY;
        public int X {
            get => (int) XY.X;
            set => XY.X = value;
        }
        public int Y {
            get => (int) XY.Y;
            set => XY.Y = value;
        }

        /// <summary>
        /// User-defined or otherwise expected size.
        /// </summary>
        public Point WH;
        public int W {
            get => WH.X;
            set => WH.X = value;
        }
        public int H {
            get => WH.Y;
            set => WH.Y = value;
        }

        public bool Visible = true;

        public virtual InteractiveMode Interactive { get; set; } = InteractiveMode.Pass;

        public virtual bool? ForceDrawAllChildren { get; protected set; }

        public virtual bool? Cached { get; set; } = null;
        public virtual Padding ClipExtend { get; set; } = 8;
        public virtual CanvasPool? CachePool { get; set; }

        protected bool _Clip = false;
        public virtual bool Clip {
            get => _Clip;
            set {
                if (_Clip == value)
                    return;
                _Clip = value;
                InvalidateFull();
            }
        }

        #endregion


        #region Hierarchy

        private readonly uint _UniqueID;
        private readonly int _RandID;
        
        public uint UniqueID => _UniqueID;

        private readonly string _IDFallback;
        private string? _ID;
        public virtual string? ID {
            get => _ID ?? _IDFallback;
            set => _ID = value?.NotEmpty();
        }

        public readonly HashSet<string> Classes = new();

        public Element? Parent;
        private readonly ObservableCollection<Element> _Children = new();
        public ObservableCollection<Element> Children {
            get => _Children;
            set {
                _Children.Clear();
                if (value is null)
                    return;
                foreach (Element child in value)
                    _Children.Add(child);
            }
        }

        public readonly SiblingCollection Siblings;

        public string Path {
            get {
                StringBuilder sb = new();
                sb.Append(ID);

                for (Element? el = Parent; el is not null; el = el.Parent) {
                    sb.Insert(0, ".");
                    sb.Insert(0, el.ID);
                }

                return sb.ToString();

            }
        }

        public bool IsRooted {
            get {
                for (Element? el = this; el is not null; el = el.Parent)
                    if (el == UI.Root)
                        return true;
                return false;
            }
        }

        public Element this[int i] => Children[i];
        public Element this[string id] => GetChild(id) ?? throw new Exception($"Element \"{Path}\" doesn't contain any child with ID \"{id}\"");

        public bool Is<T>() where T : Element => As<T>() is not null;
        public T? As<T>() where T : Element => this as T;

        #endregion


        #region Real positions

        private Vector2? _RealXY;

        /// <summary>
        /// Actual position inside of parent.
        /// </summary>
        public Vector2 RealXY {
            get => _RealXY ?? XY;
            set => _RealXY = value;
        }
        public float RealX {
            get => RealXY.X;
            set => RealXY = new(value, RealXY.Y);
        }
        public float RealY {
            get => RealXY.Y;
            set => RealXY = new(RealXY.X, value);
        }
        public void ResetRealXY() => _RealXY = null;

        public Rectangle RealXYWH => RealXY.ToPoint().WithSize(WH);

        protected Vector2? PaintingScreenXY;
        public Vector2 ScreenXY {
            get {
                if (PaintingScreenXY is Vector2 cached)
                    return cached;

                Vector2 xy = RealXY;
                for (Element? el = Parent; el is not null; el = el.Parent) {
                    if (el.PaintingScreenXY is Vector2 offs) {
                        xy += offs;
                        break;
                    }
                    xy += el.RealXY;
                }

                return xy;
            }
            set {
                RealXY = XY = value - ScreenXY;
                if (PaintingScreenXY is not null)
                    PaintingScreenXY = value;
            }
        }

        public virtual Padding Padding => new();
        public virtual Point InnerWH => WH - Padding.WH;

        public Rectangle ScreenXYWH => ScreenXY.ToPoint().WithSize(WH);

        #endregion


        #region Status

        public bool Alive => UI.Root.Collection == Collection;

        public bool Hovered => Owns(UI.Hovering);
        public bool Pressed => Owns(UI.Dragging) && Hovered;
        public bool Dragged => Owns(UI.Dragging);
        public bool Focused => Owns(UI.Focusing);

        public Rectangle? OnScreen { get; protected set; }
        public Rectangle? OnScreenExtended { get; protected set; }
        internal void InternalSetOnScreen(Rectangle? value) => OnScreen = value;
        internal void InternalSetOnScreenExtended(Rectangle? value) => OnScreenExtended = value;
        public bool Contains(Point xy) => OnScreen?.Contains(xy) ?? false;
        public bool ContainsExtended(Point xy) => OnScreenExtended?.Contains(xy) ?? false;

        #endregion


        public Element() {
            _UniqueID = Interlocked.Increment(ref TotalElementCount);
            _RandID = RandIDGen.Next();
            _IDFallback = $"({GetType().Name}:{_UniqueID})";

            _Style = new(this);
            _Data = new();
            _Events = new(this);
            _Layout = new(this);

            Siblings = new(this);

            Children.CollectionChanged += OnChildrenCollectionChanged;
            Modifiers.CollectionChanged += OnModifiersCollectionChanged;

            SetupStyleEntries();
        }

        ~Element() {
            Dispose(false);
        }

        protected virtual void Dispose(bool disposing) {
            if (IsDisposed)
                return;
            IsDisposed = true;

            CachedTexture?.Dispose();
            CachedTexture = null;
        }

        public void Dispose() {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void SetupStyleEntries() {
        }

        private void OnModifiersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) {
            switch (e.Action) {
                case NotifyCollectionChangedAction.Add:
                    foreach (Modifier item in e.NewItems ?? throw new NullReferenceException("Modifier add didn't give new items")) {
                        item.Attach(this);
                        if (item.Meta.Update)
                            _ModifiersUpdateAdd.Add(item);
                        if (item.Meta.ModifyDraw)
                            _ModifiersDraw.Add(item);
                    }
                    break;

                case NotifyCollectionChangedAction.Remove:
                    foreach (Modifier item in e.OldItems ?? throw new NullReferenceException("Modifier remove didn't give old items")) {
                        item.Detach();
                        if (item.Meta.Update)
                            _ModifiersUpdateRemove.Add(item);
                        if (item.Meta.ModifyDraw)
                            _ModifiersDraw.Remove(item);
                    }
                    break;

                case NotifyCollectionChangedAction.Reset: // BIG NOTE: this doesn't only imply a .clear operation was performed
                    // Because of the above, just regenerate everything from the actual state of the collection
                    
                    // Remove everything, since all of this may be invalid
                    _ModifiersDraw.Clear();
                    _ModifiersUpdate.Clear();
                    _ModifiersUpdateAdd.Clear();
                    _ModifiersUpdateRemove.Clear();
                    
                    // Just refill from the beginning
                    foreach (Modifier item in sender as ObservableCollection<Modifier> ??
                                              throw new NullReferenceException("Modifiers clear didn't give sender")) {
                        item.Attach(this);
                        if (item.Meta.Update)
                            _ModifiersUpdateAdd.Add(item);
                        if (item.Meta.ModifyDraw)
                            _ModifiersDraw.Add(item);
                        
                    }
                    break;

            }
        }

        #region Recursion

        public Element Add(Element child) {
            Children.Add(child);
            return child;
        }

        public T Add<T>(T child) where T : Element {
            Children.Add(child);
            return child;
        }

        public bool Remove(Element child) {
            return Children.Remove(child);
        }

        public bool Remove<T>(T child) where T : Element {
            return Children.Remove(child);
        }

        public bool RemoveSelf() {
            return Parent?.Remove(this) ?? false;
        }

        public void Clear()
            => Children.Clear();

        public void DisposeChildren() {
            foreach (Element child in Children) {
                child.DisposeChildren();
                child.Dispose();
            }

            Children.Clear();
        }

        IEnumerator IEnumerable.GetEnumerator()
            => GetEnumerator();
        public IEnumerator<Element> GetEnumerator()
            => Children.GetEnumerator();

        public bool Owns(Element? other) {
            if (other is null)
                return false;
            for (Element? el = other; el is not null; el = el.Parent)
                if (el == this)
                    return true;
            return false;
        }

        public void ForEach(Action<Element> cb) {
            cb(this);
            foreach (Element child in Children)
                child.ForEach(cb);
        }

        public T? ForEach<T>(Func<Element, T?> cb) where T : class {
            T? rv;
            if ((rv = cb(this)) is not null)
                return rv;
            foreach (Element child in Children)
                if ((rv = child.ForEach(cb)) is not null)
                    return rv;
            return null;
        }

        #endregion


        #region Invalidation

        private void OnChildrenCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) {
            switch (e.Action) {
                case NotifyCollectionChangedAction.Add:
                    HashSet<Element> nulls = new();
                    foreach (Element item in e.NewItems ?? throw new NullReferenceException("Child add didn't give new items")) {
                        if (item is NullElement) {
                            nulls.Add(item);
                            continue;
                        }
                        item.Parent = this;
                        item.InvalidateFull();
                        item.InvalidateFullDown();
                    }
                    foreach (Element item in nulls)
                        Children.Remove(item);
                    break;

                case NotifyCollectionChangedAction.Remove:
                    foreach (Element item in e.OldItems ?? throw new NullReferenceException("Child remove didn't give old items")) {
                        item.Parent = null;
                    }
                    break;

                case NotifyCollectionChangedAction.Reset:
                    foreach (Element item in sender as ObservableCollection<Element> ?? throw new NullReferenceException("Child clear didn't give sender")) {
                        item.Parent = null;
                    }
                    break;

            }

            UI.Root.InvalidateCollect();
            UI.Root.InvalidateForce();  // TODO: Performance sink, look into only reflowing the modified elements
            InvalidateFull();
        }

        public virtual void InvalidateFull() {
            for (Element? el = this; el is not null; el = el.Parent) {
                el.Reflowing = true;
                el.Repainting = true;
            }
        }

        public virtual void InvalidateFullDown() {
            foreach (Element el in Children) {
                el.Reflowing = true;
                el.Repainting = true;
                el.InvalidateFullDown();
            }
        }

        public virtual void InvalidatePaint() {
            for (Element? el = this; el is not null; el = el.Parent) {
                el.Repainting = true;
            }
        }

        public virtual void InvalidatePaintDown() {
            foreach (Element el in Children) {
                el.Repainting = true;
                el.InvalidatePaintDown();
            }
        }

        public virtual void InvalidateCachedTexture() {
            for (Element? el = this; el is not null; el = el.Parent) {
                el.CachedTexture?.Dispose();
                el.CachedTexture = null;
            }
        }

        public virtual void InvalidateCachedTextureDown() {
            foreach (Element el in Children) {
                el.CachedTexture?.Dispose();
                el.CachedTexture = null;
                el.InvalidateCachedTextureDown();
            }
        }

        #endregion


        #region Main Loop

        public virtual void Awake() {
        }

        public virtual void Revive() {
            Style.Revive();
        }

        public virtual void Update(float dt) {
            Style.Update(dt);

            UpdateModifiersUpdate();

            if (_ModifiersUpdate.Count != 0) {
                foreach (Modifier modifier in _ModifiersUpdate)
                    modifier.Update(dt);

                UpdateModifiersUpdate();
            }
        }

        public virtual void UpdateHidden(float dt) {
        }

        public virtual void DrawContent() {
            if (ForceDrawAllChildren ?? ((Cached ?? true) || !Clip)) {
                foreach (Element el in Children) {
                    el.Paint();
                }
            } else {
                foreach (Element el in Children) {
                    if (el.Visible && el.OnScreen is not null) {
                        el.Paint();
                    }
                }
            }
        }

        private void UpdateModifiersUpdate() {
            foreach (Modifier modifier in _ModifiersUpdateAdd)
                _ModifiersUpdate.Add(modifier);
            _ModifiersUpdateAdd.Clear();

            foreach (Modifier modifier in _ModifiersUpdateRemove)
                _ModifiersUpdate.Remove(modifier);
            _ModifiersUpdateRemove.Clear();
        }

        #endregion


        #region Paint

        // TODO: Element painting is a general TODO area.

        public virtual void Paint() {
            if (!Visible)
                return;

            PaintingScreenXY = ScreenXY;

            PaintContent(false, true, ClipExtend);

            if (UI.GlobalDrawDebug) {
                DrawDebug();
            }

            PaintingScreenXY = null;
        }

        public virtual IReloadable<RenderTarget2DRegion, RenderTarget2DRegionMeta>? PaintToCache(Padding padding) {
            PaintingScreenXY = ScreenXY;

            PaintContent(true, false, padding);

            PaintingScreenXY = null;

            return CachedTexture;
        }

        protected virtual void DrawDebug() {
            Rectangle xywh = ScreenXYWH;
            Color c = new();
            c.PackedValue = (uint) _RandID;
            c.A = 0xff;
            UIDraw.Recorder.Add(new UICmd.DebugRect(c, xywh));
            if ((Cached ?? true) && ConsecutiveCachedPaints <= 2) {
                UIDraw.Recorder.Add(new UICmd.DebugRect(Cached is null ? Color.Green : ConsecutiveUncachedPaints <= 2 ? Color.Yellow : Color.Red, new(xywh.X + 1, xywh.Y + 1, xywh.Width - 2, xywh.Height - 2)));
            }
        }

        protected virtual void PaintContent(bool paintToCache, bool paintToScreen, Padding padding) {
            Point whTexture = WH + padding.WH;
            bool? cached = paintToCache ? true : (Cached ?? (_ModifiersDraw.Count != 0 ? true : null));
            RenderTarget2DRegion? cachedTexture = CachedTexture?.ValueValid;

            if (cached == null && IsComposited && !Clip)
                cached = false;

            bool repainting = Repainting;
            Repainting = false;

            if (cached == false /* and not null */ || (!paintToCache && UI.ForceDisableCache)) {
                CachedTexture?.Dispose();
                CachedTexture = null;
                cachedTexture = null;
                if (paintToScreen)
                    DrawContent();
                return;
            }

            bool pack = false;

            if (!repainting) {
                ConsecutiveUncachedPaints = 0;
                if (ConsecutiveCachedPaints < 32) {
                    ConsecutiveCachedPaints++;
                    if (ConsecutiveCachedPaints < 16 && cached is null && !Clip) {
                        if (paintToScreen)
                            DrawContent();
                        return;
                    }

                } else {
                    pack = true;
                }

            } else {
                if (ConsecutiveUncachedPaints < 8) {
                    ConsecutiveUncachedPaints++;

                } else if (cached is null && !Clip) {
                    CachedTexture?.Dispose();
                    CachedTexture = null;
                    cachedTexture = null;
                }

                if (cachedTexture is null && (cached is null && !Clip)) {
                    if (paintToScreen)
                        DrawContent();
                    return;
                }
            }

            if (cachedTexture is not null && (cachedTexture.RT.IsDisposed || cachedTexture.RT.Width < whTexture.X || cachedTexture.RT.Height < whTexture.Y)) {
                CachedTexture?.Dispose();
                CachedTexture = null;
                cachedTexture = null;
            }

            if ((repainting || UI.GlobalDrawDebug) && cachedTexture is not null && cachedTexture.Page is not null) {
                CachedTexture?.Dispose();
                CachedTexture = null;
                cachedTexture = null;
            }

            if (cachedTexture is null) {
                if (CachedTexture is not null) {
                    cachedTexture = CachedTexture.ValueLazy;
                    if (cachedTexture is null)
                        CachedTexture = null;
                }
                if (cachedTexture is null) {
                    if (CachedTexture is null)
                        CachedTexture = Reloadable.Temporary(default(RenderTarget2DRegionMeta), () => (CachePool ?? UI.MegaCanvas.PoolMSAA).Get(whTexture.X, whTexture.Y), true);
                    cachedTexture = CachedTexture.Value;
                }
                EffectiveCachedPaints = 0;
                ConsecutiveCachedPaints = 0;
                repainting = true;
            }

            if (cachedTexture is null) {
                if (paintToScreen)
                    DrawContent();
                return;
            }

            Debug.Assert(CachedTexture is not null);

            if (EffectiveCachedPaints < 16) { 
                EffectiveCachedPaints++;
                if (EffectiveCachedPaints == 16 && (CachePool ?? UI.MegaCanvas.PoolMSAA) is { } cachePool &&
                    (cachedTexture.UsedRegion.GetArea() >= (whTexture + new Point(cachePool.Padding, cachePool.Padding)).GetArea() * 1.2f)) {
                    CachedTexture.Dispose();
                    CachedTexture = Reloadable.Temporary(default(RenderTarget2DRegionMeta), () => cachePool.Get(whTexture.X, whTexture.Y), true);
                    cachedTexture = CachedTexture.Value;
                    repainting = true;
                }
            }

            Vector2 xy = ScreenXY;
            if (repainting || UI.GlobalDrawDebug) {
                CachedPaintID++;

                UIDraw.Push(cachedTexture, -xy + padding.LT.ToVector2());

                DrawContent();

                UIDraw.Pop();

            } else if (!repainting && pack && cachedTexture.Page is null) {
                RenderTarget2DRegion? packed = UI.MegaCanvas.GetPackedAndFree(cachedTexture, new(0, 0, whTexture.X, whTexture.Y));
                if (packed is not null) {
                    CachedTexture = Reloadable.Temporary(new RenderTarget2DRegionMeta(), () => packed, _ => {
                        packed?.Dispose();
                        packed = null;
                    });
                    cachedTexture = CachedTexture.Value;
                    InvalidateCachedTextureDown();
                }
            }

            if (paintToScreen)
                DrawCachedTexture(cachedTexture, xy, padding, whTexture);
        }

        protected virtual void DrawCachedTexture(RenderTarget2DRegion rt, Vector2 xy, Padding padding, Point size) {
            UIDraw.AddDependency(rt);
            DrawModifiable(new UICmd.Sprite(
                rt.RT,
                rt.Region.WithSize(size),
                (xy.ToPoint() - padding.LT).WithSize(size),
                Color.White
            ));
        }

        protected void DrawModifiable(UICmd.Sprite cmd) {
            if (_ModifiersDraw.Count != 0)
                foreach (Modifier modifier in _ModifiersDraw)
                    modifier.ModifyDraw(ref cmd);

            UIDraw.Recorder.Add(cmd);
        }

        #endregion


        #region Finders

        public Element? GetParent(string id) {
            for (Element? el = Parent; el is not null; el = el.Parent)
                if (el.ID == id)
                    return el;
            return null;
        }

        public Element GetParent() {
            if (Parent is not null)
                return Parent;
            throw new Exception($"Failed to get parent in hierarchy {Path}");
        }

        public T GetParent<T>() where T : Element {
            for (Element? el = Parent; el is not null; el = el.Parent)
                if (el is T found)
                    return found;
            throw new Exception($"Failed to get parent of type {typeof(T)} in hierarchy {Path}");
        }

        public Element? GetChild(string id) {
            foreach (Element el in Children)
                if (el.ID == id)
                    return el;
            return null;
        }

        public T GetChild<T>() where T : Element {
            foreach (Element el in Children)
                if (el is T found)
                    return found;
            throw new Exception($"Failed to get child of type {typeof(T)} in hierarchy {Path}");
        }

        public T GetChild<T>(string id) where T : Element {
            foreach (Element el in Children)
                if (el is T found && found.ID == id)
                    return found;
            throw new Exception($"Failed to get child of with ID \"{id}\" type {typeof(T).Name} in hierarchy {Path}");
        }

        public Element? FindChild(string id) {
            foreach (Element el in Children)
                if (el.ID == id)
                    return el;
            foreach (Element el in Children)
                if (el.FindChild(id) is Element found)
                    return found;
            return null;
        }

        public T? FindChild<T>() where T : Element {
            foreach (Element el in Children)
                if (el is T found)
                    return found;
            foreach (Element el in Children)
                if (el.FindChild<T>() is Element found)
                    return (T) found;
            return null;
        }

        public T FindChild<T>(string? id) where T : Element {
            foreach (Element el in Children)
                if (el is T found && (string.IsNullOrEmpty(id) || el.ID == id))
                    return found;
            foreach (Element el in Children)
                if (el.FindChild<T>(id) is Element found)
                    return (T) found;
            throw new Exception($"Element \"{Path}\" doesn't contain any child (or subchild) with ID \"{id}\" type {typeof(T).Name}");
        }

        public Element? Hit(int mx, int my) {
            if (Interactive < InteractiveMode.Pass)
                return null;

            if (!ScreenXYWH.Contains(mx, my))
                return null;

            foreach (Element el in Children)
                if (el.Hit(mx, my) is Element found)
                    return found;

            if (Interactive < InteractiveMode.Process)
                return null;
            return this;
        }

        #endregion


        #region Event Listener Logic

        public T Invoke<T>(T e) where T : Event
            => _Events.Invoke(e);

        public T InvokeDown<T>(T e) where T : Event {
            Invoke(e);
            if (e.Status == EventStatus.Cancelled)
                return e;

            foreach (Element child in Children) {
                child.InvokeDown(e);
                if (e.Status == EventStatus.Cancelled)
                    return e;
            }

            return e;
        }

        public T InvokeUp<T>(T e) where T : Event {
            for (Element? el = this; el is not null; el = el.Parent) {
                el.Invoke(e);
                if (e.Status == EventStatus.Cancelled)
                    return e;
            }
            return e;
        }

        #endregion

        #region Layout Handler Logic

        internal void INTERNAL_ReflowLoopReset() => ReflowLoopReset();
        protected void ReflowLoopReset() {
            ReflowLoopCycles = 0;
        }

        internal void INTERNAL_ReflowLoopCount() => ReflowLoopCount();
        protected void ReflowLoopCount() {
            if (ReflowLoopCycles >= ReflowLoopCyclesMax)
                throw new Exception($"Element {this} stuck in reflow loop with unknown cause (was Reflowing ever reset?)");
            ReflowLoopCycles++;
        }

        protected void ReflowLoopProtect() {
            if (ReflowLoopCycles >= ReflowLoopCyclesMax)
                throw new Exception($"Element {this} stuck in reflow loop, detected another reflow");
        }

        public void ForceFullReflow() {
            ReflowLoopReset();
            FirstPass:
            LayoutEvent reflow = LayoutEvent.Instance;
            foreach (LayoutPass pass in UI.Root.AllLayoutPasses) {
                reflow.ForceReflow = LayoutForce.One;
                reflow.Pass = pass;
                reflow.Recursive = false;
                // No need to InvokeDown as reflows follow their own recursion rules.
                Invoke(reflow);
                if (Reflowing) {
                    ReflowLoopCount();
                    goto FirstPass;
                }
            }
        }

        public void ForceFullReflowDown() {
            ReflowLoopReset();
            FirstPass:
            LayoutEvent reflow = LayoutEvent.Instance;
            foreach (LayoutPass pass in UI.Root.AllLayoutPasses) {
                reflow.ForceReflow = LayoutForce.All;
                reflow.Pass = pass;
                reflow.Recursive = true;
                // No need to InvokeDown as reflows follow their own recursion rules.
                Invoke(reflow);
                if (Reflowing) {
                    ReflowLoopCount();
                    goto FirstPass;
                }
            }
        }

        private void OnReflow(LayoutEvent e) {
            if (!(Reflowing || this == UI.Root) && e.ForceReflow == LayoutForce.None)
                return;
            if (e.Pass == (LayoutPass) int.MaxValue)
                Reflowing = false;
            if (e.ForceReflow == LayoutForce.One)
                e.ForceReflow = LayoutForce.None;
            Repainting = true;

            Layout.Invoke(e);
        }

        #endregion

        #region Style Helpers

        public abstract partial class StyleKeys {

            protected StyleKeys(Secret secret) {
                throw new InvalidOperationException("StyleKeys cannot be instantiated.");
            }

            protected sealed class Secret {
                private Secret() { }
            }

        }

        #endregion

    }

    public class SiblingCollection : IEnumerable<Element> {

        public readonly Element Owner;

        public SiblingCollection(Element owner) {
            Owner = owner;
        }

        public Element this[int i] {
            get {
                if (i == 0)
                    return Owner;
                ObservableCollection<Element> parentChildren = Owner.Parent?.Children ?? throw new Exception("Can't get siblings of orphaned element!");
                return parentChildren[parentChildren.IndexOf(Owner) + i];
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
            => GetEnumerator();
        public IEnumerator<Element> GetEnumerator()
            => Owner.Parent?.Children.GetEnumerator() ?? Enumerable.Empty<Element>().GetEnumerator();

    }

    public enum InteractiveMode {
        Discard = -1,
        Pass = 0,
        Process = 1,
        Block = 2
    }
}
