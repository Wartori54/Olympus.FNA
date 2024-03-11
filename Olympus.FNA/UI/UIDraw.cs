using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OlympUI.MegaCanvas;
using System;
using System.Collections.Generic;

namespace OlympUI {
    public static class UIDraw {

        private static RecorderEntry Root = new() {
            TransformOffset = new(0f, 0f)
        };
        private static RecorderEntry Current = Root;
        private static Stack<RecorderEntry> Stack = new();
        private static Stack<Vector2> TransformOffsets = new();
        private static Dictionary<uint, RecorderEntry> Recorders = new() {
            { 0, Root }
        };
        private static uint DrawID;

        public static Recorder Recorder => Current.Recorder;

        public static void Push(RenderTarget2DRegion rt, Vector2? transformOffset) {
            Current.Dependencies.Add(rt.UniqueID);
            Stack.Push(Current);

            if (!Recorders.TryGetValue(rt.UniqueID, out RecorderEntry? entry))
                Recorders[rt.UniqueID] = entry = new(Current, rt);

            if (entry.IsDirty)
                throw new Exception("Pushing a new draw target that is still dirty");

            entry.Lifetime = RecorderEntry.LifetimeMax;
            entry.TransformOffset = transformOffset;
            entry.IsDirty = true;

            Current = entry;

            TransformOffsets.Push(UI.TransformOffset);
            if (transformOffset is { } transformOffsetValue)
                UI.TransformOffset = transformOffsetValue;
        }

        public static void Pop() {
            Current = Stack.Pop();
            UI.TransformOffset = TransformOffsets.Pop();
        }

        public static void Update() {
            Root.IsDirty = true;
            Root.Lifetime = RecorderEntry.LifetimeMax;

            // Fun fact: Modern .NET allows iterating through a dictionary while removing items from it!
            foreach ((uint id, RecorderEntry entry) in Recorders) {
                entry.Dependencies.Clear();
                if (--entry.Lifetime <= 0)
                    Recorders.Remove(id);
            }
        }

        public static void Draw() {
            // Required because this can be called while a render target is active.
            GraphicsStateSnapshot gss = new(UI.Game.GraphicsDevice);
            Recorder.Add(gss, static gss => gss.Apply());

            DrawID++;
            DrawEntry(Root);

            gss.Apply();
        }

        public static void AddDependency(RenderTarget2DRegion rt) {
            Current.Dependencies.Add(rt.UniqueID);
        }
        
        public static List<uint> GetDeps() => Current.Dependencies;

        private static void DrawEntry(RecorderEntry entry) {
            if (entry.DrawID == DrawID)
                return;

            entry.DrawID = DrawID;

            foreach (uint depID in entry.Dependencies)
                if (Recorders.TryGetValue(depID, out RecorderEntry? dep))
                    DrawEntry(dep);

            if (!entry.IsDirty)
                return;

            entry.IsDirty = false;

            if (entry.TransformOffset is { } transformOffset) {
                UI.TransformOffset = transformOffset;

                GraphicsDevice gd = UI.Game.GraphicsDevice;

                if (entry.RT is { } rt) {
                    rt.RT.SetRenderTargetUsage(RenderTargetUsage.PlatformContents);
                    gd.SetRenderTarget(rt.RT);
                    gd.Clear(ClearOptions.Target, new Vector4(0, 0, 0, 0), 0, 0);
                    rt.RT.SetRenderTargetUsage(RenderTargetUsage.PreserveContents);

                } else {
                    gd.SetRenderTarget(null);
                }

                UI.SpriteBatch.BeginUI();

                entry.Recorder.Run();
                entry.Recorder.Clear();

                UI.SpriteBatch.End();

            } else {
                entry.Recorder.Run();
                entry.Recorder.Clear();
            }
        }

        private sealed class RecorderEntry {
            public const int LifetimeMax = 8;
            public readonly Recorder Recorder;
            public List<uint> Dependencies = new();
            public int Lifetime;
            public uint DrawID;
            public Vector2? TransformOffset;
            public RenderTarget2DRegion? RT;
            public bool IsDirty;

            public RecorderEntry() {
                Recorder = new();
            }

            public RecorderEntry(RecorderEntry shared, RenderTarget2DRegion rt) {
                Recorder = new();
                RT = rt;
            }
        }

    }
}
