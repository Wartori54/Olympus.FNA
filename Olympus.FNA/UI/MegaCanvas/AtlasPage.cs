// #define OLYMPUS_DEBUG_ATLASPAGE

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace OlympUI.MegaCanvas {
    public sealed class AtlasPage : IDisposable {

        public readonly CanvasManager Manager;
        public readonly GraphicsDevice Graphics;

        public readonly RenderTarget2D RT;

        public readonly List<Rectangle> Spaces = new();
        public readonly HashSet<RenderTarget2DRegion> Taken = new();

        public int Reclaimed = 0;
        public int ReclaimedPrev = 0;
        public int ReclaimedFrames = 0;

        public AtlasPage(CanvasManager manager) {
            Manager = manager;
            Graphics = manager.GraphicsDevice;

            RT = new(Graphics, Manager.PageSize, Manager.PageSize, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
            Spaces.Add(new(0, 0, RT.Width, RT.Height));

#if OLYMPUS_DEBUG_ATLASPAGE
            GraphicsDevice gd = Graphics;
            GraphicsStateSnapshot gss = new(gd);

            gd.SetRenderTarget(RT);
            using SpriteBatch sb = new(gd);
            sb.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.PointWrap, DepthStencilState.Default, UI.RasterizerStateCullCounterClockwiseUnscissoredNoMSAA);
            sb.Draw(
                Assets.DebugUnused.Value,
                new Vector2(0f, 0f),
                new Rectangle(0, 0, RT.Width, RT.Height),
                Color.White,
                0,
                Vector2.Zero,
                1f,
                SpriteEffects.None,
                0
            );
            sb.End();

            gss.Apply();
#endif
        }

        public void Dispose() {
            RT.Dispose();
        }

        public void Update() {
            if (Reclaimed >= 16 || (Reclaimed >= 8 && Reclaimed == ReclaimedPrev)) {
                ReclaimedFrames++;
            } else {
                ReclaimedFrames = 0;
                ReclaimedPrev = Reclaimed;
            }

            if (ReclaimedFrames >= 30 || Reclaimed >= 32) {
                Cleanup();
            }
        }

        /// <summary>
        /// Obtains a free region in the atlas.
        /// </summary>
        /// <param name="want">The size of the region.</param>
        /// <returns>A region of the specified size, or null if no space is available.</returns>
        public RenderTarget2DRegion? GetRegion(Rectangle want) {
            // Find best candidate
            Rectangle space = default;
            int index = -1;
            for (int i = Spaces.Count - 1; i >= 0; --i) {
                Rectangle entry = Spaces[i];
                if (want.Width <= entry.Width && want.Height <= entry.Height && (
                        index == -1 ||
                        (entry.Width < space.Width && entry.Height < space.Height) ||
                        (want.Width < want.Height ? entry.Width <= space.Width : entry.Height <= space.Height)
                )) {
                    space = entry;
                    index = i;
                    if (want.Width == entry.Width && want.Height == entry.Height)
                        break;
                }
            }

            // No space left 
            if (index == -1)
                return null;

            // Since the found usable space is likely to be a lot larger than the size it was asked for
            // just split the extra parts into new spaces.
            Rectangle taken = new(space.X, space.Y, want.Width, want.Height); // How much does the request take
            RenderTarget2DRegion rtrg = new(this, RT, taken, taken);
            bool replace = true; // We can replace a single region on the spaces array
            if (taken.Width < taken.Height) {
                /*
                    +-----------+-----------+
                    |taken      |taken.r    |
                    |           |space.y    |
                    |           |s.r - t.r  |
                    |           |space.h    |
                    +-----------+           |
                    |space.x    |           |
                    |taken.b    |           |
                    |taken.w    |           |
                    |s.b - t.b  |           |
                    |           |           |
                    +-----------------------+
                 */
                if (taken.Right < space.Right) {
                    Rectangle free = new(taken.Right, space.Y, space.Right - taken.Right, space.Height);
                    if (replace) {
                        replace = false;
                        Spaces[index] = free;
                    } else {
                        Spaces.Add(free);
                    }
                }
                if (taken.Bottom < space.Bottom) {
                    Rectangle free = new(space.X, taken.Bottom, taken.Width, space.Bottom - taken.Bottom);
                    if (replace) {
                        replace = false;
                        Spaces[index] = free;
                    } else {
                        Spaces.Add(free);
                    }
                }

            } else {
                /*
                    +-----------+-----------+
                    |taken      |taken.r    |
                    |           |space.y    |
                    |           |s.r - t.r  |
                    |           |taken.h    |
                    +-----------+-----------+
                    |space.x                |
                    |taken.b                |
                    |space.w                |
                    |s.b - t.b              |
                    |                       |
                    +-----------------------+
                 */
                if (taken.Right < space.Right) {
                    Rectangle free = new(taken.Right, space.Y, space.Right - taken.Right, taken.Height);
                    if (replace) {
                        replace = false;
                        Spaces[index] = free;
                    } else {
                        Spaces.Add(free);
                    }
                }
                if (taken.Bottom < space.Bottom) {
                    Rectangle free = new(space.X, taken.Bottom, space.Width, space.Bottom - taken.Bottom);
                    if (replace) {
                        replace = false;
                        Spaces[index] = free;
                    } else {
                        Spaces.Add(free);
                    }
                }
            }

            if (replace) // if we did not replace any region remove it since, otherwise theres going to be a hole
                Spaces.RemoveAt(index);

            Taken.Add(rtrg);
            return rtrg;
        }

        public void Free(RenderTarget2DRegion? rtrg) {
            if (rtrg is null)
                return;
            if (rtrg.Page != this)
                throw new Exception($"Attempting free of atlas region {rtrg.Region} that belongs to another page");
            if (!Taken.Remove(rtrg))
                throw new Exception($"Attempting double-free of atlas region {rtrg.Region}");
            Spaces.Add(rtrg.Region);
            Reclaimed++;

#if OLYMPUS_DEBUG_ATLASPAGE
            GraphicsDevice gd = Graphics;
            GraphicsStateSnapshot gss = new(gd);

            gd.SetRenderTarget(RT);
            using SpriteBatch sb = new(gd);
            sb.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.PointWrap, DepthStencilState.Default, UI.RasterizerStateCullCounterClockwiseUnscissoredNoMSAA);
            sb.Draw(
                Assets.DebugDisposed.Value,
                new Vector2(rtrg.Region.X, rtrg.Region.Y),
                new Rectangle(0, 0, rtrg.Region.Width, rtrg.Region.Height),
                Color.White,
                0,
                Vector2.Zero,
                1f,
                SpriteEffects.None,
                0
            );
            sb.End();

            gss.Apply();
#endif
        }

        public void Cleanup() {
            Spaces.Clear();
            Spaces.Add(new(0, 0, RT.Width, RT.Height));

            foreach (RenderTarget2DRegion rtrg in Taken) {
                for (int i = Spaces.Count - 1; i >= 0; --i) {
                    Rectangle space = Spaces[i];
                    Rectangle taken = rtrg.Region;

                    // Rectangle subtraction ported from Olympus Lua, first implemented in Lönn.

                    int tlx = Math.Max(space.X, taken.X);
                    int tly = Math.Max(space.Y, taken.Y);
                    int brx = Math.Min(space.Right, taken.Right);
                    int bry = Math.Min(space.Bottom, taken.Bottom);

                    if (tlx >= brx || tly >= bry) {
                        // No intersection.
                        continue;
                    }

                    Spaces.RemoveAt(i);
                    int ii = i;

                    if (taken.Width < taken.Height) {
                        // Large left rectangle
                        if (tlx > space.X)
                            Spaces.Insert(ii++, new(space.X, space.Y, tlx - space.X, space.Height));

                        // Large right rectangle
                        if (brx < space.Right)
                            Spaces.Insert(ii++, new(brx, space.Y, space.Right - brx, space.Height));

                        // Small top rectangle
                        if (tly > space.Y)
                            Spaces.Insert(ii++, new(tlx, space.Y, brx - tlx, tly - space.Y));

                        // Small bottom rectangle
                        if (bry < space.Bottom)
                            Spaces.Insert(ii++, new(tlx, bry, brx - tlx, space.Bottom - bry));

                    } else {
                        // Small left rectangle
                        if (tlx > space.X)
                            Spaces.Insert(ii++, new(space.X, tly, tlx - space.X, bry - tly));

                        // Small right rectangle
                        if (brx < space.Right)
                            Spaces.Insert(ii++, new(brx, tly, space.Right - brx, bry - tly));

                        // Large top rectangle
                        if (tly > space.Y)
                            Spaces.Insert(ii++, new(space.X, space.Y, space.Width, tly - space.Y));

                        // Large bottom rectangle
                        if (bry < space.Bottom)
                            Spaces.Insert(ii++, new(space.X, bry, space.Width, space.Bottom - bry));

                    }
                }
            }
        }

    }
}
