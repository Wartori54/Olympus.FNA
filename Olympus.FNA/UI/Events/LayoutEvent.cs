using OlympUI.Events;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace OlympUI;

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

    public override void Invoke(IEventReceive el) {
        throw new InvalidOperationException("LayoutEvents should not be Invoked");
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

    // Layout event cache
    private readonly Dictionary<Type, List<(LayoutPassAttribute, MethodInfo)>> AttributeCache = new();

    internal readonly List<HandlerList> Handlers = new();
    internal readonly Dictionary<LayoutPass, HandlerList> HandlerMap = new();
    
    public readonly Dictionary<LayoutDataType, LayoutData> LayoutInfo = new();

    public readonly HashSet<LayoutPass> PassesApplied = new();

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
        LayoutInfo.Clear();
    }

    public void Reset() {
        Clear();
        AddAttributeHandlers();
    }

    // Scans or adds from cache all the delegates for the owner element
    private void AddAttributeHandlers() {
        for (Type? type = Owner.GetType(); type != null; type = type.BaseType) {
            if (AttributeCache.TryGetValue(type, out List<(LayoutPassAttribute, MethodInfo)>? list)) {
                foreach ((LayoutPassAttribute attr, MethodInfo mi) tuple in list) {
                    Add(tuple.attr.Pass ?? LayoutPass.Normal,
                        tuple.attr.Subpass ?? LayoutSubpass.AfterChildren,
                        tuple.mi.CreateDelegate<Action<LayoutEvent>>(Owner));
                }
            
                continue;
            }

            List<(LayoutPassAttribute, MethodInfo)> cache = new();
            foreach ((LayoutPassAttribute? layoutPassAttribute, MethodInfo member) entry in type
                         .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                         .Select(member => (member.GetCustomAttribute<LayoutPassAttribute>(), member))) {
                if (entry.layoutPassAttribute == null) continue;
                Add(entry.layoutPassAttribute.Pass ?? LayoutPass.Normal,
                    entry.layoutPassAttribute.Subpass ?? LayoutSubpass.AfterChildren,
                    entry.member.CreateDelegate<Action<LayoutEvent>>(Owner));
                cache.Add(entry!);
            }

            AttributeCache.Add(type, cache);
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

    // Handy tuple version for IEnumerable initializers
    public void Add((LayoutPass Pass, LayoutSubpass Subpass, Action<LayoutEvent> Handler) args)
        => Add(args.Pass, args.Subpass, args.Handler);

    public void Add(LayoutPass pass, LayoutSubpass subpass, Action<LayoutEvent> handler) {
        List<Action<LayoutEvent>> list = GetHandlers(pass, subpass);
        list.Add(handler);
    }

    public void Add(LayoutPass pass, LayoutSubpass subpass, Action<LayoutEvent> handler, LayoutData data) {
        Add(pass, subpass, handler);
        if (!LayoutInfo.TryAdd(data.DataType, data)) {
            throw new NotSupportedException("Detected duplicate layouts!");
        }
    }

    // Handy tuple version for IEnumerable initializers
    public void Add((LayoutPass pass, LayoutSubpass subpass, Action<LayoutEvent> handler, LayoutData data) args)
        => Add(args.pass, args.subpass, args.handler, args.data);

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

        PassesApplied.Add(e.Pass);
        // Done!
        e.Reset();
        return e;
    }

    public LayoutEvent Invoke(LayoutEvent e) {
        if (!HandlerMap.TryGetValue(e.Pass, out HandlerList? handlers)) {
            PassesApplied.Add(e.Pass);
            // No passes registered currently, pass the event to the children if required
            RecurseToChildren(e);
            return e;
        }

        bool hasRecursedToChildren = false;

        // Iterate on the current pass
        foreach (HandlerSublist subhandlers in handlers.Handlers) {
            if (subhandlers.Pass >= LayoutSubpass.AfterChildren && !hasRecursedToChildren) {
                // AfterChildren is a special case, we've hit the first one, so invoke on children now
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

        PassesApplied.Add(e.Pass);

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

    public record LayoutData(LayoutDataType DataType);

    public enum LayoutDataType {
        Fill,
        Positioner,
    }
}
