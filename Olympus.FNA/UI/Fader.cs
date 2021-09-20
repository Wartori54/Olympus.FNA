﻿#define FASTGET

using Microsoft.Xna.Framework;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace OlympUI {
    public sealed class Faders : IEnumerable<(string, IFader)> {

        private List<Action<Faders>> Links = new();
        private List<(string, IFader)> List = new();
        private Dictionary<string, IFader> Map = new();

        public void Link(Action<Faders> cb)
            => Links.Add(cb);

        public void LinkInvalidatePaint(Element element)
            => Links.Add(_ => element.InvalidatePaint());

        public void LinkSetStyle(Element element, string key)
            => Links.Add(faders => {
                foreach ((string Key, IFader Fader) entry in faders.List)
                    entry.Fader.SetStyle(element.Style, entry.Key);
                element.InvalidatePaint();
            });

        private void UpdateLinks() {
            foreach (Action<Faders> link in Links)
                link(this);
        }

        public void Add(string key, IFader fader) {
            List.Add((key, fader));
            Map.Add(key, fader);
        }

        public bool Update(float dt, Style style) {
            bool updated = false;
            foreach ((string Key, IFader Fader) entry in List) {
                bool subupdated = entry.Fader.Update(dt, style, entry.Key);
                updated |= subupdated;
            }
            if (updated)
                UpdateLinks();
            return updated;
        }

        public T Get<T>(string key) where T : struct
            => ((Fader<T>) Map[key]).Value;

        public void Get<T>(string key, out T value) where T : struct
            => value = ((Fader<T>) Map[key]).Value;

        IEnumerator IEnumerable.GetEnumerator()
            => GetEnumerator();

        public IEnumerator<(string, IFader)> GetEnumerator()
            => List.GetEnumerator();

    }

    public interface IFader {

        T GetValue<T>();

        T GetValueTo<T>();

        bool SetValueTo(object value);

        void Link(Action<IFader> cb);

        void LinkInvalidatePaint(Element element);

        void LinkSetStyle(Element element, string key);

        void SetStyle(Style style, string key);

        bool Update(float dt);

        bool Update(float dt, Style style);

        bool Update(float dt, Style style, string key);

        IFader New();

    }

    public abstract class Fader<T> : IFader where T : struct {

        private List<Action<Fader<T>>> Links = new();

        public T Value;
        public T ValueFrom;
        public T ValueTo;

        public float Time = -1f;
        public float Duration = 0.15f;

        private T ValueFromPrev;
        private T ValueToPrev;
        private float TPrev;

        public Fader(T value = default) {
            Value = ValueFrom = ValueTo = value;
        }

        public abstract T Calculate(T a, T b, float t);
        protected abstract bool Equal(T a, T b);
        public abstract Fader<T> New();

        TGet IFader.GetValue<TGet>()
#if FASTGET
            => Unsafe.As<T, TGet>(ref Value);
#else
            => (TGet) (object) Value;
#endif

        TGet IFader.GetValueTo<TGet>()
#if FASTGET
            => Unsafe.As<T, TGet>(ref ValueTo);
#else
            => (TGet) (object) ValueTo;
#endif

        bool IFader.SetValueTo(object value) {
            if (value is T raw) {
                ValueTo = raw;
                return true;
            }
            return false;
        }

        public void Link(Action<Fader<T>> cb)
            => Links.Add(cb);

        public void Link(Action<IFader> cb)
            => Links.Add(fader => cb(fader));

        public void LinkInvalidatePaint(Element element)
            => Links.Add(_ => element.InvalidatePaint());

        public void LinkSetStyle(Element element, string key)
            => Links.Add(fader => {
                element.Style.Add(key, fader.Value);
                element.InvalidatePaint();
            });

        private void UpdateLinks() {
            foreach (Action<Fader<T>> link in Links)
                link(this);
        }

        public void SetStyle(Style style, string key) {
            style.Add(key, Value);
        }

        public bool Update(float dt) {
            if (Time < 0f) {
                Value = ValueFrom = ValueTo;
                Time = Duration;
                UpdateLinks();
                return true;
            }

            bool force = !Equal(ValueToPrev, ValueTo);
            if (force) {
                if (Equal(ValueFromPrev, ValueTo) && Equal(ValueToPrev, ValueFrom)) {
                    (ValueFromPrev, ValueToPrev) = (ValueToPrev, ValueFromPrev);
                    ValueFrom = Value;
                    Time = Duration - TPrev * Duration;
                } else {
                    ValueFromPrev = ValueFrom = Value;
                    ValueToPrev = ValueTo;
                    Time = 0f;
                }

            } else {
                Time += dt;
                if (Time > Duration)
                    Time = Duration;
            }

            if (!force && Time >= Duration)
                return false;

            float t = 1f - Time / Duration;
            TPrev = t = 1f - t * t;
            T next = Calculate(ValueFrom, ValueTo, t);
            bool changed = !Equal(Value, next);
            Value = next;
            UpdateLinks();
            return changed;
        }

        public bool Update(float dt, Style style) {
            ValueTo = style.GetReal<T>();
            return Update(dt);
        }

        public bool Update(float dt, Style style, string key) {
            ValueTo = style.GetReal<T>(key);
            return Update(dt);
        }

        IFader IFader.New()
            => New();

    }

    public sealed class FloatFader : Fader<float> {

        public FloatFader(float value = default)
            : base(value) {
        }

        public override float Calculate(float a, float b, float t)
            => a + (b - a) * t;

        protected override bool Equal(float a, float b)
            => a == b;

        public override Fader<float> New()
            => new FloatFader(ValueTo);

    }

    public sealed class ColorFader : Fader<Color> {

        public ColorFader(Color value = default)
            : base(value) {
        }

        public override Color Calculate(Color a, Color b, float t)
            => new(
                (byte) (a.R + (b.R - a.R) * t),
                (byte) (a.G + (b.G - a.G) * t),
                (byte) (a.B + (b.B - a.B) * t),
                (byte) (a.A + (b.A - a.A) * t)
            );

        protected override bool Equal(Color a, Color b)
            => a == b;

        public override Fader<Color> New()
            => new ColorFader(ValueTo);

    }
}
