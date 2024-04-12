using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using OlympUI.Events;
using System;
using System.Collections;
using System.Collections.Generic;

namespace OlympUI {
    public abstract class Event {
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

        public abstract void Invoke(IEventReceive el);
    }

    public enum EventStatus {
        Normal,
        Finished,
        Cancelled
    }

    /// <summary>
    /// Represents an event manager that only supports dynamic events.
    /// </summary>
    public class EventManager : IEnumerable {
        private readonly Dictionary<Type, List<Action<Event>>> DynamicEvents = new();
        
        private readonly Element Owner;
        private readonly IEventReceive? eventReceive;
        public EventManager(Element owner) {
            Owner = owner;
            eventReceive = owner as IEventReceive;
            if (eventReceive != null) // Only classes marked with this can be
                Owner.Interactive = InteractiveMode.Process;
        }

        // Invokes an event
        public void Invoke(Event ev) {
            // Do interface impls first
            if (eventReceive != null) {
                ev.Reset();
                ev.Element = Owner;
                ev.Invoke(eventReceive);
                switch (ev.Status) {
                    case EventStatus.Normal:
                    default:
                        break;
                    case EventStatus.Finished:
                    case EventStatus.Cancelled:
                        return;
                }
            }

            // Then do dynamic
            DynamicEvents.TryGetValue(ev.GetType(), out List<Action<Event>>? value);
            if (value == null) return;
            foreach (Action<Event> handler in value) {
                ev.Reset();
                ev.Element = Owner;
                handler(ev);
                switch (ev.Status) {
                    case EventStatus.Normal:
                    default:
                        continue;
                    case EventStatus.Finished:
                        break;
                    case EventStatus.Cancelled:
                        return;
                }
            }
        }
        
        public IEnumerator GetEnumerator() {
            throw new NotSupportedException();
        }

        /// <summary>
        /// Adds an event for any event type, skipping having to implement the proper interface.
        /// </summary>
        /// <param name="func">The handler.</param>
        /// <typeparam name="T">The event type.</typeparam>
        public void Add<T>(Action<T> func) where T : Event {
            Owner.Interactive = InteractiveMode.Process;
            if (!DynamicEvents.TryGetValue(typeof(T), out List<Action<Event>>? list)) {
                DynamicEvents[typeof(T)] = list = new List<Action<Event>>();
            }
            // neat wrapper to cast it
            list.Add(ev => func((T) ev));
        }
    }


    public abstract class MouseEvent : Event {

        public readonly MouseState StatePrev;
        public readonly MouseState State;

        public Point XY => State.ToPoint();
        public Point DXY => new(
            State.X - StatePrev.X,
            State.Y - StatePrev.Y
        );

        protected MouseEvent() {
            StatePrev = UIInput.MousePrev;
            State = UIInput.Mouse;
        }

        public override void Invoke(IEventReceive el) {
            if (el is IMouseEventReceiver mer) {
                InvokeSpecific(mer);
            }
        }

        protected abstract void InvokeSpecific(IMouseEventReceiver el);

        public sealed class Move : MouseEvent {
            protected override void InvokeSpecific(IMouseEventReceiver el) {
                el.OnMove(this);
            }
        }

        public sealed class Enter : MouseEvent {
            protected override void InvokeSpecific(IMouseEventReceiver el) {
                el.OnEnter(this);
            }
        }

        public sealed class Leave : MouseEvent {
            protected override void InvokeSpecific(IMouseEventReceiver el) {
                el.OnLeave(this);
            }
        }

        public sealed class Drag : MouseEvent {
            protected override void InvokeSpecific(IMouseEventReceiver el) {
                el.OnDrag(this);
            }
        }

        public abstract class ButtonEvent : MouseEvent {

            public MouseButtons Button;
            public bool Dragging;

        }

        public sealed class Press : ButtonEvent {
            public int ConsecutiveClicks;

            protected override void InvokeSpecific(IMouseEventReceiver el) {
                el.OnPress(this);
            }
        }

        public sealed class Release : ButtonEvent {
            protected override void InvokeSpecific(IMouseEventReceiver el) {
                el.OnRelease(this);
            }
        }

        public sealed class Click : ButtonEvent {
            protected override void InvokeSpecific(IMouseEventReceiver el) {
                el.OnClick(this);
            }
        }

        public sealed class Scroll : MouseEvent {

            public Point ScrollDXY;

            public Scroll() {
                ScrollDXY = UIInput.MouseScrollDXY;
            }

            protected override void InvokeSpecific(IMouseEventReceiver el) {
                el.OnScroll(this);
            }
        }
    }
    
    public abstract class FocusEvent : Event {
        public override void Invoke(IEventReceive el) {
            if (el is IFocusEventReceiver fer) {
                InvokeSpecific(fer);
            }
        }

        protected abstract void InvokeSpecific(IFocusEventReceiver el);

        public sealed class Focus : FocusEvent {
            protected override void InvokeSpecific(IFocusEventReceiver el) {
                el.OnFocus(this);
            }
        }
        public sealed class UnFocus : FocusEvent {
            protected override void InvokeSpecific(IFocusEventReceiver el) {
                el.OnUnFocus(this);
            }
        }
    }
}
