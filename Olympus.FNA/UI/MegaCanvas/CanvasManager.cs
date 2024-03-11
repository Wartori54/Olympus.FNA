using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;

namespace OlympUI.MegaCanvas {
    public sealed class CanvasManager : IDisposable {

        public readonly Game Game;
        public GraphicsDevice GraphicsDevice => Game.GraphicsDevice;

        public int MinSize = 8;
        public int MaxSize = 4096;
        public int PageSize = 2048;
        public int MaxPackedSize = 1024;
        public int MultiSampleCount;

        public readonly CanvasPool Pool;
        public readonly CanvasPool PoolMSAA;
        public CanvasPool DefaultPool => PoolMSAA;

        public readonly List<AtlasPage> Pages = new();

        private readonly BasicMesh BlitMesh;
        private readonly ReloadableSlot<Texture2D, Texture2DMeta> BlitTextureSlot = new();

        private readonly Queue<Action> Queued = new(); // TODO: replace this with a delayed dispose, since its only used for that

        public CanvasManager(Game game) {
            Game = game;
            BlitMesh = new BasicMesh(game) {
                MSAA = false,
                Texture = BlitTextureSlot,
                BlendState = BlendState.Opaque,
            };
            Pool = new(this, false);
            PoolMSAA = new(this, true);
        }

        public void Dispose() {
            Pool.Dispose();
            PoolMSAA.Dispose();

            foreach (AtlasPage page in Pages)
                page.Dispose();
            Pages.Clear();

            BlitMesh.Dispose();
        }

        public void Update() {
            Pool.Update();
            PoolMSAA.Update();

            lock (Pages) {
                foreach (AtlasPage page in Pages) {
                    page.Update();
                }
            }

            if (Queued.Count > 0) {
                lock (Queued) {
                    foreach (Action a in Queued)
                        a();
                    Queued.Clear();
                }
            }
        }

        public void Queue(Action a) {
            lock (Queued) {
                Queued.Enqueue(a);
            }
        }

#pragma warning disable IDE0051 // Only used via #if DEBUG && true / false
        private int _BlitID;
#pragma warning restore IDE0051

        public void Blit(Texture2D from, Rectangle fromBounds, RenderTarget2D to, Rectangle toBounds)
            => Blit(from, fromBounds, to, toBounds, new(1f, 1f, 1f, 1f));

        public void Blit(Texture2D from, Rectangle fromBounds, RenderTarget2D to, Rectangle toBounds, Color color) {
#if DEBUG && false
            {
                string path = Path.Combine(Environment.CurrentDirectory, "megacanvas");
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);
                path = Path.Combine(path, $"blit_{_BlitID++:D6}.png");
                using FileStream fs = new(path, FileMode.Create);
                to.SaveAsPng(fs, to.Width, to.Height);
            }
#endif

            GraphicsDevice gd = GraphicsDevice;
            GraphicsStateSnapshot gss = new(gd);

            gd.SetRenderTarget(to);
            BlitTextureSlot.Set(new(from, null), from);
            BasicMesh mesh = BlitMesh;
            mesh.Color = color;
            MeshShapes<MiniVertex> shapes = mesh.Shapes;
            shapes.Clear();
            shapes.Add(new MeshShapes.Quad() {
                XY1 = new(toBounds.Left, toBounds.Top),
                XY2 = new(toBounds.Right, toBounds.Top),
                XY3 = new(toBounds.Left, toBounds.Bottom),
                XY4 = new(toBounds.Right, toBounds.Bottom),
                UV1 = new(fromBounds.Left / (float) from.Width, fromBounds.Top / (float) from.Height),
                UV2 = new(fromBounds.Right / (float) from.Width, fromBounds.Top / (float) from.Height),
                UV3 = new(fromBounds.Left / (float) from.Width, fromBounds.Bottom / (float) from.Height),
                UV4 = new(fromBounds.Right / (float) from.Width, fromBounds.Bottom / (float) from.Height),
            });
            mesh.Draw();

            gss.Apply();

#if DEBUG && false
            {
                string path = Path.Combine(Environment.CurrentDirectory, "megacanvas");
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);
                path = Path.Combine(path, $"blit_{_BlitID++:D6}.png");
                using FileStream fs = new(path, FileMode.Create);
                to.SaveAsPng(fs, to.Width, to.Height);
            }
#endif
        }

        public RenderTarget2DRegion? GetPacked(RenderTarget2D old)
            => GetPacked(old, new(0, 0, old.Width, old.Height));

        /// <summary>
        /// Obtains a region from any atlas with the specified bounds and copies the data to it.
        /// </summary>
        /// <param name="old">The region from where the data should be copied from.</param>
        /// <param name="oldBounds">The bounds of that region to copy.</param>
        /// <returns>A new region with the data.</returns>
        public RenderTarget2DRegion? GetPacked(RenderTarget2D old, Rectangle oldBounds) {
            RenderTarget2DRegion? packed = GetPackedRegion(oldBounds);
            if (packed is null)
                return null;

            Blit(old, oldBounds, packed.RT, packed.Region);
            return packed;
        }

        public RenderTarget2DRegion? GetPackedAndFree(RenderTarget2DRegion old)
            => GetPackedAndFree(old, old.Region);

        /// <summary>
        /// Obtains a new region from any atlas with the specified bounds and disposes the old one.
        /// <br />
        /// Basically a migrate to atlas one liner.
        /// </summary>
        /// <param name="old">The region to copy and delete.</param>
        /// <param name="oldBounds">Its bounds.</param>
        /// <returns>A new region in the atlas with the old data.</returns>
        public RenderTarget2DRegion? GetPackedAndFree(RenderTarget2DRegion old, Rectangle oldBounds) {
            RenderTarget2DRegion? packed = GetPacked(old.RT, oldBounds);
            if (packed is null)
                return null;

            old.Dispose();
            return packed;
        }

        /// <summary>
        /// Obtains a region from any atlas.
        /// </summary>
        /// <param name="want">The region to obtain.</param>
        /// <returns>A region in the atlas or null if the operation fails.</returns>
        public RenderTarget2DRegion? GetPackedRegion(Rectangle want) {
            if (want.Width > MaxPackedSize || want.Height > MaxPackedSize)
                return null;

            // Get a region from an existing page
            foreach (AtlasPage page in Pages)
                if (page.GetRegion(want) is { } rtrg)
                    return rtrg;
            
            // Or create a new page and obtain a region from there
            AtlasPage newPage = CreateNewPage();
            return newPage.GetRegion(want) ?? throw new Exception("Failed to obtain region from newly born atlas!?");
            
        }

        public void Dump(string dir) {
            GraphicsDevice gd = GraphicsDevice;
            GraphicsStateSnapshot gss = new(gd);
            Texture2D white = Assets.White.Value;

            for (int i = Pages.Count - 1; i >= 0; --i) {
                AtlasPage page = Pages[i];
                RenderTarget2D rt = page.RT;
                using RenderTarget2D tmp = new(gd, rt.Width, rt.Height, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.DiscardContents);

                gd.SetRenderTarget(tmp);
                using SpriteBatch sb = new(gd);
                sb.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.PointClamp, DepthStencilState.Default, UI.RasterizerStateCullCounterClockwiseUnscissoredNoMSAA);
                sb.Draw(rt, new Vector2(0f, 0f), Color.White);
                sb.End();
                sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.Default, UI.RasterizerStateCullCounterClockwiseUnscissoredNoMSAA);
                foreach (Rectangle rg in page.Spaces) {
                    sb.Draw(white, new Rectangle(rg.X, rg.Y, rg.Width, 1), Color.Green * 0.7f);
                    sb.Draw(white, new Rectangle(rg.X, rg.Bottom - 1, rg.Width, 1), Color.Green * 0.7f);
                    sb.Draw(white, new Rectangle(rg.X, rg.Y + 1, 1, rg.Height - 2), Color.Green * 0.7f);
                    sb.Draw(white, new Rectangle(rg.Right - 1, rg.Y + 1, 1, rg.Height - 2), Color.Green * 0.7f);
                    sb.Draw(white, new Rectangle(rg.X + 1, rg.Y + 1, rg.Width - 2, rg.Height - 2), Color.Green * 0.3f);
                }
                foreach (RenderTarget2DRegion rtrg in page.Taken) {
                    Rectangle rg = rtrg.Region;
                    sb.Draw(white, new Rectangle(rg.X, rg.Y, rg.Width, 1), Color.Blue * 0.7f);
                    sb.Draw(white, new Rectangle(rg.X, rg.Bottom - 1, rg.Width, 1), Color.Blue * 0.7f);
                    sb.Draw(white, new Rectangle(rg.X, rg.Y + 1, 1, rg.Height - 2), Color.Blue * 0.7f);
                    sb.Draw(white, new Rectangle(rg.Right - 1, rg.Y + 1, 1, rg.Height - 2), Color.Blue * 0.7f);
                    sb.Draw(white, new Rectangle(rg.X + 1, rg.Y + 1, rg.Width - 2, rg.Height - 2), Color.Blue * 0.3f);
                }
                sb.End();

                using FileStream fs = new(Path.Combine(dir, $"atlas_{i}.png"), FileMode.Create);
                tmp.SaveAsPng(fs, tmp.Width, tmp.Height);
            }

            gss.Apply();

            foreach ((string Name, CanvasPool Pool) pool in new (string, CanvasPool)[] {
                ("main", Pool),
                ("msaa", PoolMSAA),
            }) {
                pool.Pool.Dump(dir, pool.Name);
                for (int i = pool.Pool.Entries.Length - 1; i >= 0; --i) {
                    CanvasPool.RenderTarget2DWrapper entry = pool.Pool.Entries[i];
                    if (entry.IsDisposed)
                        continue;
                    using FileStream fs = new(Path.Combine(dir, $"pooled_{pool.Name}_{i}.png"), FileMode.Create);
                    // RT nullability is handled in `IsDisposed`
                    entry.RT!.SaveAsPng(fs, entry.RT.Width, entry.RT.Height);
                }
                
                {
                    int i = 0;
                    foreach (RenderTarget2D rt in pool.Pool.Used) {
                        using FileStream fs = new(Path.Combine(dir, $"unpooled_{pool.Name}_{i}.png"), FileMode.Create);
                        rt.SaveAsPng(fs, rt.Width, rt.Height);
                        ++i;
                    }
                }
            }
        }

        private AtlasPage CreateNewPage() {
            AtlasPage newPage = new(this);
            Pages.Add(newPage);
            return newPage;
        }
        
#if DEBUG
        public bool IsOnAtlas(RenderTarget2DRegion region) {
            foreach (AtlasPage page in Pages) {
                if (page.Taken.Contains(region))
                    return true;
            }
            return false;
        }
        
#endif

    }
}
