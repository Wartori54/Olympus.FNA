#define FASTGET

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace OlympUI {
    public static class Reloadable {

        internal static uint NextGlobalID;

        public static IReloadableTemporaryContext? TemporaryContext;

        public static IReloadable<TValue, TMeta> Temporary<TValue, TMeta>(TMeta meta, Func<TValue?> loader, bool owns) where TMeta : struct {
            Reloadable<TValue, TMeta> reloadable = new(null, meta, loader, owns ? value => (value as IDisposable)?.Dispose() : null);

#if DEBUG
            if (TemporaryContext is null && Debugger.IsAttached) {
                Debugger.Break();
            }
#endif

            return owns ? TemporaryContext?.MarkTemporary(reloadable) ?? reloadable : reloadable;
        }

        public static IReloadable<TValue, TMeta> Temporary<TValue, TMeta>(TMeta meta, Func<TValue?> loader, Action<TValue?> unloader) where TMeta : struct {
            Reloadable<TValue, TMeta> reloadable = new(null, meta, loader, unloader);

#if DEBUG
            if (TemporaryContext is null && Debugger.IsAttached) {
                Debugger.Break();
            }
#endif

            return TemporaryContext?.MarkTemporary(reloadable) ?? reloadable;
        }
        
        public static IReloadable<TValue, TMeta> Permanent<TValue, TMeta>(TMeta meta, Func<TValue?> loader, Action<TValue?> unloader) where TMeta : struct {
            Reloadable<TValue, TMeta> reloadable = new(null, meta, loader, unloader);

#if DEBUG
            if (TemporaryContext is null && Debugger.IsAttached) {
                Debugger.Break();
            }
#endif

            return reloadable;
        }
        

    }

    public interface IReloadable : IGenericValueSource, IDisposable {

        bool IsValid { get; }

        bool LifeTick();
        void LifeBump();

    }

    public interface IReloadable<TValue> : IReloadable {
        /// <summary>
        /// Returns a valid value for this reloadable, or default if none is.
        /// </summary>
        TValue? ValueValid { get; }
        /// <summary>
        /// Lazily loads a value for this reloadable. Expensiveness warning.
        /// </summary>
        TValue? ValueLazy { get; }
        /// <summary>
        /// Returns the value stored in this reloadable, can throw if not valid.
        /// </summary>
        TValue Value { get; }

    }

    public interface IReloadable<TValue, TMeta> : IReloadable<TValue> where TMeta : struct {

        TMeta Meta { get; set; }

    }

    public interface IReloadableMeta<TValue> {

        bool IsValid(TValue? value);

    }

    public interface IReloadableTemporaryContext {

        IReloadable<TValue, TMeta> MarkTemporary<TValue, TMeta>(IReloadable<TValue, TMeta> reloadable) where TMeta : struct;
        IReloadable<TValue, TMeta> UnmarkTemporary<TValue, TMeta>(IReloadable<TValue, TMeta> reloadable) where TMeta : struct;

    }

    public sealed class Reloadable<TValue, TMeta> : IReloadable<TValue, TMeta> where TMeta : struct {

        public readonly uint GlobalID;
        public readonly string ID;
        public readonly Func<TValue?> Loader;
        public readonly Action<TValue?>? Unloader;
        public ulong ReloadID;
        public TValue? ValueRaw;

#if DEBUG
        public readonly StackTrace Source;
#endif

        private bool _IsValid;
        public bool IsValid {
            [MemberNotNullWhen(true, nameof(ValueRaw), nameof(ValueValid), nameof(ValueLazy))]
            get => _IsValid && (_MetaTyped?.IsValid(ValueRaw) ?? true);
            set => _IsValid = value;
        }

        public int Lifespan { get; set; }

        public TValue? ValueValid {
            get {
                LifeBump();
                return IsValid ? ValueRaw : default;
            }
        }

        public TValue? ValueLazy {
            get {
                Reload();
                return ValueRaw;
            }
        }

        public TValue Value => ValueLazy ?? throw new Exception($"Failed loading: {(string.IsNullOrEmpty(ID) ? "<temporary pseudo-reloadable resource>" : ID)}");

        TGet IGenericValueSource.GetValue<TGet>() {
            TValue value = Value;
#if FASTGET
            return Unsafe.As<TValue, TGet>(ref value);
#else
            return (TGet) (object) value;
#endif
        }

        private TMeta _Meta;
        private IReloadableMeta<TValue>? _MetaTyped;
        public TMeta Meta {
            get => _Meta;
            [MemberNotNull(nameof(_Meta))]
            set {
                _Meta = value;
                _MetaTyped = value as IReloadableMeta<TValue>;

                AssetTracker<TValue, TMeta>.Instance?.MetaUpdated(this, _Meta, value);
            }
        }

        public Reloadable(string? id, TMeta meta, Func<TValue?> loader, Action<TValue?>? unloader = null) {
#if DEBUG
            Source = new StackTrace(1);
#endif

            GlobalID = Reloadable.NextGlobalID++;

            ID = id ?? "";
            Loader = loader;
            Unloader = unloader;
            _Meta = meta;
            _MetaTyped = meta as IReloadableMeta<TValue>;

            AssetTracker<TValue, TMeta>.Instance?.Created(this);
        }

        ~Reloadable() {
            AssetTracker<TValue, TMeta>.Instance?.Collected(this);
        }

        public void Dispose() {
            if (!IsValid)
                return;

            if (Unloader is not null) {
                Unloader(ValueRaw);
            } else if (!string.IsNullOrEmpty(ID) && ValueRaw is IDisposable disposable) {
                disposable.Dispose();
            }

            ValueRaw = default;
            IsValid = false;

            AssetTracker<TValue, TMeta>.Instance?.Unloaded(this);
        }

        public void Reload(bool force = false) {
            LifeBump();

            if (ReloadID != Assets.ReloadID) {
                ReloadID = Assets.ReloadID;
                force = true;
            }

            if (IsValid) {
                if (!force)
                    return;
                Dispose();
            }

            ValueRaw = Loader();
            IsValid = ValueRaw is not null;

            if (!IsValid)
                return;

            AssetTracker<TValue, TMeta>.Instance?.Loaded(this);
        }

        public bool LifeTick() {
            if (Lifespan == 0)
                return false;
            Lifespan--;
            return true;
        }

        public void LifeBump() {
            Lifespan = 32;
        }

    }

    public class ReloadableAs<TValue, TAsValue, TMeta> : IReloadable<TAsValue, TMeta> where TMeta : struct {

        public readonly IReloadable<TValue, TMeta> Reloadable;

        [MemberNotNullWhen(true, nameof(ValueValid), nameof(ValueLazy))]
        public bool IsValid => Reloadable.IsValid;

        public TAsValue? ValueValid {
            get {
                TValue? value = Reloadable.ValueValid;
                if (value is null)
                    return default;
#if FASTGET
                return Unsafe.As<TValue, TAsValue>(ref value);
#else
                return (TValue) (object) value;
#endif
            }
        }

        public TAsValue? ValueLazy {
            get {
                TValue? value = Reloadable.ValueLazy;
                if (value is null)
                    return default;
#if FASTGET
                return Unsafe.As<TValue, TAsValue>(ref value);
#else
                return (TValue) (object) value;
#endif
            }
        }

        public TAsValue Value {
            get {
                TValue value = Reloadable.Value;
#if FASTGET
                return Unsafe.As<TValue, TAsValue>(ref value);
#else
                return (TValue) (object) value;
#endif
            }
        }

        TGet IGenericValueSource.GetValue<TGet>() {
            TValue value = Reloadable.Value;
#if FASTGET
            return Unsafe.As<TValue, TGet>(ref value);
#else
            return (TGet) (object) value;
#endif
        }

        public TMeta Meta {
            get => Reloadable.Meta;
            set => Reloadable.Meta = value;
        }

        public ReloadableAs(IReloadable<TValue, TMeta> reloadable) {
            Reloadable = reloadable;
        }

        public void Dispose()
            => Reloadable.Dispose();

        public void LifeBump()
            => Reloadable.LifeBump();

        public bool LifeTick()
            => Reloadable.LifeTick();

    }

    public class ReloadableLink<TValue, TMeta, TAsValue, TAsMeta> : IReloadable<TAsValue, TAsMeta> where TMeta : struct where TAsMeta : struct {

        public readonly IReloadable<TValue, TMeta> Reloadable;
        public readonly Func<TMeta, TAsMeta> MetaConverter;
        public readonly Func<TValue, TAsValue> ValueConverter;
        public readonly Action<IReloadable<TValue, TMeta>> Unloader;

        [MemberNotNullWhen(true, nameof(ValueValid), nameof(ValueLazy))]
        public bool IsValid => Reloadable.IsValid;

        public TAsValue? ValueValid {
            get {
                TValue? value = Reloadable.ValueValid;
                if (value is null)
                    return default;
                return ValueConverter(value);
            }
        }

        public TAsValue? ValueLazy {
            get {
                TValue? value = Reloadable.ValueLazy;
                if (value is null)
                    return default;
                return ValueConverter(value);
            }
        }

        public TAsValue Value => ValueConverter(Reloadable.Value);

        TGet IGenericValueSource.GetValue<TGet>() {
            TAsValue value = ValueConverter(Reloadable.Value);
#if FASTGET
            return Unsafe.As<TAsValue, TGet>(ref value);
#else
            return (TGet) (object) value;
#endif
        }

        public TAsMeta Meta {
            get => MetaConverter(Reloadable.Meta);
            set => throw new NotSupportedException();
        }

        public ReloadableLink(IReloadable<TValue, TMeta> reloadable, Func<TMeta, TAsMeta> meta, Func<TValue, TAsValue> value, Action<IReloadable<TValue, TMeta>> unloader) {
            Reloadable = reloadable;
            MetaConverter = meta;
            ValueConverter = value;
            Unloader = unloader;
        }

        public void Dispose()
            => Unloader(Reloadable);

        public void LifeBump()
            => Reloadable.LifeBump();

        public bool LifeTick()
            => Reloadable.LifeTick();

    }

    public class ReloadableSlot<TValue, TMeta> : IReloadable<TValue, TMeta> where TMeta : struct {

        private TValue? _Value;

        [MemberNotNullWhen(true, nameof(_Value), nameof(ValueValid), nameof(ValueLazy))]
        public bool IsValid => _Value is not null;

        public int Lifespan => 0;

        public TValue? ValueValid {
            get => _Value;
            set => _Value = value;
        }

        public TValue? ValueLazy {
            get => _Value;
            set => _Value = value;
        }

        [MemberNotNull(nameof(_Value), nameof(ValueValid), nameof(ValueLazy))]
        public TValue Value {
#pragma warning disable CS8774 // Member must have a non-null value when exiting.
            get => _Value ?? throw new Exception("ReloadableSlot without a value");
            set => _Value = value;
#pragma warning restore CS8774 // Member must have a non-null value when exiting.
        }

        TGet IGenericValueSource.GetValue<TGet>() {
            TValue value = Value;
#if FASTGET
            return Unsafe.As<TValue, TGet>(ref value);
#else
            return (TGet) (object) value;
#endif
        }

        public TMeta Meta { get; set; }

        public ReloadableSlot() {
        }

        public void Dispose() {
        }

        public void Reload(bool force = false) {
        }

        public bool LifeTick() {
            return false;
        }

        public void LifeBump() {
        }

        public void Set(TMeta meta, TValue value) {
            Meta = meta;
            Value = value;
        }

    }


    public struct NullMeta {
    }

    public struct Texture2DMeta : IReloadableMeta<Texture2D> {

        public readonly int Width;
        public readonly int Height;
        public readonly Func<Color[]?>? GetData;

        public long MemorySize => Width * Height * 4L;
        public long MemorySizePoT => Width.NextPoT() * Height.NextPoT() * 4L;
        public long MemoryWaste => MemorySizePoT - MemorySize;

        public Texture2DMeta(int width, int height, Func<Color[]?>? getData) {
            Width = width;
            Height = height;
            GetData = getData;
        }

        public Texture2DMeta(Texture2D texture, Func<Color[]?>? getData) {
            Width = texture.Width;
            Height = texture.Height;
            GetData = getData;
        }

        public bool IsValid(Texture2D? value) => value is not null && !value.IsDisposed;

        public static IReloadable<Texture2D, Texture2DMeta> Reloadable(string? id, int width, int height, Func<Color[]?>? getData)
            => new Reloadable<Texture2D, Texture2DMeta>(id, new Texture2DMeta(width, height, getData), () => {
                Color[] data = getData?.Invoke() ?? throw new InvalidOperationException("Trying to load a Texture2D from a Texture2DMeta without GetData!");
                unsafe {
                    fixed (Color* ptr = data)
                        return Assets.OpenTextureDataUnmipped((IntPtr) ptr, width, height, data.Length);
                }
            }, tex => tex?.Dispose());

    }

    public static class Texture2DReloadableExtensions {

        public static Color[] GetData(this IReloadable<Texture2D, Texture2DMeta> reloadable) {
            if (reloadable.Meta.GetData?.Invoke() is { } data)
                return data;

            Texture2D texture = reloadable.Value;
            data = new Color[texture.Width * texture.Height];
            texture.GetData(data);
            return data;
        }

    }
}
