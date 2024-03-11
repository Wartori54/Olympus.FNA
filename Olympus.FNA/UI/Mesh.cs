using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace OlympUI {
    public abstract class Mesh : IDisposable {

        public readonly Game Game;

        public GraphicsDevice GraphicsDevice => Game.GraphicsDevice;

        public BufferTypes BufferType = BufferTypes.Auto;
        public VertexBuffer? VertexBuffer;
        public IndexBuffer? IndexBuffer;

        protected bool IsDisposed;
        protected bool NextQueued;
        protected int ReloadBufferAutoDraws;
        protected int ReloadBufferAutoReloads;

        public Mesh(Game game) {
            Game = game;
        }

        ~Mesh() {
            Dispose(false);
        }

        public virtual void QueueNext() => NextQueued = true;
        public abstract void Reload();

        public abstract void Draw();

        protected virtual void Dispose(bool disposing) {
            if (IsDisposed)
                return;
            IsDisposed = true;

            VertexBuffer?.Dispose();
            VertexBuffer = null;

            IndexBuffer?.Dispose();
            IndexBuffer = null;
        }

        public void Dispose() {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public enum BufferTypes {
            Auto,
            Static,
            Dynamic,
            None,
        }

    }

    public interface IVertexMesh<TVertex> where TVertex : unmanaged, IVertexType {

        void Apply(TVertex[] vertices, int verticesMax, short[] indices, int indicesMax);

        void QueueNext(TVertex[] vertices, int verticesMax, short[] indices, int indicesMax);

    }

    public class Mesh<TEffect, TVertex> : Mesh, IVertexMesh<TVertex> where TEffect : Effect where TVertex : unmanaged, IVertexType {

        public IReloadable<TEffect, NullMeta> Effect;
        public bool DisposeEffect;

        public TVertex[] Vertices = Array.Empty<TVertex>();
        public int VerticesMax = -1;
        public short[] Indices = Array.Empty<short>();
        public int IndicesMax = -1;

        private TVertex[]? VerticesNext;
        private int VerticesMaxNext;
        private short[]? IndicesNext;
        private int IndicesMaxNext;
        private bool DelayedUpload;

        public Func<GraphicsDevice, TEffect, bool>? BeforeDraw;

        public bool WireFrame;
        public bool MSAA = true;
        public BlendState BlendState = BlendState.AlphaBlend;
        public SamplerState SamplerState = SamplerState.LinearClamp;

        public Mesh(Game game, IReloadable<TEffect, NullMeta> effect)
            : base(game) {
            Effect = effect;
        }

        protected bool DequeueNext() {
            bool dequeued = false;

            if (VerticesNext is not null) {
#if OLYMPUS_MESH_COPYQUEUE
                if (VerticesNext.Length > Vertices.Length)
                    Vertices = new TVertex[VerticesNext.Length];
                Array.Copy(VerticesNext, 0, Vertices, 0, Vertices.Length);
#else
                Vertices = VerticesNext;
#endif
                VerticesMax = VerticesMaxNext;
                VerticesNext = null;
                dequeued = true;
            }

            if (IndicesNext is not null) {
#if OLYMPUS_MESH_COPYQUEUE
                if (IndicesNext.Length > Indices.Length)
                    indices = Indices = new ushort[IndicesNext.Length];
                Array.Copy(IndicesNext, 0, Indices, 0, Indices.Length);
#else
                Indices = IndicesNext;
#endif
                IndicesMax = IndicesMaxNext;
                IndicesNext = null;
                dequeued = true;
            }

            return dequeued;
        }

        public override void Reload() {
            if (NextQueued) {
                NextQueued = false;
                DequeueNext();
            }

            if (!UI.IsOnMainThread) {
                DelayedUpload = true;
                return;
            }
            DelayedUpload = false;

            TVertex[] vertices = Vertices;
            int verticesMax = VerticesMax;
            short[] indices = Indices;
            int indicesMax = IndicesMax;

            if (verticesMax < 0)
                verticesMax = vertices.Length;

            if (indicesMax < 0)
                indicesMax = indices.Length;

            if (VertexBuffer is not null && VertexBuffer.VertexCount < verticesMax) {
                VertexBuffer.Dispose();
                VertexBuffer = null;
            }
            if (IndexBuffer is not null && IndexBuffer.IndexCount < indicesMax) {
                IndexBuffer.Dispose();
                IndexBuffer = null;
            }

            if (verticesMax == 0 || indicesMax == 0)
                return;

            if (VertexBuffer is null || VertexBuffer.IsDisposed || VertexBuffer.GraphicsDevice != GraphicsDevice) {
                if (BufferType == BufferTypes.Dynamic) {
                    VertexBuffer = new DynamicVertexBuffer(GraphicsDevice, typeof(TVertex), verticesMax, BufferUsage.None);
                } else {
                    VertexBuffer = new(GraphicsDevice, typeof(TVertex), verticesMax, BufferUsage.None);
                }
            }
            VertexBuffer.SetData(vertices, 0, verticesMax);

            if (IndexBuffer is null || IndexBuffer.IsDisposed || IndexBuffer.GraphicsDevice != GraphicsDevice) {
                if (BufferType == BufferTypes.Dynamic) {
                    IndexBuffer = new DynamicIndexBuffer(GraphicsDevice, IndexElementSize.SixteenBits, indicesMax, BufferUsage.None);
                } else {
                    IndexBuffer = new(GraphicsDevice, IndexElementSize.SixteenBits, indicesMax, BufferUsage.None);
                }
            }
            IndexBuffer.SetData(indices, 0, indicesMax);

            NextQueued = false;
        }

        public void Apply(TVertex[] vertices, int verticesMax, short[] indices, int indicesMax) {
            _ = (VerticesNext = null, IndicesNext = null, NextQueued = false);
            _ = (Vertices = vertices, VerticesMax = verticesMax, Indices = indices, IndicesMax = indicesMax);
        }

        public override void QueueNext()
            => _ = (VerticesNext = null, IndicesNext = null, NextQueued = true);

        public void QueueNext(TVertex[] vertices, int verticesMax, short[] indices, int indicesMax)
            => _ = (VerticesNext = vertices, VerticesMaxNext = verticesMax, IndicesNext = indices, IndicesMaxNext = indicesMax, NextQueued = true);

        protected virtual bool AutoBeforeDraw() {
            GraphicsDevice gd = GraphicsDevice;
            TEffect effect = Effect.Value;

            if (!(BeforeDraw?.Invoke(gd, effect) ?? true))
                return false;

            gd.BlendState = BlendState;
            gd.DepthStencilState = DepthStencilState.Default;
            if (WireFrame) {
                gd.RasterizerState = Assets.WireFrame.Value;
            } else if (MSAA) {
                gd.RasterizerState = UI.RasterizerStateCullCounterClockwiseUnscissoredWithMSAA;
            } else {
                gd.RasterizerState = UI.RasterizerStateCullCounterClockwiseUnscissoredNoMSAA;
            }
            gd.SamplerStates[0] = SamplerState;

            return true;
        }

        public override void Draw() {
            bool forceReload = DelayedUpload;
            bool direct = false;

            if (BufferType == BufferTypes.None) {
                DequeueNext();
                direct = true;

            } else if (BufferType == BufferTypes.Auto) {
                if (NextQueued) {
                    NextQueued = false;
                    DequeueNext();
                    ReloadBufferAutoDraws = 0;
                    if (ReloadBufferAutoReloads < 16) {
                        ReloadBufferAutoReloads++;
                    } else {
                        if (VertexBuffer is not null) {
                            VertexBuffer.Dispose();
                            VertexBuffer = null;
                        }
                        if (IndexBuffer is not null) {
                            IndexBuffer.Dispose();
                            IndexBuffer = null;
                        }
                    }
                    direct = true;

                } else if (ReloadBufferAutoDraws < 128) {
                    ReloadBufferAutoDraws++;
                    ReloadBufferAutoReloads = 0;
                    if (ReloadBufferAutoDraws == 128) {
                        forceReload = true;
                    } else {
                        direct = true;
                    }
                }
            }

            if (!direct && (
                forceReload || NextQueued ||
                VertexBuffer is null || VertexBuffer.IsDisposed || VertexBuffer.GraphicsDevice != GraphicsDevice ||
                IndexBuffer is null || IndexBuffer.IsDisposed || IndexBuffer.GraphicsDevice != GraphicsDevice))
                Reload();

            int verticesMax = VerticesMax;
            if (verticesMax < 0)
                verticesMax = Vertices.Length;

            int indicesMax = IndicesMax;
            if (indicesMax < 0)
                indicesMax = Indices.Length;
            indicesMax /= 3;

            if (verticesMax == 0 || indicesMax == 0)
                return;

            GraphicsDevice gd = GraphicsDevice;
            TEffect effect = Effect.Value;

            if (direct) {
                AutoBeforeDraw();

                foreach (EffectPass pass in effect.CurrentTechnique.Passes) {
                    pass.Apply();
                    gd.DrawUserIndexedPrimitives(
                        PrimitiveType.TriangleList,
                        Vertices, 0, verticesMax,
                        Indices, 0, indicesMax
                    );
                }

            } else if (VertexBuffer is not null && IndexBuffer is not null) {
                AutoBeforeDraw();

                gd.Indices = IndexBuffer;
                gd.SetVertexBuffer(VertexBuffer);
                foreach (EffectPass pass in effect.CurrentTechnique.Passes) {
                    pass.Apply();
                    gd.DrawIndexedPrimitives(
                        PrimitiveType.TriangleList,
                        0, 0, verticesMax,
                        0, indicesMax
                    );
                }
            }
        }

        protected override void Dispose(bool disposing) {
            if (IsDisposed)
                return;
            base.Dispose(disposing);

            if (DisposeEffect)
                Effect.Dispose();

            Vertices = Array.Empty<TVertex>();
            Indices = Array.Empty<short>();

            VerticesNext = null;
            IndicesNext = null;

            BeforeDraw = null;
        }

    }

    public class BasicMesh<TVertex> : Mesh<MiniEffect, TVertex> where TVertex : unmanaged, IVertexType {

        public IReloadable<Texture2D, Texture2DMeta> Texture = Assets.White;
        public bool DisposeTexture;
        public Color Color = Color.White;

        public Matrix? Transform;

        private MeshShapes<TVertex> _Shapes;
        public MeshShapes<TVertex> Shapes {
            get => _Shapes;
            set {
                _Shapes = value;
                value.Apply(this);
            }
        }

        public BasicMesh(Game game)
            : base(game, MiniEffect.Cache.GetEffect(() => game.GraphicsDevice)) {
            _Shapes = new() {
                Mesh = this
            };
        }

        public Matrix CreateTransform()
            => CreateTransform(Vector2.Zero);
        public Matrix CreateTransform(Vector2 offset) {
            Viewport view = GraphicsDevice.Viewport;
            return Matrix.CreateOrthographicOffCenter(
                -offset.X, -offset.X + view.Width,
                -offset.Y + view.Height, -offset.Y,
                view.MinDepth, view.MaxDepth
            );
        }

        protected override bool AutoBeforeDraw() {
            GraphicsDevice gd = GraphicsDevice;
            MiniEffect effect = Effect.Value;

            if (!base.AutoBeforeDraw())
                return false;

            gd.Textures[0] = Texture.Value;
            effect.ColorParam.Value = Color.ToVector4();
            effect.TransformParam.Value = Transform ?? CreateTransform();

            return true;
        }

        protected override void Dispose(bool disposing) {
            if (IsDisposed)
                return;
            base.Dispose(disposing);

            if (DisposeTexture)
                Texture.Dispose();

            _Shapes.Clear();
        }

        public void Draw(Matrix? transform) {
            Transform = transform;
            Draw();
        }

    }

    public sealed class BasicMesh : BasicMesh<MiniVertex> {

        public BasicMesh(Game game) : base(game) {
        }

    }
}
