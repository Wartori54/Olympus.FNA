using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace OlympUI {
    public class Event {

        private Element? _Target;
        public Element Target {
            get => _Target ?? throw new NullReferenceException("Event without target!");
            set => _Target = value;
        }

        private Element? _Element;
        public Element Element {
            get => _Element ?? _Target ?? throw new NullReferenceException("Event without element!");
            set {
                _Element = value;
                _Target ??= value;
            }
        }

        public EventStatus Status { get; set; }

        public long Extra { get; set; }

        public void End() {
            if (Status < EventStatus.Finished)
                Status = EventStatus.Finished;
        }

        public void Cancel() {
            if (Status < EventStatus.Cancelled)
                Status = EventStatus.Cancelled;
        }

    }

    public enum EventStatus {
        Normal,
        Finished,
        Cancelled
    }

    public interface IEventAttributeOnAdd {
        void OnAdd(Element e);
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public class AutoInteractiveEventAttribute : Attribute, IEventAttributeOnAdd {
        public void OnAdd(Element e) {
            if (e.Interactive < InteractiveMode.Process)
                e.Interactive = InteractiveMode.Process;
        }
    }

    public sealed class EventHandler {

        public readonly Type Type;
        public readonly MulticastDelegate Real;
        public readonly Action<Event> Callback;

        public EventHandler(Type type, MulticastDelegate real, Action<Event> callback) {
            Type = type;
            Real = real;
            Callback = callback;
        }

    }

    public sealed class EventHandlers : IEnumerable<EventHandler> {

        internal readonly Dictionary<Type, List<EventHandler>> HandlerMap = new();

        public readonly Element Owner;

        public EventHandlers(Element owner) {
            Owner = owner;
            Scan(owner.GetType());
        }

        public void Clear() {
            HandlerMap.Clear();
        }

        public void Reset() {
            Clear();
            Scan(Owner.GetType());
        }

        internal void Scan(Type startingType) {
            // FIXME: Cache!
            object[] registerArgs = new object[1];
            for (Type? parentType = startingType; parentType is not null && parentType != typeof(object); parentType = parentType.BaseType) {
                foreach (MethodInfo method in parentType.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)) {
                    if (!method.Name.StartsWith("On") ||
                        method.ReturnType != typeof(void) || method.GetParameters() is not ParameterInfo[] args ||
                        args.Length != 1 || args[0].ParameterType is not Type paramType ||
                        !typeof(Event).IsAssignableFrom(paramType))
                        continue;
                    object handler = method.CreateDelegate(typeof(Action<>).MakeGenericType(paramType), Owner);
                    registerArgs[0] = handler;
                    m_Add.MakeGenericMethod(paramType).Invoke(this, registerArgs);
                }
            }
        }

        public List<EventHandler> GetHandlers(Type type) {
            if (!HandlerMap.TryGetValue(type, out List<EventHandler>? list))
                HandlerMap[type] = list = new();
            return list;
        }

        private void HandleAdd<T>() where T : Event {
            // FIXME: Cache!
            foreach (Attribute attrib in typeof(T).GetCustomAttributes(true))
                if (attrib is IEventAttributeOnAdd handler)
                    handler.OnAdd(Owner);
        }

        private static readonly MethodInfo m_Add =
            typeof(EventHandlers).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(m => m.Name == nameof(Add) && m.GetGenericArguments().Length == 1) ??
            throw new Exception($"Cannot find method {nameof(Element)}.{nameof(Add)}");
        public EventHandler Add<T>(Action<T> handler) where T : Event {
            List<EventHandler> list = GetHandlers(typeof(T));
            EventHandler entry = new(typeof(T), handler, e => handler((T) e));
            list.Add(entry);
            HandleAdd<T>();
            return entry;
        }

        public EventHandler Add<T>(int index, Action<T> handler) where T : Event {
            List<EventHandler> list = GetHandlers(typeof(T));
            EventHandler entry = new(typeof(T), handler, e => handler((T) e));
            list.Insert(index, entry);
            HandleAdd<T>();
            return entry;
        }

        public void Remove<T>(Action<T> handler) where T : Event {
            List<EventHandler> list = GetHandlers(typeof(T));
            int index = list.FindIndex(h => ReferenceEquals(h.Real, handler));
            if (index != -1)
                list.RemoveAt(index);
        }

        public T Invoke<T>(T e) where T : Event {
            for (Type? type = typeof(T); type is not null && type != typeof(object); type = type.BaseType) {
                e.Status = EventStatus.Normal;
                foreach (EventHandler handler in GetHandlers(type)) {
                    e.Element = Owner;
                    handler.Callback(e);
                    switch (e.Status) {
                        case EventStatus.Normal:
                        default:
                            continue;
                        case EventStatus.Finished:
                            break;
                        case EventStatus.Cancelled:
                            return e;
                    }
                }
            }
            return e;
        }

        IEnumerator IEnumerable.GetEnumerator()
            => GetEnumerator();

        public IEnumerator<EventHandler> GetEnumerator() {
            foreach (List<EventHandler> handlers in HandlerMap.Values)
                foreach (EventHandler handler in handlers)
                    yield return handler;
        }

    }

    /// <summary>
    /// Special event that is used and handled internally.
    /// </summary>
    public sealed class LayoutEvent : Event {
        public static readonly LayoutEvent Instance = new(LayoutForce.None, true, LayoutPass.Normal, LayoutSubpass.AfterChildren);

        public LayoutForce ForceReflow;
        public bool Recursive;
        public LayoutPass Pass;
        public LayoutSubpass Subpass;

        public LayoutEvent(LayoutForce forceReflow, bool recursive, LayoutPass pass, LayoutSubpass subpass) {
            ForceReflow = forceReflow;
            Recursive = recursive;
            Pass = pass;
            Subpass = subpass;
        }

    }

    public enum LayoutForce {
        None,
        One,
        All
    }

    public enum LayoutPass {
        Pre =       -10000,
        Normal =    0,
        Late =      30000,
        Post =      50000,
        Force =     90000
    }

    public enum LayoutSubpass {
        Pre = -10000,
        BeforeChildren = -1,
        AfterChildren = 0,
        Late = 30000,
        Post = 50000,
        Force = 90000
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public class LayoutPassAttribute : Attribute {

        public LayoutPass? Pass;
        public LayoutSubpass? Subpass;

        public LayoutPassAttribute(LayoutPass pass) {
            Pass = pass;
        }

        public LayoutPassAttribute(LayoutSubpass subpass) {
            Subpass = subpass;
        }

        public LayoutPassAttribute(LayoutPass pass, LayoutSubpass subpass) {
            Pass = pass;
            Subpass = subpass;
        }

    }

    public sealed class LayoutHandlers : IEnumerable<Action<LayoutEvent>> {

        internal readonly List<HandlerList> Handlers = new();
        internal readonly Dictionary<LayoutPass, HandlerList> HandlerMap = new();

        internal class HandlerList {
            public readonly LayoutPass Pass;
            public readonly List<HandlerSublist> Handlers = new();
            public readonly Dictionary<LayoutSubpass, HandlerSublist> HandlerMap = new();
            public HandlerList(LayoutPass pass) {
                Pass = pass;
            }
        }

        internal class HandlerSublist {
            public readonly LayoutSubpass Pass;
            public readonly List<Action<LayoutEvent>> Handlers = new();
            public HandlerSublist(LayoutSubpass pass) {
                Pass = pass;
            }
        }

        public readonly Element Owner;

        public LayoutHandlers(Element owner) {
            Owner = owner;
            Scan(owner.GetType());
        }

        public void Clear() {
            Handlers.Clear();
            HandlerMap.Clear();
        }

        public void Reset() {
            Clear();
            Scan(Owner.GetType());
        }

        internal void Scan(Type startingType) {
            // FIXME: Cache!
            object[] registerArgs = new object[1];
            for (Type? parentType = startingType; parentType is not null && parentType != typeof(object); parentType = parentType.BaseType) {
                foreach (MethodInfo method in parentType.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)) {
                    if (!method.Name.StartsWith("Layout") ||
                        method.ReturnType != typeof(void) || method.GetParameters() is not ParameterInfo[] args ||
                        args.Length != 1 || args[0].ParameterType is not Type paramType ||
                        !typeof(LayoutEvent).IsAssignableFrom(paramType))
                        continue;
                    int indexOfSplit = method.Name.LastIndexOf('_');
                    if (!Enum.TryParse(
                            method.Name == "Layout" ? "Normal" :
                            indexOfSplit != -1 ? method.Name.Substring("Layout".Length, indexOfSplit - "Layout".Length) :
                            method.Name.Substring("Layout".Length),
                            out LayoutPass pass
                        ))
                        pass = LayoutPass.Normal;
                    if (indexOfSplit != -1) {
                        if (method.Name[indexOfSplit + 1] == 'P' && int.TryParse(method.Name.Substring(indexOfSplit + 2), out int offs)) {
                            pass += offs;
                        } else if (method.Name[indexOfSplit + 1] == 'M' && int.TryParse(method.Name.Substring(indexOfSplit + 2), out offs)) {
                            pass -= offs;
                        }
                    }
                    LayoutSubpass subpass = LayoutSubpass.AfterChildren;
                    if (method.GetCustomAttribute<LayoutPassAttribute>() is LayoutPassAttribute attrib) {
                        pass = attrib.Pass ?? pass;
                        subpass = attrib.Subpass ?? subpass;
                    }
                    Add(pass, subpass, method.CreateDelegate<Action<LayoutEvent>>(Owner));
                }
            }
        }

        internal HandlerList GetHandlers(LayoutPass pass) {
            if (!HandlerMap.TryGetValue(pass, out HandlerList? list)) {
                HandlerMap[pass] = list = new(pass);
                int min = 0;
                int max = Handlers.Count;
                int index = 0;
                if (max >= 0) {
                    while (max - min > 1) {
                        int mid = min + (int) Math.Ceiling((max - min) / 2D);
                        LayoutPass midPass = Handlers[mid].Pass;
                        if (pass <= midPass) {
                            max = mid;
                        } else {
                            min = mid;
                            index = mid + 1;
                        }
                    }
                    if (max == 1) {
                        if (pass <= Handlers[0].Pass) {
                            index = 0;
                        } else {
                            index = 1;
                        }
                    }
                }
                Handlers.Insert(index, list);
            }
            return list;
        }

        public List<Action<LayoutEvent>> GetHandlers(LayoutPass pass, LayoutSubpass subpass) {
            HandlerList main = GetHandlers(pass);
            if (!main.HandlerMap.TryGetValue(subpass, out HandlerSublist? list)) {
                main.HandlerMap[subpass] = list = new(subpass);
                int min = 0;
                int max = main.Handlers.Count;
                int index = 0;
                if (max >= 0) {
                    while (max - min > 1) {
                        int mid = min + (int) Math.Ceiling((max - min) / 2D);
                        LayoutSubpass midPass = main.Handlers[mid].Pass;
                        if (subpass <= midPass) {
                            max = mid;
                        } else {
                            min = mid;
                            index = mid + 1;
                        }
                    }
                    if (max == 1) {
                        if (subpass <= main.Handlers[0].Pass) {
                            index = 0;
                        } else {
                            index = 1;
                        }
                    }
                }
                main.Handlers.Insert(index, list);
            }
            return list.Handlers;
        }

        public void Add(Action<LayoutEvent> handler)
            => Add(LayoutPass.Normal, LayoutSubpass.AfterChildren, handler);
        public void Add(LayoutPass pass, Action<LayoutEvent> handler)
            => Add(pass, LayoutSubpass.AfterChildren, handler);
        public void Add(LayoutPass pass, LayoutSubpass subpass, Action<LayoutEvent> handler) {
            List<Action<LayoutEvent>> list = GetHandlers(pass, subpass);
            list.Add(handler);
        }
        public void Add((LayoutPass Pass, LayoutSubpass Subpass, Action<LayoutEvent> Handler) args) {
            List<Action<LayoutEvent>> list = GetHandlers(args.Pass, args.Subpass);
            list.Add(args.Handler);
        }
        
        public void AddUnique((LayoutPass Pass, LayoutSubpass Subpass, Action<LayoutEvent> Handler) args) {
            List<Action<LayoutEvent>> list = GetHandlers(args.Pass, args.Subpass);
            if (!list.Contains(args.Handler))
                list.Add(args.Handler);
        }

        public void Remove<T>(LayoutPass pass, LayoutSubpass subpass, Action<LayoutEvent> handler) {
            List<Action<LayoutEvent>> list = GetHandlers(pass, subpass);
            int index = list.IndexOf(handler);
            if (index != -1)
                list.RemoveAt(index);
        }

        public LayoutEvent InvokeAll(LayoutEvent e) {
            foreach (HandlerList handlers in Handlers) {
                foreach (HandlerSublist subhandlers in handlers.Handlers) {
                    e.Status = EventStatus.Normal;
                    e.Subpass = subhandlers.Pass;
                    foreach (Action<LayoutEvent> handler in subhandlers.Handlers) {
                        e.Target = Owner;
                        e.Element = Owner;
                        handler(e);
                        switch (e.Status) {
                            case EventStatus.Normal:
                            default:
                                continue;
                            case EventStatus.Finished:
                                break;
                            case EventStatus.Cancelled:
                                // This shouldn't EVER occur... right?
                                // Cancelling layout events - especially recursive ones! - would be really fatal.
                                e.Status = EventStatus.Finished;
                                return e;
                        }
                    }
                }
            }

            // Cancelling layout events - especially recursive ones! - would be really fatal.
            e.Status = EventStatus.Normal;
            return e;
        }

        public LayoutEvent Invoke(LayoutEvent e) {
            if (!HandlerMap.TryGetValue(e.Pass, out HandlerList? handlers)) {
                if (e.Recursive) {
                    foreach (Element child in e.Element.Children) {
                        child.Invoke(e);
                        if (e.Status == EventStatus.Cancelled)
                            return e;
                    }
                }
                return e;
            }

            int i;
            for (i = 0; i < handlers.Handlers.Count; i++) {
                HandlerSublist subhandlers = handlers.Handlers[i];
                if (subhandlers.Pass >= LayoutSubpass.AfterChildren)
                    break;

                e.Status = EventStatus.Normal;
                e.Subpass = subhandlers.Pass;
                foreach (Action<LayoutEvent> handler in subhandlers.Handlers) {
                    e.Target = Owner;
                    e.Element = Owner;
                    handler(e);
                    switch (e.Status) {
                        case EventStatus.Normal:
                        default:
                            continue;
                        case EventStatus.Finished:
                            break;
                        case EventStatus.Cancelled:
                            // This shouldn't EVER occur... right?
                            // Cancelling layout events - especially recursive ones! - would be really fatal.
                            e.Status = EventStatus.Finished;
                            return e;
                    }
                }
            }

            if (e.Recursive) {
                foreach (Element child in e.Element.Children) {
                    child.Invoke(e);
                    if (e.Status == EventStatus.Cancelled)
                        return e;
                }
            }

            for (; i < handlers.Handlers.Count; i++) {
                HandlerSublist subhandlers = handlers.Handlers[i];

                e.Status = EventStatus.Normal;
                e.Subpass = subhandlers.Pass;
                foreach (Action<LayoutEvent> handler in subhandlers.Handlers) {
                    e.Target = Owner;
                    e.Element = Owner;
                    handler(e);
                    switch (e.Status) {
                        case EventStatus.Normal:
                        default:
                            continue;
                        case EventStatus.Finished:
                            break;
                        case EventStatus.Cancelled:
                            // This shouldn't EVER occur... right?
                            // Cancelling layout events - especially recursive ones! - would be really fatal.
                            e.Status = EventStatus.Finished;
                            return e;
                    }
                }
            }

            // Cancelling layout events - especially recursive ones! - would be really fatal.
            e.Status = EventStatus.Normal;
            return e;
        }

        IEnumerator IEnumerable.GetEnumerator()
            => GetEnumerator();

        public IEnumerator<Action<LayoutEvent>> GetEnumerator() {
            foreach (HandlerList handlers in Handlers)
                foreach (HandlerSublist subhandlers in handlers.Handlers)
                    foreach (Action<LayoutEvent> handler in subhandlers.Handlers)
                        yield return handler;
        }

    }

    [AutoInteractiveEvent]
    public class MouseEvent : Event {

        public MouseState StatePrev;
        public MouseState State;

        public Point XY => State.ToPoint();
        public Point DXY => new(
            State.X - StatePrev.X,
            State.Y - StatePrev.Y
        );

        public MouseEvent() {
            StatePrev = UIInput.MousePrev;
            State = UIInput.Mouse;
        }

        public class Move : MouseEvent {
        }

        public class Enter : MouseEvent {
        }

        public class Leave : MouseEvent {
        }

        public class Drag : MouseEvent {
        }

        public class ButtonEvent : MouseEvent {

            public MouseButtons Button;
            public bool Dragging;

        }

        public class Press : ButtonEvent {
        }

        public class Release : ButtonEvent {
        }

        public class Click : ButtonEvent {
        }

        public class Scroll : MouseEvent {

            public Point ScrollDXY;

            public Scroll() {
                ScrollDXY = UIInput.MouseScrollDXY;
            }

        }

    }
}
