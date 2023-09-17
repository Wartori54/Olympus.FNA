using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OlympUI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;

namespace Olympus {
    public class ShaderWarmupComponent : AppComponent {

        public Stopwatch Timer = new();

        public List<MiniEffectCache> Caches = new();
        public int CacheIndex;

        public object?[]? Keys;
        public int KeyIndex;

        public VertexPositionColorTexture[] Vertices = new VertexPositionColorTexture[3];
        public short[] Indices = new short[3];

        public bool Done;

        public ShaderWarmupComponent(App app)
            : base(app) {

            Vertices[0] = new(Vector3.Zero, Color.Red, Vector2.Zero);
            Vertices[1] = new(Vector3.Right, Color.Green, Vector2.UnitX);
            Vertices[2] = new(Vector3.One, Color.Blue, Vector2.One);

            DrawOrder = -1000001;
        }

        public override void Initialize() {
            base.Initialize();

            foreach (Type type in UIReflection.GetAllTypes(typeof(MiniEffect))) {
                if (type.GetField(nameof(MiniEffect.Cache), BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly) is not FieldInfo field ||
                    field.GetValue(null) is not MiniEffectCache cache)
                    continue;

                Caches.Add(cache);

                foreach (object? key in cache.GetWarmupKeys())
                    cache.GetData(key);
            }

            App.Get<SplashComponent>().Locks.Add(this);
        }

        public override bool UpdateDraw() {
            return true;
        }

        public override void Draw(GameTime gameTime) {
            if (CacheIndex >= Caches.Count) {
                Finish();
                return;
            }

            long timeEnd = Timer.Elapsed.Ticks + 16 * TimeSpan.TicksPerMillisecond;

            do {
                MiniEffectCache cache;

                if (CacheIndex == 0 && KeyIndex == 0 && Keys is null) {
                    Timer.Start();

                    cache = Caches[0];
                    Keys = cache.GetWarmupKeys();

                } else if (KeyIndex >= Keys!.Length) {
                    CacheIndex++;
                    KeyIndex = 0;
                    Keys = null;

                    if (CacheIndex >= Caches.Count) {
                        Finish();
                        return;
                    }

                    cache = Caches[CacheIndex];
                    Keys = cache.GetWarmupKeys();

                } else {
                    cache = Caches[CacheIndex];
                }

                object? key = Keys![KeyIndex++];

                AppLogger.Log.Information($"Shader warmup: {cache.Name} {key}");

                GraphicsDevice gd = GraphicsDevice;
                MiniEffect effect = cache.GetEffect(() => gd, key).Value;

                foreach (EffectPass pass in effect.CurrentTechnique.Passes) {
                    pass.Apply();
                    gd.DrawUserIndexedPrimitives(
                        PrimitiveType.TriangleList,
                        Vertices, 0, Vertices.Length,
                        Indices, 0, Indices.Length
                    );
                }

            } while (Timer.Elapsed.Ticks < timeEnd);
        }

        private void Finish() {
            if (Done)
                return;

            Timer.Stop();
            AppLogger.Log.Information($"Shaders warmed up in: {Timer.Elapsed}");

            Done = true;
            App.Components.Remove(this);
            App.Get<SplashComponent>().Locks.Remove(this);
        }

    }
}
