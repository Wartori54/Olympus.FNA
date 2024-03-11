using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;

namespace OlympUI.MegaCanvas {

    /// <summary>
    /// This class is basically a pool to recycle RenderTarget2Ds, and holding all the created ones for safety
    /// It creates all the RenderTarget2D instances and when it frees them it may store them for recycling
    /// Each recycle candidate has a life time, that if its not been sent by that time it'll be deleted
    /// </summary>
    public class CanvasPool : IDisposable {
        public readonly CanvasManager Manager;
        private const int PreCreatedRegions = 32;
        private const double LifeTime = 3; // Seconds
        private const double LifeTimeCheckDelay = 1; // Seconds
        private readonly RenderTarget2DWrapper[] regions = new RenderTarget2DWrapper[PreCreatedRegions];
        private readonly HashSet<RenderTarget2D> allInUse = new();
        private readonly bool MSAA;
        public int RegionsUsed { get; private set; }
        public int UsedCount => allInUse.Count;

        public long UsedMemory { get; private set; } = 0;
        public long TotalMemory { get; private set; } = 0;
        
        private DateTime lastLifeTimeCheck = DateTime.Now;

        public RenderTarget2DWrapper[] Entries => regions; // TODO: remove this lmao
        public HashSet<RenderTarget2D> Used => allInUse;

        public CanvasPool(CanvasManager manager, bool msaa) {
            Manager = manager;
            MSAA = msaa;
            for (int i = 0; i < PreCreatedRegions; i++) {
                regions[i] = new RenderTarget2DWrapper();
            }
        }

        public void Update() {
            if (lastLifeTimeCheck.Add(TimeSpan.FromSeconds(LifeTimeCheckDelay)).CompareTo(DateTime.Now) > 0) return;
            lock (regions) {
                for (int i = 0; i < regions.Length; i++) {
                    if (!regions[i].IsDummy && regions[i].LifeCheck()) {
                        regions[i].Dispose();
                        TotalMemory -= regions[i].RT?.GetMemorySizePoT() ?? 0;
                        regions[i] = new RenderTarget2DWrapper();
                        RegionsUsed--;
                    }
                }
            }
            
        }

        public void Dispose() {
            lock (regions) {
                for (int i = 0; i < regions.Length; i++) {
                    if (!regions[i].IsDummy) {
                        regions[i].Dispose();
                        TotalMemory -= regions[i].RT?.GetMemorySizePoT() ?? 0;
                        regions[i] = new RenderTarget2DWrapper();
                    }
                }
            }

            UsedMemory = 0;
            TotalMemory = 0;
        }

        /// <summary>
        /// Gets a region from either the cached ones or a new one
        /// </summary>
        /// <param name="width">The width target</param>
        /// <param name="height">The height target</param>
        /// <returns>The ready to use region</returns>
        public RenderTarget2DRegion? Get(int width, int height) {
            if (width < Manager.MinSize || 
                height < Manager.MinSize ||
                width > Manager.MaxSize ||
                height > Manager.MaxSize) { // TODO: This causes a crash when window is too big
                Console.WriteLine($"CanvasPool: asked for region that did not fulfill minimum or maximum size: {width}, {height}");
                return null; // TODO: maybe return a special region for when its huge?
            }

            RenderTarget2D rt;

            const double tooBigThreshold = 1.5;
            lock (regions) {
                // Try to reuse one, make sure its not too big
                if (regions.TryGetSmallest(width, height, out RenderTarget2DWrapper? best, out int idx) &&
                    (best.Width > width * tooBigThreshold || best.Height > height * tooBigThreshold)) {
                    regions[idx] = new RenderTarget2DWrapper(); // Replace with a new one
                    RegionsUsed--;
                    rt = best.RT!; // If its not disposed it cannot be null
                } else { // Otherwise just create a new one
                    int Padding = 16;
                    int widthReal = Math.Min(Manager.MaxSize, (int) MathF.Ceiling((float) width / Padding + 1) * Padding);
                    int heightReal = Math.Min(Manager.MaxSize, (int) MathF.Ceiling((float) height / Padding + 1) * Padding);
                    rt = new RenderTarget2D(Manager.GraphicsDevice, widthReal, heightReal, false, SurfaceFormat.Color,
                        DepthFormat.None, MSAA ? Manager.MultiSampleCount : 0, RenderTargetUsage.PlatformContents);
                    TotalMemory += rt.GetMemorySizePoT();
                }

                allInUse.Add(rt);
                UsedMemory += rt.GetMemorySizePoT();
            }

            return new RenderTarget2DRegion(this, rt, new Rectangle(0, 0, rt.Width, rt.Height), new Rectangle(0, 0, width, height));
        }

        // Takes (or disposes) an renderTarget2d for later use
        public void Free(RenderTarget2D rt) {
            lock (regions) {
                if (!allInUse.Remove(rt))
                    throw new Exception("Trying to free a RenderTarget2D in the wrong pool / double-free?");

                UsedMemory -= rt.GetMemorySizePoT();

                if (rt.IsDisposed) return; // What?

                if (RegionsUsed >= regions.Length) { // All regions are used up
                    rt.Dispose();
                    TotalMemory -= rt.GetMemorySizePoT();
                } else {
                    for (int i = 0; i < regions.Length; i++) { // Find a dummy one and replace it
                        if (regions[i].IsDummy) {
                            regions[i] = new RenderTarget2DWrapper(rt, TimeSpan.FromSeconds(LifeTime));
                            RegionsUsed++;
                            return;
                        }
                    }

                    throw new Exception("RegionUsed desync! (most likely)");
                }
            }

        }

        public void Dump(string dir, string name) {
            for (int i = regions.Length - 1; i >= 0; --i) {
                RenderTarget2DWrapper entry = regions[i];
                if (entry.IsDisposed)
                    continue;
                using FileStream fs = new(Path.Combine(dir, $"pooled_{name}_{i}.png"), FileMode.Create);
                entry.RT?.SaveAsPng(fs, entry.RT.Width, entry.RT.Height);
            }

            {
                int i = 0;
                foreach (RenderTarget2D rt in allInUse) {
                    using FileStream fs = new(Path.Combine(dir, $"unpooled_{name}_{i}.png"), FileMode.Create);
                    rt.SaveAsPng(fs, rt.Width, rt.Height);
                    ++i;
                }
            }
        }
        
        // Wrapper around RenderTarget2D to get nice properties
        public class RenderTarget2DWrapper : ISizeable, IDisposable {
            public RenderTarget2D? RT;
            public int Width => RT?.Width ?? 0;
            public int Height => RT?.Height ?? 0;
            public bool IsDisposed => RT?.IsDisposed ?? true;
            public readonly bool IsDummy;
            public DateTime CreationDate = DateTime.Now;
            public readonly TimeSpan LifeTime = TimeSpan.Zero;

            public RenderTarget2DWrapper() {
                IsDummy = true;
            }
            public RenderTarget2DWrapper(RenderTarget2D rt, TimeSpan lifeTime) {
                RT = rt;
                LifeTime = lifeTime;
                IsDummy = false;
                CreationDate = DateTime.Now;
            }

            public bool LifeCheck() {
                if (IsDummy) return false;

                if (CreationDate.Add(LifeTime).CompareTo(DateTime.Now) < 0) {
                    Dispose();
                    return true;
                }

                return false;
            }

            public void Dispose() {
                RT?.Dispose();
                RT = null;
            }
        }
    }
}