using FontStashSharp;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace OlympUI {
    public sealed class Style : IEnumerable<Style.Entry> {

        public static readonly Dictionary<Type, Key> CommonKeys = new() {
            { typeof(DynamicSpriteFont), new("Font") },
        };

        private static readonly Dictionary<string, Key> ExpressionKeys = new();

        // This must sadly be an ordered list for update order. A dict can be added alongside this later if needed.
        public static readonly List<Style> TypeStyles = new();
        public static readonly Dictionary<(Type From, Type To), IConverter> Converters = new();

        private readonly Dictionary<Key, Entry> Map = new(KeyEqualityComparer.Instance);
        private readonly Dictionary<Key, uint> EnumeratorCache = new(KeyEqualityComparer.Instance);

        private Style? Parent;

        private List<Style> ParentStack = new();

        public Type? Type;
        public Element? Element;

        private event Action? OnInvalidate;
        private uint InvalidateID;
        private bool IsHierarchyInvalidated {
            get {
                for (Style? style = Parent; style is not null; style = style.Parent)
                    if (style.InvalidateID == UI.GlobalDrawID)
                        return true;
                return false;
            }
        }

        static Style() {
            foreach (Type type in UIReflection.GetAllTypes(typeof(IConverter))) {
                if (type.IsAbstract)
                    continue;

                IConverter conv = (IConverter) Activator.CreateInstance(type)!;
                foreach ((Type, Type) mapping in conv.Supported)
                    Converters[mapping] = conv;
            }
        }

        public Style() {
        }

        public Style(Element el) {
            Type = el.GetType();
            Element = el;

            SetupParent(Type);
        }

        public static T Convert<T>(object raw) {
            if (Converters.TryGetValue((raw.GetType(), typeof(T)), out IConverter? conv))
                return conv.Convert<T>(raw);
            return (T) System.Convert.ChangeType(raw, typeof(T));
        }

        public static Key GetCommonKey(Type type) {
            if (CommonKeys.TryGetValue(type, out Key key))
                return key;

            for (Type? typeBase = type; typeBase is not null; typeBase = typeBase.BaseType) {
                if (typeBase.IsConstructedGenericType && typeBase.GetGenericTypeDefinition() == typeof(Reloadable<,>))
                    return GetCommonKey(typeBase.GetGenericArguments()[0]);

                if (typeBase.IsConstructedGenericType && typeBase.GetGenericTypeDefinition() == typeof(IReloadable<,>))
                    return GetCommonKey(typeBase.GetGenericArguments()[0]);

                if (typeBase.IsConstructedGenericType && typeBase.GetGenericTypeDefinition() == typeof(Fader<>))
                    return GetCommonKey(typeBase.GetGenericArguments()[0]);
            }

            return new(type.Name);
        }

        public static Key? GetKeyFromCallerArgumentExpression(string? expr) {
            if (string.IsNullOrEmpty(expr))
                return null;

            if (ExpressionKeys.TryGetValue(expr, out Key key))
                return key;

            int split = expr.IndexOf(' ');
            string sub = char.ToUpperInvariant(expr[split + 1]) + expr.Substring(split + 2);
            if (sub.StartsWith("Style"))
                sub = expr.Substring(5);
            return ExpressionKeys[expr] = new(sub);
        }

        private void SetupParent(Type type) {
            if (Parent is not null)
                return;

            if (Element is not null) {
                Style style = this;
                for (Type? parentType = type; parentType != typeof(object) && parentType is not null; parentType = parentType.BaseType) {
                    if (parentType.GetField("DefaultStyle", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) is Style parent) {
                        parent.SetupParent(parentType);
                        style.Parent = parent;
                        // The first parent that we meet should set up the rest of the parent chain.
                        break;
                    }
                }

            } else if (Type is null) {
                Type = type;
                TypeStyles.Insert(0, this);
                Style style = this;
                for (Type? parentType = type.BaseType; parentType != typeof(object) && parentType is not null; parentType = parentType.BaseType) {
                    if (parentType.GetField("DefaultStyle", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) is Style parent) {
                        parent.SetupParent(parentType);
                        style.Parent = parent;
                        style = parent;
                    }
                }
            }

            if (Parent is not null)
                foreach (Entry entry in Map.Values)
                    entry.Parent = Parent.GetEntry(entry.Key);
        }

        public void GetEntry<T>(out Entry value)
            => value = GetEntry(GetCommonKey(typeof(T)));
#if !OLYMPUS_STYLE_NOEXPR
        public void GetEntry(out Entry value, [CallerArgumentExpression("value")] string? expr = null)
            => value = GetEntry(GetKeyFromCallerArgumentExpression(expr) ?? throw new ArgumentException($"Couldn't parse entry name from \"{expr}\"", nameof(value)));
#endif
        public Entry GetEntry<T>()
            => GetEntry(GetCommonKey(typeof(T)));
        public Entry GetEntry(Key key) {
            if (Map.TryGetValue(key, out Entry? entry))
                return entry;
            return Map[key] = new(this, key, Parent?.GetEntry(key));
        }

        public void Add<T>(Func<T> value)
            => Add(GetCommonKey(typeof(T)), (object) value);
        public void Add<T>(Key key, Func<T> value)
            => Add(key, (object) value);
        public void Add(Link value)
            => Add(value.Key, (object) value);
        public void Add(Key key, Link value)
            => Add(key, (object) value);
        public void Add<T>(Fader<T> value) where T : struct
            => Add(GetCommonKey(typeof(T)), (object) value);
        public void Add<T>(Key key, Fader<T> value) where T : struct
            => Add(key, (object) value);
        public void Add<T>(T value)
            => Add(GetCommonKey(typeof(T)), value);
        public void Add(Key key, object? value)
            => GetEntry(key).Value = value;

        public void Clear() {
            Map.Clear();
            EnumeratorCache.Clear();
        }

#if OLYMPUS_STYLE_NOEXPR
        public bool TryGetCurrent<T>([NotNullWhen(true)] out T? value)
            => TryGetCurrent(GetCommonName(typeof(T)), out value);
#else
        public bool TryGetCurrent<T>([NotNullWhen(true)] out T? value, [CallerArgumentExpression("value")] string? expr = null)
            => TryGetCurrent(GetKeyFromCallerArgumentExpression(expr) ?? GetCommonKey(typeof(T)), out value);
#endif
        public bool TryGetCurrent<T>(Key key, [NotNullWhen(true)] out T? value)
            => GetEntry(key).TryGetCurrent(out value);

#if OLYMPUS_STYLE_NOEXPR
        public void GetCurrent<T>(out T value)
            => value = GetCurrent<T>(GetCommonName(typeof(T)));
#else
        public void GetCurrent<T>(out T value, [CallerArgumentExpression("value")] string? expr = null)
            => value = GetCurrent<T>(GetKeyFromCallerArgumentExpression(expr) ?? GetCommonKey(typeof(T)));
#endif
        public void GetCurrent<T>(KeyOrValue<T> keyOrValue, out T value)
            => value = GetCurrent<T>(keyOrValue);
        public void GetCurrent<T>(Key key, out T value)
            => value = GetCurrent<T>(key);
        public T GetCurrent<T>()
            => GetCurrent<T>(GetCommonKey(typeof(T)));
        public T GetCurrent<T>(KeyOrValue<T> keyOrValue)
            => keyOrValue.HasValue ? keyOrValue.Value : GetCurrent<T>(keyOrValue.Key);
        public T GetCurrent<T>(Key key)
            => TryGetCurrent(key, out T? value) ? value : throw new Exception($"{(Element is null ? "Instance style for" : "Static style for")} \"{Type}\" doesn't define \"{key}\"");

#if OLYMPUS_STYLE_NOEXPR
        public bool TryGetReal<T>([NotNullWhen(true)] out T? value)
            => TryGetReal(GetCommonName(typeof(T)), out value);
#else
        public bool TryGetReal<T>([NotNullWhen(true)] out T? value, [CallerArgumentExpression("value")] string? expr = null)
            => TryGetReal(GetKeyFromCallerArgumentExpression(expr) ?? GetCommonKey(typeof(T)), out value);
#endif
        public bool TryGetReal<T>(Key key, [NotNullWhen(true)] out T? value)
            => GetEntry(key).TryGetReal(out value);

#if OLYMPUS_STYLE_NOEXPR
        public void GetReal<T>(out T value)
            => value = GetReal<T>(GetCommonName(typeof(T)));
#else
        public void GetReal<T>(out T value, [CallerArgumentExpression("value")] string? expr = null)
            => value = GetReal<T>(GetKeyFromCallerArgumentExpression(expr) ?? GetCommonKey(typeof(T)));
#endif
        public void GetReal<T>(Key key, out T value)
            => value = GetReal<T>(key);
        public T GetReal<T>()
            => GetReal<T>(GetCommonKey(typeof(T)));
        public T GetReal<T>(Key key)
            => TryGetReal(key, out T? value) ? value : throw new Exception($"{(Element is null ? "Instance style for" : "Static style for")} \"{Type}\" doesn't define \"{key}\"");

        public void Apply(Style value) {
            if (value is null) {
                foreach (KeyValuePair<Key, Entry> entry in Map)
                    GetEntry(entry.Key).Value = null;
            } else {
                foreach (Entry entry in value)
                    Add(entry.Key, entry.Value);
            }
        }

        public void Apply(Key key) {
            if (Element is not null) {
                if (ParentStack.Count == 0) {
                    for (Style? parent = Parent; parent is not null; parent = parent.Parent)
                        ParentStack.Insert(0, parent);
                }

                for (int i = 0; i < ParentStack.Count; i++) {
                    Style parent = ParentStack[i];
                    if (parent.Parent != null && i - 1 > 0 && parent.Parent != ParentStack[i - 1]) {
                        throw new InvalidOperationException("Somehow Parent stack for this style got modified");
                    }
                    Entry entry = parent.GetEntry(key);
                    if (entry.Value is Style value)
                        Apply(value);
                    if (entry.GetSkinnedRaw() is Dictionary<string, object> props)
                        foreach (KeyValuePair<string, object> prop in props)
                            Add(new(prop.Key), prop.Value);
                }
            }

            {
                Entry entry = GetEntry(key);
                if (entry.Value is Style value)
                    Apply(value);
                if (entry.GetSkinnedRaw() is Dictionary<string, object> props)
                    foreach (KeyValuePair<string, object> prop in props)
                        Add(new(prop.Key), prop.Value);
            }
        }

        public Link GetLink(Key key)
            => new(this, key);
        public Link GetLink<T>()
            => new(this, GetCommonKey(typeof(T)));

        public void Revive() {
            foreach (Entry entry in Map.Values)
                if (entry.Value is IFader fader)
                    fader.Revive();
        }

        public static void UpdateTypeStyles(float dt) {
            foreach (Style style in TypeStyles) {
                style.Update(dt);
            }
        }

        public void Update(float dt) {
            bool invalidate = IsHierarchyInvalidated;

            foreach (Entry entry in Map.Values)
                if (entry.Value is IFader fader)
                    invalidate |= fader.Update(dt);

            if (invalidate)
                Invalidate();
        }

        private void Invalidate() {
            InvalidateID = UI.GlobalUpdateID;
            OnInvalidate?.Invoke();
            Element?.InvalidatePaint();
        }

        IEnumerator IEnumerable.GetEnumerator()
            => GetEnumerator();

        private uint getEnumIdCall;
        public IEnumerator<Entry> GetEnumerator() {
            // FIXME: Enumerator Cache-ing?
            getEnumIdCall++; // This will overflow and its fine

            foreach (KeyValuePair<Key, Entry> kvp in Map)
                if (!kvp.Value.IsUnset)
                    if (EnumeratorCache.TryGetValue(kvp.Key, out uint v)) {
                        if (v != getEnumIdCall) {
                            EnumeratorCache[kvp.Key] = getEnumIdCall;
                            yield return kvp.Value;
                        }
                    } else {
                        EnumeratorCache.Add(kvp.Key, getEnumIdCall);
                        yield return kvp.Value;
                    }

            if (Element is not null) {
                for (Style? parent = Parent; parent is not null; parent = parent.Parent)
                    foreach (KeyValuePair<Key, Entry> kvp in parent.Map)
                        if (!kvp.Value.IsUnset)
                            if (EnumeratorCache.TryGetValue(kvp.Key, out uint v)) {
                                if (v != getEnumIdCall) {
                                    EnumeratorCache[kvp.Key] = getEnumIdCall;
                                    yield return kvp.Value;
                                }
                            } else {
                                EnumeratorCache.Add(kvp.Key, getEnumIdCall);
                                yield return kvp.Value;
                            }
            }
        }

        public class Entry {

            public readonly Style Style;
            public readonly Key Key;
            public Entry? Parent;

            private object? _Value;
            private Link? _ValueAsLink;
            private IReloadable? _ValueAsReloadable;
            private IFader? _ValueAsFader;
            private ValueType _ValueType;

            private Skin.FaderStub? _SkinnedFader;
            private Skin.FaderStub? _SkinnedFaderPending;

            public object? Value {
                get => _Value;
                set {
                    if (value is null) {
                        ForceSetValue(null);
                        return;
                    }

                    if (_ValueAsFader != null) {
                        IFader faderOld = _ValueAsFader;
                        if (value is IFader faderOther) {
                            faderOld.Deserialize(faderOther.GetSerializedDuration(), faderOther.GetSerializedValue());
                        } else {
                            faderOld.SetValueTo(value);
                        }
                        // If we don't remove skinned faders here, they'll override our override again.
                        _SkinnedFader = null;
                        _SkinnedFaderPending = null;
                        return;
                    }

                    if (_SkinnedFader != null) {
                        Skin.FaderStub faderStub = _SkinnedFader;
                        _SkinnedFader = null;
                        _SkinnedFaderPending = null;
                        value = Fader.Create(value.GetType(), faderStub.Fade, value);
                    }

                    value = ForceSetValue(value);

                    if (value is IFader) {
                        _SkinnedFader = null;
                        _SkinnedFaderPending = null;
                    }
                }
            }

            private object? ForceSetValue(object? value) {
                if (_ValueAsLink is not null)
                    _ValueAsLink.Source.OnInvalidate -= Style.Invalidate;

                _ValueAsLink = null;
                _ValueAsReloadable = null;
                _ValueAsFader = null;
                IsUnset = false;
                IsInherited = false;

                if (value is null) {
                    _Value = value;
                    _ValueType = ValueType.Inherit;
                    IsInherited = true;
                    return value;
                }

                if (Style.Element is not null) {
                    for (Style? parent = Style.Parent; parent is not null; parent = parent.Parent) {
                        if (!parent.Map.TryGetValue(Key, out Entry? parentEntry))
                            continue;
                        if (parentEntry.Value is IFader fader) {
                            fader = fader.Clone();
                            if (fader.SetValueTo(value)) {
                                value = fader;
                                break;
                            }
                        }
                    }
                }

                _Value = value;

                if (value is Link link) {
                    _ValueType = ValueType.Link;
                    _ValueAsLink = link;
                    link.Source.OnInvalidate += Style.Invalidate;

                } else if (value is Delegate) {
                    _ValueType = ValueType.Func;

                } else if (value is IReloadable reloadable) {
                    _ValueType = ValueType.Reloadable;
                    _ValueAsReloadable = reloadable;

                } else if (value is IFader fader) {
                    _ValueType = ValueType.Fader;
                    _ValueAsFader = fader;

                } else {
                    _ValueType = ValueType.Raw;
                }

                return value;
            }

            public bool IsUnset = true;
            public bool IsInherited;

            public readonly bool IsDummy;

            public Entry(Style owner, Key key, Entry? parent) {
                IsDummy = false;
                Style = owner;
                Key = key;
                Parent = parent;
                Value = null;
            }

            public Entry(object value) {
                IsDummy = true;
            }

            public void CheckDummy() {
                if (IsDummy)
                    throw new InvalidOperationException("Dummy style entry!");
            }

            public void GetCurrent<T>(out T value)
                => value = GetCurrent<T>();
            public T GetCurrent<T>()
                => TryGetCurrent(out T? value) ? value : throw new Exception($"{(Style.Element is null ? "Instance style for" : "Static style for")} \"{Style.Type}\" has got null \"{Key}\"");

            public void GetReal<T>(out T value)
                => value = GetReal<T>();
            public T GetReal<T>()
                => TryGetReal(out T? value) ? value : throw new Exception($"{(Style.Element is null ? "Instance style for" : "Static style for")} \"{Style.Type}\" has got null \"{Key}\"");

            public object? GetSkinnedRaw() {
                CheckDummy();

                object? raw = default;

                if (Style.Type is Type type && Skin.Current is Skin skin) {
                    if (skin.TryGetValue(type.Name, Key.ToString(), out raw) && raw != default)
                        return raw;

                    // Links are pain. Someone please save me from this hell.
                    {
                        if (_ValueAsLink?.Source.GetEntry(Key).GetSkinnedRaw() is object got)
                            return got;
                    }

                    if (Style.Element is not null) {
                        foreach (string @class in Style.Element.Classes)
                            if (skin.TryGetValue(@class, Key.ToString(), out raw) && raw != default)
                                return raw;
                    }

                    {
                        if (Parent?.GetSkinnedRaw() is object got)
                            return got;
                    }
                }

                return raw;
            }

            private bool TryGetSkinnedAndPrepare<T>([NotNullWhen(true)] out T? value) {
                object? skinnedRaw = GetSkinnedRaw();
                if (skinnedRaw is Skin.FaderStub faderStub) {
                    if (Value is not IFader fader) {
                        _SkinnedFader = faderStub;
                        _SkinnedFaderPending = faderStub;
                        if (faderStub.Value is not null) {
                            ForceSetValue(Fader.Create(faderStub.Type ?? throw new Exception("Typeless fader stub"), faderStub.Fade, faderStub.Value));
                        } else if (TryGetReal(out T? valueReal)) {
                            ForceSetValue(Fader.Create(typeof(T), faderStub.Fade, valueReal));
                        }

                    } else if (_SkinnedFader is Skin.FaderStub faderStubOld ? faderStub != faderStubOld : IsUnset) {
                        _SkinnedFader = faderStub;
                        _SkinnedFaderPending = faderStub;
                        fader.Deserialize(faderStub.Fade, faderStub.Value);
                    }
                } else if (_SkinnedFader is not null && Value is IFader) {
                    _SkinnedFader = null;
                    _SkinnedFaderPending = null;
                    ForceSetValue(null);
                }

                if (skinnedRaw is T skinned) {
                    value = skinned;
                    return true;
                }

                value = default;
                return false;
            }

            public bool TryGetCurrent<T>([NotNullWhen(true)] out T? value) {
                CheckDummy();

                bool skinnedSet = TryGetSkinnedAndPrepare(out T? skinned);

                if (Style.Element is null && skinnedSet) {
                    value = skinned;
#pragma warning disable CS8762 // skinnedSet being true is enough proof of skinned being non-null.
                    return true;
#pragma warning restore CS8762
                }

                bool valueSet;
                switch (_ValueType) {
                    case ValueType.Raw:
                    default:
                        value = Convert<T>(Value!);
                        valueSet = true;
                        break;

                    case ValueType.Inherit:
                        value = default;
                        valueSet = false;
                        break;

                    case ValueType.Link:
                        valueSet = _ValueAsLink!.Source.TryGetCurrent(_ValueAsLink.Key, out value);
                        break;

                    case ValueType.Func:
                        value = (Value as Func<T>)!();
                        valueSet = value is not null;
                        break;

                    case ValueType.Reloadable:
                        value = _ValueAsReloadable!.GetValue<T>();
                        valueSet = value is not null;
                        break;

                    case ValueType.Fader:
                        value = _ValueAsFader!.GetValue<T>();
#pragma warning disable CS8762 // T cannot be nullable for faders.
                        // Skip SkinnedFadersPending check.
                        return true;
#pragma warning restore CS8762
                }

                if (valueSet && _SkinnedFaderPending is Skin.FaderStub faderStub) {
                    _SkinnedFaderPending = null;
#pragma warning disable CS8602 // valueSet being true is enough proof of value being non-null.
                    IFader fader = Fader.Create(value.GetType(), faderStub.Fade, value);
#pragma warning restore CS8602
                    Value = fader;
                }

#pragma warning disable CS8762 // valueSet being true is enough proof of value being non-null.
                if (valueSet)
                    return true;
#pragma warning restore CS8762

                if (skinnedSet) {
                    value = skinned;
#pragma warning disable CS8762 // skinnedSet being true is enough proof of skinned being non-null.
                    return true;
#pragma warning restore CS8762
                }

                if (Parent is not null)
                    return Parent.TryGetCurrent(out value);

                return false;
            }

            public bool TryGetReal<T>([NotNullWhen(true)] out T? value) {
                CheckDummy();

                switch (_ValueType) {
                    case ValueType.Raw:
                    default:
                        value = Convert<T>(Value!);
#pragma warning disable CS8762 // Convert<T> returns non-null.
                        return true;
#pragma warning restore CS8762

                    case ValueType.Inherit:
                        value = default;
                        break;

                    case ValueType.Link:
                        if (_ValueAsLink!.Source.TryGetReal(_ValueAsLink.Key, out value))
                            return true;
                        break;

                    case ValueType.Func:
                        value = (Value as Func<T>)!();
                        if (value is not null)
                            return true;
                        break;

                    case ValueType.Reloadable:
                        value = _ValueAsReloadable!.GetValue<T>();
                        if (value is not null)
                            return true;
                        break;

                    case ValueType.Fader:
                        value = _ValueAsFader!.GetValueTo<T>();
#pragma warning disable CS8762 // T cannot be nullable for faders.
                        return true;
#pragma warning restore CS8762
                }

                if (Parent is not null)
                    return Parent.TryGetReal(out value);

                return false;
            }

            private enum ValueType {
                Raw,
                Inherit,
                Link,
                Func,
                Reloadable,
                Fader,
            }

        }

        public class Link {
            public readonly Style Source;
            public readonly Key Key;
            public Link(Style source, Key key) {
                Source = source;
                Key = key;
            }
        }

        public interface IConverter {
            (Type From, Type To)[] Supported { get; }
            T Convert<T>(object raw);
        }

        public readonly struct Key {
            public readonly string? Value;
            public readonly int HashCode;
            public Key(string value) {
                Value = value;
                HashCode = Value.GetHashCode();
            }

            public override bool Equals([NotNullWhen(true)] object? obj)
                => obj is Key other && HashCode == other.HashCode && Value == other.Value;

            public override int GetHashCode()
                => HashCode;

            public override string ToString()
                => Value ?? "";
        }

        public readonly struct KeyOrValue<TValue> {
            internal readonly bool HasValue;

            private readonly Key _Key;
            internal readonly Key Key =>
                HasValue ? throw new Exception("KeyOrValue has got a value, shouldn't get key") :
                _Key;

            private readonly TValue? _Value;
            internal readonly TValue Value =>
                !HasValue ? throw new Exception("KeyOrValue has got a key, shouldn't get value") :
                _Value ?? throw new Exception("KeyOrValue with null value");

            public KeyOrValue(Key key) {
                HasValue = false;
                _Key = key;
                _Value = default;
            }

            public KeyOrValue(string key) {
                HasValue = false;
                _Key = new(key);
                _Value = default;
            }

            public KeyOrValue(TValue value) {
                HasValue = true;
                _Key = default;
                _Value = value;
            }

            public static implicit operator KeyOrValue<TValue>(Key key) => new(key);
            public static implicit operator KeyOrValue<TValue>(TValue value) => new(value);
        }

        public sealed class KeyEqualityComparer : EqualityComparer<Key> {
            public static readonly KeyEqualityComparer Instance = new();

            private KeyEqualityComparer() {
            }

            public override bool Equals(Key x, Key y)
                => x.HashCode == y.HashCode && x.Value == y.Value;

            public override int GetHashCode([DisallowNull] Key obj)
                => obj.HashCode;
        }

    }
}
