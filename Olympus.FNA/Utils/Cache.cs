using System;
using System.Diagnostics;
using System.Threading;

namespace Olympus.Utils {

    public abstract class Cache<T> {
        protected T InternalCache;

        protected Func<object?, T> generator;

        protected readonly object? Sender;

        private readonly Mutex mut = new();

        protected Cache(Func<object?, T> generator, object? sender) {
            this.generator = generator;
            Sender = sender;
        }

        public T Value {
            get {
                mut.WaitOne();
                if (!IsValid() && !Frozen) {
                    try {
                        Regenerate();
                    } catch (Exception ex) {
                        AppLogger.Log.Error(ex, "Failed regenerating cache!");
                    }
                }

                mut.ReleaseMutex();

                return InternalCache;
            }
        }

        public bool Frozen { get; set; }

        public abstract bool IsValid();

        public abstract void Regenerate();
    }

    public class ManualCache<T> : Cache<T> {

        public bool Valid { get; protected set; }

        public ManualCache(Func<object?, T> gen, object? sender) : base (gen, sender) {
            Valid = false;
        }

        public override bool IsValid() {
            return Valid;
        }

        public override void Regenerate() {
            InternalCache = generator(Sender);
            if (InternalCache == null) AppLogger.Log.Information("Null cache at " + new StackTrace());
            if (InternalCache != null || Nullable.GetUnderlyingType(typeof(T)) != null) {
                Valid = true;
            } else {
                AppLogger.Log.Warning("Regenerating cache returned null!");
            }
        }

        public void Invalidate() {
            Valid = false;
        }
    }
    
    public class TimedCache<T> : ManualCache<T> {
    
        private DateTime lastCache = DateTime.MinValue;
        public readonly TimeSpan RefreshInterval;

        public TimedCache(TimeSpan refreshInterval, Func<object?, T> gen, object? sender) : base (gen, sender) {
            RefreshInterval = refreshInterval;
        }

        public override bool IsValid() {
            return Valid && DateTime.Now - lastCache < RefreshInterval;
        }

        public override void Regenerate() {
            base.Regenerate();
            lastCache = DateTime.Now;
        }
    }
}
