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

        public EventStatus Status { get; private set; }

        public long Extra { get; set; }

        public virtual void End() {
            if (Status < EventStatus.Finished)
                Status = EventStatus.Finished;
        }

        public virtual void Cancel() {
            if (Status < EventStatus.Cancelled)
                Status = EventStatus.Cancelled;
        }
        
        public virtual void Reset() {
            Status = EventStatus.Normal;
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
                e.Reset();
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

        public override void Cancel() {
            throw new InvalidOperationException("LayoutEvents cannot be canceled!");
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
            AddAttributeHandlers();
        }

        public void Clear() {
            Handlers.Clear();
            HandlerMap.Clear();
        }

        public void Reset() {
            Clear();
            AddAttributeHandlers();
        }

        private void AddAttributeHandlers() {
            for (Type? type = Owner.GetType(); type !=  null; type = type.BaseType) {
                foreach ((LayoutPassAttribute? layoutPassAttribute, MethodInfo member) entry in type
                             .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                             .Select(member => (member.GetCustomAttribute<LayoutPassAttribute>(), member))) {
                    if (entry.layoutPassAttribute != null)
                        Add(entry.layoutPassAttribute.Pass ?? LayoutPass.Normal,
                            entry.layoutPassAttribute.Subpass ?? LayoutSubpass.AfterChildren,
                            entry.member.CreateDelegate<Action<LayoutEvent>>(Owner));
                }
            }
        }
        
        /// <summary>
        /// Obtains the HandlerList associated with a certain pass.
        /// </summary>
        private HandlerList GetHandlers(LayoutPass pass) {
            if (HandlerMap.TryGetValue(pass, out HandlerList? list)) return list;
            // Does not exist yet, add it in.
            HandlerMap[pass] = list = new HandlerList(pass);
            int index = MathUtil.BinarySearch(0, Handlers.Count, i => pass >= Handlers[i].Pass);
            Handlers.Insert(index, list);
            return list;
        }

        /// <summary>
        /// Obtains the HandlerSubList associated with ta certain pass and subpass.
        /// </summary>
        /// <param name="pass"></param>
        /// <param name="subpass"></param>
        /// <returns></returns>
        private List<Action<LayoutEvent>> GetHandlers(LayoutPass pass, LayoutSubpass subpass) {
            HandlerList main = GetHandlers(pass);
            if (main.HandlerMap.TryGetValue(subpass, out HandlerSublist? list)) return list.Handlers;
            // Does not exist yet, add it in.
            main.HandlerMap[subpass] = list = new HandlerSublist(subpass);
            int index = MathUtil.BinarySearch(0, 
                main.Handlers.Count, 
                i => subpass >= main.Handlers[i].Pass);
            main.Handlers.Insert(index, list);
            return list.Handlers;
        }

        /// <summary>
        /// Adds a handler to the default pass
        /// </summary>
        public void Add(Action<LayoutEvent> handler)
            => Add(LayoutPass.Normal, LayoutSubpass.AfterChildren, handler);
        
        /// <summary>
        /// Adds a handler to a pass and default subpass
        /// </summary>
        public void Add(LayoutPass pass, Action<LayoutEvent> handler)
            => Add(pass, LayoutSubpass.AfterChildren, handler);
        
        // Handy tuple version
        public void Add((LayoutPass Pass, LayoutSubpass Subpass, Action<LayoutEvent> Handler) args)
            => Add(args.Pass, args.Subpass, args.Handler);
        
        public void Add(LayoutPass pass, LayoutSubpass subpass, Action<LayoutEvent> handler) {
            List<Action<LayoutEvent>> list = GetHandlers(pass, subpass);
            list.Add(handler);
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
                    e.Reset();
                    e.Pass = handlers.Pass;
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
                                e.Cancel();
                                return e;
                        }
                    }
                }
            }

            // Done!
            e.Reset();
            return e;
        }

        public LayoutEvent Invoke(LayoutEvent e) {
            if (!HandlerMap.TryGetValue(e.Pass, out HandlerList? handlers)) {
                // No passes registered currently, pass the event to the children if required
                RecurseToChildren(e);
                return e;
            }

            bool hasRecursedToChildren = false;

            // Iterate on the current pass
            foreach (HandlerSublist subhandlers in handlers.Handlers) {
                if (subhandlers.Pass >= LayoutSubpass.AfterChildren) { // AfterChildren is a special case, we've hit the first one, so invoke on children now
                    RecurseToChildren(e);
                    hasRecursedToChildren = true;
                }

                e.Reset();
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
                            e.Cancel();
                            return e;
                    }
                }
            }

            // Reminder that the above recurse to children may only get execute if we have a event that goes after children
            if (!hasRecursedToChildren) {
                RecurseToChildren(e);
            }

            e.Reset();
            return e;
        }

        private static void RecurseToChildren(LayoutEvent e) {
            if (!e.Recursive) return;
            foreach (Element child in e.Element.Children) {
                child.Invoke(e);
                if (e.Status == EventStatus.Cancelled) {
                    throw new InvalidOperationException();
                    continue;
                }
            }
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
            public int ConsecutiveClicks;
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
    
    [AutoInteractiveEvent]
    public class FocusEvent : Event {
        public class Focus : FocusEvent { }
        public class Unfocus : FocusEvent { }
    }
}
