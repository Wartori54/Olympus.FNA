using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace OlympUI.MegaCanvas {
    public sealed class CanvasPool : IDisposable {

        public readonly CanvasManager Manager;
        public readonly GraphicsDevice Graphics;

        public bool MSAA;

        public readonly Entry[] Entries = new Entry[32];
        public int EntriesAlive = 0;
        public readonly HashSet<RenderTarget2D> Used = new();
        public long UsedMemory = 0;
        public long TotalMemory = 0;
        public int Padding = 16;
        public bool ForcePoT = false;
        private int SquishFrame = 0;
        public int SquishFrames = 60 * 3;
        public int MaxAgeFrames = 60 * 1;
        public int CullTarget = 16;
        public bool CullTriggered = false;

        public CanvasPool(CanvasManager manager, bool msaa) {
            Manager = manager;
            Graphics = manager.GraphicsDevice;
            MSAA = msaa;
        }

        public void Dispose() {
            for (int i = 0; i < Entries.Length; i++) {
                Entries[i].RT?.Dispose();
                Entries[i] = default;
            }
            EntriesAlive = 0;
            UsedMemory = 0;
            TotalMemory = 0;
            SquishFrame = 0;
        }

        public void Update() {
            lock (Entries) {
                if (SquishFrame++ >= SquishFrames || CullTriggered) {
                    CullTriggered = false;
                    for (int i = Entries.Length - 1; i >= 0; --i) {
                        ref Entry entry = ref Entries[i];
                        if (!entry.IsNull && (entry.IsDisposed || entry.Age++ >= MaxAgeFrames)) {
                            Dispose(ref entry);
                        }
                    }
                    for (int i = Entries.Length - 1; i >= CullTarget; --i) {
                        ref Entry entry = ref Entries[i];
                        if (!entry.IsNull) {
                            Dispose(ref entry);
                        }
                    }

                } else {
                    for (int i = Entries.Length - 1; i >= 0; --i) {
                        ref Entry entry = ref Entries[i];
                        if (!entry.IsDisposed && entry.Age++ >= MaxAgeFrames) {
                            Dispose(ref entry);
                        }
                    }
                }
            }
        }

        private void Dispose(ref Entry entry) {
            entry.RT?.Dispose();
            TotalMemory -= entry.RT?.GetMemorySizePoT() ?? 0;
            entry = default;
            EntriesAlive--;
        }

        public RenderTarget2DRegion? Get(int width, int height) {
            if (width < Manager.MinSize || height < Manager.MinSize ||
                width > Manager.MaxSize || height > Manager.MaxSize) // TODO: This causes a crash when window is too big
                return null;

            RenderTarget2D? rt = null;
            bool fresh;

            lock (Entries) {
                if (Entries.TryGetSmallest(width, height, out Entry entry, out int index) &&
                    entry.Width <= width * 1.4 && entry.Height <= height * 1.4) {
                    Entries[index] = default;
                    EntriesAlive--;
                    rt = entry.RT;
                }
            }

            if (rt is null) {
                int widthReal = Math.Min(Manager.MaxSize, (int) MathF.Ceiling(width / Padding + 1) * Padding);
                int heightReal = Math.Min(Manager.MaxSize, (int) MathF.Ceiling(height / Padding + 1) * Padding);
                if (ForcePoT) {
                    widthReal = widthReal.NextPoT();
                    heightReal = heightReal.NextPoT();
                }
                rt = new(Graphics, widthReal, heightReal, false, SurfaceFormat.Color, DepthFormat.None, MSAA ? Manager.MultiSampleCount : 0, RenderTargetUsage.PlatformContents);
                fresh = true;

            } else {
                fresh = false;
            }

            lock (Entries) {
                Used.Add(rt);
                UsedMemory += rt.GetMemorySizePoT();
                if (fresh)
                    TotalMemory += rt.GetMemorySizePoT();
            }

            return new(this, rt, new(0, 0, rt.Width, rt.Height), new(0, 0, width, height));
        }

        public void Free(RenderTarget2D? rt) {
            if (rt is null)
                return;
            lock (Entries) {
                if (!Used.Remove(rt))
                    throw new Exception("Trying to free a RenderTarget2D in the wrong pool / double-free?");

                UsedMemory -= rt.GetMemorySizePoT();

                if (rt.IsDisposed)
                    return;

                if (CullTriggered) {
                    rt.Dispose();
                    TotalMemory -= rt.GetMemorySizePoT();

                } else if (EntriesAlive >= Entries.Length) {
                    CullTriggered = true;
                    rt.Dispose();
                    TotalMemory -= rt.GetMemorySizePoT();

                } else {
                    for (int i = 0; i < Entries.Length; i++) {
                        if (Entries[i].IsNull) {
                            Entries[i] = new(rt);
                            EntriesAlive++;
                            return;
                        }
                    }
                    // This shouldn't ever be reached but eh.
#if DEBUG
                    throw new Exception("This shouldn't ever be reached.");
#else
                    CullTriggered = true;
                    rt.Dispose();
                    TotalMemory -= rt.GetMemorySizePoT();
#endif
                }
            }
        }

        public struct Entry : ISizeable {

            public RenderTarget2D? RT;

            public int Width { get; set; }
            public int Height { get; set; }

            public int Age;

            [MemberNotNullWhen(false, nameof(RT))]
            public bool IsDisposed => RT?.IsDisposed ?? true;
            [MemberNotNullWhen(false, nameof(RT))]
            public bool IsNull => RT is null;

            public Entry(RenderTarget2D rt) {
                RT = rt;
                Width = rt.Width;
                Height = rt.Height;
                Age = 0;
            }

        }

    }
}
