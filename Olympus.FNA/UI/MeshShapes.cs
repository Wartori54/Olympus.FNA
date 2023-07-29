using LibTessDotNet;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace OlympUI {
    using static MeshShapes;

    public sealed unsafe class MeshShapes<TVertex> : IEnumerable where TVertex : unmanaged, IVertexType {

        public TVertex[] Vertices = new TVertex[32];
        public int VerticesMax = 0;
        public short[] Indices = new short[128];
        public int IndicesMax = 0;

        public Color Color = Color.White;
        public int AutoPoints = 64;

        public IVertexMesh<TVertex>? Mesh;

        private readonly VertexGenerator<TVertex> Gen = VertexGenerator.Get<TVertex>();

        public void Apply(IVertexMesh<TVertex> mesh) {
            Mesh = mesh;
            mesh.Apply(Vertices, VerticesMax, Indices, IndicesMax);
        }

        public void Clear() {
            VerticesMax = 0;
            IndicesMax = 0;
        }

        public void GrowVertices(int i) {
            int max = VerticesMax + i;
            if (Vertices.Length <= max) {
                int size = Vertices.Length;
                do {
                    size <<= 1;
                } while (size <= max);
                Array.Resize(ref Vertices, size);
            }
        }

        public void GrowIndices(int i) {
            int max = IndicesMax + i;
            if (Indices.Length <= max) {
                int size = Indices.Length;
                do {
                    size <<= 1;
                } while (size <= max);
                Array.Resize(ref Indices, size);
            }
        }

        public void AddVertex(TVertex v) {
            GrowVertices(1);
            Vertices[VerticesMax++] = v;
        }

        public void AddIndex(short v) {
            GrowIndices(1);
            Indices[IndicesMax++] = v;
        }

        public void AddVertices(TVertex[] arr) {
            GrowVertices(arr.Length);
            Array.Copy(arr, 0, Vertices, VerticesMax, arr.Length);
            VerticesMax += arr.Length;
        }

        public void AddIndices(ushort[] arr) {
            GrowIndices(arr.Length);
            Array.Copy(arr, 0, Indices, IndicesMax, arr.Length);
            IndicesMax += arr.Length;
        }

        public MeshShapes<TVertex> Optimize() {
            TVertex[] verticesOld = Vertices.ToArray();
            int verticesOldMax = VerticesMax;
            VerticesMax = 0;
            Dictionary<TVertex, short> verticesMap = new();

            short[] indicesOld = Indices.ToArray();
            int indicesOldMax = IndicesMax;
            IndicesMax = 0;
            short[] indicesMap = new short[verticesOldMax];

            for (int i = 0; i < verticesOldMax; i++) {
                TVertex vertex = verticesOld[i];
                if (verticesMap.TryGetValue(vertex, out short found)) {
                    indicesMap[i] = found;
                } else {
                    verticesMap.Add(vertex, (short) i);
                    indicesMap[i] = (short) i;
                    AddVertex(vertex);
                }
            }

            for (int i = 0; i < indicesOldMax; i++)
                AddIndex(indicesMap[indicesOld[i]]);

            return this;
        }

        public void AutoApply() {
            if (Mesh is null)
                return;

            Mesh.QueueNext(Vertices, VerticesMax, Indices, IndicesMax);
        }

        public void Prepare(out int index, out int indexIndex) {
            index = VerticesMax;
            indexIndex = IndicesMax;
        }

        public void Prepare(Color colorIn, out int index, out int indexIndex, out Color color) {
            Prepare(out index, out indexIndex);

            if (colorIn == default) {
                color = Color;
            } else {
                color = colorIn.Multiply(Color);
            }
        }

        public void Add(Raw shape) {
            int index = VerticesMax;
            int indexIndex = IndicesMax;

            AddVertices(shape.Vertices);

            GrowIndices(shape.Indices.Length);
            for (int i = shape.Indices.Length - 1; i >= 0; --i)
                Indices[indexIndex + i] = (short) (shape.Indices[i] + index);
            IndicesMax += shape.Indices.Length;

            AutoApply();
        }

        public void Add(Poly shape) {
            Prepare(shape.Color, out int index, out int indexIndex, out Color color);
            VertexGenerator<TVertex> gen = Gen;

            if (shape.Width != 0f) {
                // Luckily lines are somewhat easy to deal with...
#if false
                foreach (VertexPositionColorTexture vertex in shape) {
                    AddVertex(new(vertex.Position, color, vertex.TextureCoordinate));
                }
#else
                GrowVertices(shape.VertexCount);
                fixed (TVertex* vertices = &Vertices[index]) {
                    TVertex* iV = vertices - 1;
                    foreach (TVertex vertex in shape) {
                        *(++iV) = gen.Apply(vertex, null, color, null);
                    }
                }
                VerticesMax += shape.VertexCount;
#endif

                int count = (VerticesMax - index) / 2 - 1;
                GrowIndices(count * 6);
                fixed (short* indices = &Indices[indexIndex]) {
                    for (int i = count - 1; i >= 0; --i) {
                        short* iI = &indices[6 * i];
                        int iV = index + 2 * i;

                        iI[0] = (short) (iV + 2);
                        iI[1] = (short) (iV + 1);
                        iI[2] = (short) (iV + 0);

                        iI[3] = (short) (iV + 1);
                        iI[4] = (short) (iV + 2);
                        iI[5] = (short) (iV + 3);
                    }
                }
                IndicesMax += count * 6;

                AutoApply();
                return;
            }

            // TODO: Eventually replace LibTessDotNet with a built-in triangulator / tesselator?
            Tess tess = new();

            ContourVertex[] contour = new ContourVertex[shape.XYs.Count];
            int contourIndex = 0;
            foreach (TVertex vertex in shape) {
                Vector3 pos = gen.GetPosition(vertex);
                contour[contourIndex++] = new() {
                    Position = new(pos.X, pos.Y, pos.Z),
                    Data = gen.Apply(vertex, null, color, null)
                };
            }

            tess.AddContour(contour);

            tess.Tessellate(combineCallback: (pos, data, weights) => {
                Span<TVertex> input = stackalloc TVertex[] {
                    (TVertex) data[0],
                    (TVertex) data[1],
                    (TVertex) data[2],
                    (TVertex) data[3]
                };
                return gen.Interpolate(new(pos.X, pos.Y, pos.Z), input, weights.AsSpan(0, 4));
            });

            ContourVertex[] tessVertices = tess.Vertices;
            GrowVertices(tessVertices.Length);
            fixed (TVertex* vertices = &Vertices[index]) {
                for (int i = tessVertices.Length - 1; i >= 0; --i)
                    vertices[i] = (TVertex) tessVertices[i].Data;
            }
            VerticesMax += tessVertices.Length;

            int triangles = tess.ElementCount;
            GrowIndices(triangles * 3);
            fixed (short* indices = &Indices[indexIndex])
            fixed (int* elements = tess.Elements) {
                for (int i = 0; i < triangles; i++) {
                    short* iI = &indices[3 * i];
                    int* iE = &elements[3 * i];
                    iI[0] = (short) (index + iE[0]);
                    iI[1] = (short) (index + iE[1]);
                    iI[2] = (short) (index + iE[2]);
                }
            }
            IndicesMax += triangles * 3;

            AutoApply();
        }

        public void Add(Quad shape) {
            Prepare(shape.Color, out int index, out int indexIndex, out Color color);
            VertexGenerator<TVertex> gen = Gen;

            GrowVertices(4);
            fixed (TVertex* vertices = &Vertices[index]) {
                if (!shape.HasUV) {
                    vertices[0] = gen.New(new(shape.XY1, 0f), color, new Vector2(0f, 0f));
                    vertices[1] = gen.New(new(shape.XY2, 0f), color, new Vector2(1f, 0f));
                    vertices[2] = gen.New(new(shape.XY3, 0f), color, new Vector2(0f, 1f));
                    vertices[3] = gen.New(new(shape.XY4, 0f), color, new Vector2(1f, 1f));
                } else {
                    vertices[0] = gen.New(new(shape.XY1, 0f), color, shape.UV1);
                    vertices[1] = gen.New(new(shape.XY2, 0f), color, shape.UV2);
                    vertices[2] = gen.New(new(shape.XY3, 0f), color, shape.UV3);
                    vertices[3] = gen.New(new(shape.XY4, 0f), color, shape.UV4);
                }
            }
            VerticesMax += 4;

            GrowIndices(6);
            fixed (short* indices = &Indices[indexIndex]) {
                indices[0] = (short) (index + 0);
                indices[1] = (short) (index + 1);
                indices[2] = (short) (index + 2);

                indices[3] = (short) (index + 3);
                indices[4] = (short) (index + 2);
                indices[5] = (short) (index + 1);
            }
            IndicesMax += 6;

            AutoApply();
        }

        public void Add(Rect shape) {
            float border = shape.Border;
            bool isBorder = border != 0f;
            bool isRound =
                shape.RadiusTL != 0f || shape.RadiusTR != 0f ||
                shape.RadiusBL != 0f || shape.RadiusBR != 0f;

            Vector2 min = shape.XY1;
            Vector2 max = shape.XY2;
            Vector2 size = max - min;

            Vector2 uvMin, uvMax;
            if (shape.HasUV) {
                uvMin = shape.UVXYMin;
                uvMax = shape.UVXYMax;
            } else {
                uvMin = min;
                uvMax = max;
            }

            if (!isBorder && !isRound) {
                Add(new Quad() {
                    Color = shape.Color,
                    XY1 = new(min.X, min.Y),
                    XY2 = new(max.X, min.Y),
                    XY3 = new(min.X, max.Y),
                    XY4 = new(max.X, max.Y),
                    UVXYMin = uvMin,
                    UVXYMax = uvMax
                });
                return;
            }

            if (isBorder && !isRound) {
                // Top
                Add(new Quad() {
                    Color = shape.Color,
                    XY1 = new(min.X, min.Y),
                    XY2 = new(max.X, min.Y),
                    XY3 = new(min.X, min.Y + border),
                    XY4 = new(max.X, min.Y + border),
                    UVXYMin = uvMin,
                    UVXYMax = uvMax
                });
                // Bottom
                Add(new Quad() {
                    Color = shape.Color,
                    XY1 = new(min.X, max.Y - border),
                    XY2 = new(max.X, max.Y - border),
                    XY3 = new(min.X, max.Y),
                    XY4 = new(max.X, max.Y),
                    UVXYMin = uvMin,
                    UVXYMax = uvMax
                });
                // Left
                Add(new Quad() {
                    Color = shape.Color,
                    XY1 = new(min.X, min.Y + border),
                    XY2 = new(min.X + border, min.Y + border),
                    XY3 = new(min.X, max.Y - border),
                    XY4 = new(min.X + border, max.Y - border),
                    UVXYMin = uvMin,
                    UVXYMax = uvMax
                });
                // Right
                Add(new Quad() {
                    Color = shape.Color,
                    XY1 = new(max.X - border, min.Y + border),
                    XY2 = new(max.X, min.Y + border),
                    XY3 = new(max.X - border, max.Y - border),
                    XY4 = new(max.X, max.Y - border),
                    UVXYMin = uvMin,
                    UVXYMax = uvMax
                });
                return;
            }

            Poly poly = new() {
                Color = shape.Color,
                Width = shape.Border,
                UVXYMin = uvMin,
                UVXYMax = uvMax
            };

            int pointsPerBend = shape.RadiusPoints;
            if (pointsPerBend == 0) {
                if (shape.RadiusTL != 0f)
                    pointsPerBend++;
                if (shape.RadiusTR != 0f)
                    pointsPerBend++;
                if (shape.RadiusBL != 0f)
                    pointsPerBend++;
                if (shape.RadiusBR != 0f)
                    pointsPerBend++;
                pointsPerBend = AutoPoints / pointsPerBend;
            }

            min += new Vector2(border * 0.5f, border * 0.5f);
            max -= new Vector2(border * 0.5f, border * 0.5f);

            float pointsPerBendPerStep = (pointsPerBend + 2f) * 2f;
            pointsPerBend += 2;
            float radiusMin = Math.Min((size.X - border) * 0.5f, (size.Y - border) * 0.5f);
            float angle;
            float radius;
            Vector2 last = default;

            angle = MathF.PI * 0.0f;

            radius = Math.Min(shape.RadiusTL, radiusMin);
            for (int i = 0; i < pointsPerBend; i++)
                poly.XYs.Add(last = new(
                    min.X + radius * (1f - MathF.Cos(angle + i * MathF.PI / pointsPerBendPerStep)),
                    min.Y + radius * (1f - MathF.Sin(angle + i * MathF.PI / pointsPerBendPerStep))
                ));

            angle = MathF.PI * 0.5f;
            poly.XYs[^1] = new(last.X, min.Y);

            radius = Math.Min(shape.RadiusTR, radiusMin);
            for (int i = 0; i < pointsPerBend; i++)
                poly.XYs.Add(last = new(
                    max.X - radius * (1f + MathF.Cos(angle + i * MathF.PI / pointsPerBendPerStep)),
                    min.Y + radius * (1f - MathF.Sin(angle + i * MathF.PI / pointsPerBendPerStep))
                ));

            angle = MathF.PI * 1.0f;
            poly.XYs[^1] = new(max.X, last.Y);

            radius = Math.Min(shape.RadiusBR, radiusMin);
            for (int i = 0; i < pointsPerBend; i++)
                poly.XYs.Add(last = new(
                    max.X - radius * (1f + MathF.Cos(angle + i * MathF.PI / pointsPerBendPerStep)),
                    max.Y - radius * (1f + MathF.Sin(angle + i * MathF.PI / pointsPerBendPerStep))
                ));

            angle = MathF.PI * 1.5f;
            poly.XYs[^1] = new(last.X, max.Y);

            radius = Math.Min(shape.RadiusBL, radiusMin);
            for (int i = 0; i < pointsPerBend; i++)
                poly.XYs.Add(last = new(
                    min.X + radius * (1f - MathF.Cos(angle + i * MathF.PI / pointsPerBendPerStep)),
                    max.Y - radius * (1f + MathF.Sin(angle + i * MathF.PI / pointsPerBendPerStep))
                ));

            angle = MathF.PI * 0.0f;
            poly.XYs[^1] = new(min.X, last.Y);

            poly.XYs.Add(poly.XYs[0]);

            Add(poly);
        }

        public void Add(Line shape) {
            Vector2 normal = GetNormal(shape.XY1, shape.XY2) *
                            (shape.Width != 0f ? shape.Width : Line.DefaultWidth) * 0.5f;
            if (shape.Radius == 0f) {
                Add(new Quad() {
                    Color = shape.Color,
                    XY1 = shape.XY1 + normal,
                    XY2 = shape.XY2 + normal,
                    XY3 = shape.XY1 - normal,
                    XY4 = shape.XY2 - normal
                });
                return;
            }

            if (shape.Radius > shape.Width)
                shape.Width = shape.Radius;
            
            int pointsPerBend = shape.RadiusPoints;
            if (pointsPerBend == 0) {
                pointsPerBend = AutoPoints;
            }
            
            Poly poly = new() {
                Color = shape.Color,
                Width = shape.Width/2,
                UVXYMin = shape.XY1,
                UVXYMax = shape.XY2,
            };

            shape.Radius /= 2*2;
            
            float theta = MathF.Atan2(normal.Y, normal.X);

            for (int i = 0; i < pointsPerBend; i++) {
                poly.XYs.Add(new (
                    shape.XY1.X + shape.Radius * MathF.Cos(theta - i * MathF.PI / pointsPerBend),
                    shape.XY1.Y + shape.Radius * MathF.Sin(theta - i * MathF.PI / pointsPerBend)
                ));
            }

            theta -= MathF.PI;
            
            for (int i = 0; i < pointsPerBend; i++) {
                poly.XYs.Add(new (
                    shape.XY2.X + shape.Radius * MathF.Cos(theta - i * MathF.PI / pointsPerBend),
                    shape.XY2.Y + shape.Radius * MathF.Sin(theta - i * MathF.PI / pointsPerBend)
                ));
            }
            
            poly.XYs.Add(poly.XYs[0]);

            Add(poly);
        }

        public void Add(Arc shape) {
            if (MathF.Abs(shape.AngleStart - shape.AngleEnd) > 2 * MathF.PI) {
                shape.AngleEnd = shape.AngleStart + 2 * MathF.PI;
            } else if (shape.AngleStart > shape.AngleEnd) {
                (shape.AngleEnd, shape.AngleStart) = (shape.AngleStart, shape.AngleEnd);
            }
            
            Poly poly = new() {
                Color = shape.Color,
                Width = shape.Width,
                UVXYMin = shape.XY - new Vector2(shape.RadiusX, shape.RadiusY),
                UVXYMax = shape.XY + new Vector2(shape.RadiusX, shape.RadiusY),
            };

            const float error = 0.33f;
            if (shape.RadiusPoints == 0)
                shape.RadiusPoints = (int)MathF.Ceiling(MathF.PI / MathF.Acos(1f - error / (shape.Radius < error ? 1f : shape.Radius)));
            // https://stackoverflow.com/questions/11774038/how-to-render-a-circle-with-as-few-vertices-as-possible

            float angleStep = 2 * MathF.PI / shape.RadiusPoints;

            for (float theta = shape.AngleStart; theta < shape.AngleEnd+angleStep; theta += angleStep) {
                poly.XYs.Add(new (
                    shape.XY.X + shape.RadiusX * MathF.Cos(theta),
                    shape.XY.Y + shape.RadiusY * MathF.Sin(theta)
                ));
            }
            
            Add(poly);
            
        }

        public IEnumerator GetEnumerator()
            => throw new CompilerSatisfactionException();

        public struct Raw {
            public TVertex[] Vertices;
            public ushort[] Indices;
        }

        public enum LineCornerType {
            Cut,
            Extend
        }

        public class Poly : IEnumerable<TVertex> {
            public Color Color;

            public List<Vector2> XYs = new();
            public float Width;

            public Vector2 UVXYMin;
            public Vector2 UVXYMax;
            public Vector2 UVXYSize {
                get => UVXYMax - UVXYMin;
                set => UVXYMax = UVXYMin + value;
            }

            public LineCornerType LineCornerType;

            public int VertexCount =>
                Width * 0.5f == 0f ? XYs.Count :
                XYs[^1] == XYs[0] ? (XYs.Count * 4 - 2) :
                (XYs.Count * 4 - 4);

            public void Add(Color color)
                => Color = color;

            public void Add(float width)
                => Width = width;

            public void Add(Vector2 xy)
                => XYs.Add(xy);

            IEnumerator IEnumerable.GetEnumerator()
                => GetEnumerator();

            public IEnumerator<TVertex> GetEnumerator() {
                VertexGenerator<TVertex> gen = VertexGenerator.Get<TVertex>();

                Color color = Color != default ? Color : Color.White;
                float width = Width * 0.5f;

                Vector2 min = UVXYMin;
                Vector2 max = UVXYMax;
                if (min == default && max == default) {
                    min = max = XYs[0];
                    for (int i = 1; i < XYs.Count - 1; i++) {
                        Vector2 xy = XYs[i];
                        min.X = Math.Min(min.X, xy.X);
                        min.Y = Math.Min(min.Y, xy.Y);
                        max.X = Math.Max(max.X, xy.X);
                        max.Y = Math.Max(max.Y, xy.Y);
                    }
                    min = new(min.X - width, min.Y - width);
                    max = new(max.X + width, max.Y + width);
                }
                Vector2 size = new(max.X - min.X, max.Y - min.Y);
                Vector2 sizeInv = new(1f / size.X, 1f / size.Y);
                Vector2 minSizeInv = min / size;

                if (width == 0f) {
                    for (int i = 0; i < XYs.Count; i++) {
                        Vector2 xy = XYs[i];
                        yield return gen.New(new(xy, 0f), color, xy * sizeInv - minSizeInv);
                    }

                } else {
                    Vector2 xy = XYs[0];
                    Vector2 startN = GetNormal(xy, XYs[1]);
                    Vector2 startD = Vector2.Normalize(XYs[1] - xy);
                    Vector2 normal = startN;
                    Vector2 normalW = normal * width;
                    Vector2 dir = startD;
                    Vector2 startT = xy + normalW;
                    Vector2 startB = xy - normalW;
                    xy = XYs[^1];
                    Vector2 endN = normal = GetNormal(XYs[^2], xy);
                    normalW = normal * width;
                    Vector2 endD = dir = Vector2.Normalize(xy - XYs[^2]);
                    Vector2 endT = xy + normalW;
                    Vector2 endB = xy - normalW;

                    if (xy == XYs[0]) {
                        // Push vertices out of overlaps using intersection points
                        Vector2 iT = UIMath.Intersect(
                            startT, startT + startD,
                            endT, endT + endD
                        );
                        Vector2 iB = UIMath.Intersect(
                            startB, startB + startD,
                            endB, endB + endD
                        );
                        if (!float.IsNaN(iT.X) && !float.IsNaN(iT.Y) &&
                            !float.IsNaN(iB.X) && !float.IsNaN(iB.Y)) {
                            switch (LineCornerType) {
                                case LineCornerType.Cut:
                                default:
                                    if ((MathF.Atan2(endD.X, endD.Y) - MathF.Atan2(startD.X, startD.Y) + MathF.PI * 2f) % (MathF.PI * 2f) > MathF.PI) {
                                        startT = endT = iT;
                                    } else {
                                        startB = endB = iB;
                                    }
                                    break;

                                case LineCornerType.Extend:
                                    startT = endT = iT;
                                    startB = endB = iB;
                                    break;
                            }
                        }
                    }

                    normal = startN;
                    normalW = normal * width;
                    dir = startD;
                    yield return gen.New(new(startT, 0f), color, startT * sizeInv - minSizeInv);
                    yield return gen.New(new(startB, 0f), color, startB * sizeInv - minSizeInv);

                    for (int i = 1; i < XYs.Count - 1; i++) {
                        xy = XYs[i];
                        Vector2 pN = normal;
                        Vector2 pD = dir;
                        Vector2 pT = xy + normalW;
                        Vector2 pB = xy - normalW;
                        Vector2 nN = normal = GetNormal(xy, XYs[i + 1]);
                        normalW = normal * width;
                        Vector2 nD = dir = Vector2.Normalize(XYs[i + 1] - xy);
                        Vector2 nT = xy + normalW;
                        Vector2 nB = xy - normalW;

                        // Push vertices out of overlaps using intersection points
                        Vector2 iT = UIMath.Intersect(
                            pT, pT + pD,
                            nT, nT + nD
                        );
                        Vector2 iB = UIMath.Intersect(
                            pB, pB + pD,
                            nB, nB + nD
                        );
                        if (!float.IsInfinity(iT.X) && !float.IsInfinity(iT.Y) &&
                            !float.IsInfinity(iB.X) && !float.IsInfinity(iB.Y)) {
                            switch (LineCornerType) {
                                case LineCornerType.Cut:
                                default:
                                    if ((MathF.Atan2(pD.X, pD.Y) - MathF.Atan2(nD.X, nD.Y) + MathF.PI * 2f) % (MathF.PI * 2f) > MathF.PI) {
                                        pT = nT = iT;
                                    } else {
                                        pB = nB = iB;
                                    }
                                    break;

                                case LineCornerType.Extend:
                                    pT = nT = iT;
                                    pB = nB = iB;
                                    break;
                            }
                        }

                        yield return gen.New(new(pT, 0f), color, pT * sizeInv - minSizeInv);
                        yield return gen.New(new(pB, 0f), color, pB * sizeInv - minSizeInv);
                        yield return gen.New(new(nT, 0f), color, nT * sizeInv - minSizeInv);
                        yield return gen.New(new(nB, 0f), color, nB * sizeInv - minSizeInv);
                    }

                    yield return gen.New(new(endT, 0f), color, endT * sizeInv - minSizeInv);
                    yield return gen.New(new(endB, 0f), color, endB * sizeInv - minSizeInv);

                    if (XYs[^1] == XYs[0]) {
                        yield return gen.New(new(startT, 0f), color, startT * sizeInv - minSizeInv);
                        yield return gen.New(new(startB, 0f), color, startB * sizeInv - minSizeInv);
                    }
                }
            }
        }

    }

    public static class MeshShapes {

        public static Vector2 GetNormal(Vector2 a, Vector2 b)
            => Vector2.Normalize(new(b.Y - a.Y, a.X - b.X));

        public struct Quad {
            public Color Color;
            private Vector2 _XY1;
            public Vector2 XY1 {
                get => _XY1;
                set {
                    _XY1 = value;
                    if (((_RecalcUVFlags |= 0b000001) & 0b001111) == 0b001111)
                        RecalcUV();
                }
            }
            private Vector2 _XY2;
            public Vector2 XY2 {
                get => _XY2;
                set {
                    _XY2 = value;
                    if (((_RecalcUVFlags |= 0b000010) & 0b001111) == 0b001111)
                        RecalcUV();
                }
            }
            private Vector2 _XY3;
            public Vector2 XY3 {
                get => _XY3;
                set {
                    _XY3 = value;
                    if (((_RecalcUVFlags |= 0b000100) & 0b001111) == 0b001111)
                        RecalcUV();
                }
            }
            private Vector2 _XY4;
            public Vector2 XY4 {
                get => _XY4;
                set {
                    _XY4 = value;
                    if (((_RecalcUVFlags |= 0b001000) & 0b001111) == 0b001111)
                        RecalcUV();
                }
            }
            public Vector2 UV1;
            public Vector2 UV2;
            public Vector2 UV3;
            public Vector2 UV4;
            public bool HasUV => UV1 != default || UV2 != default || UV3 != default || UV4 != default;
            private Vector2 _UVXYMin;
            private Vector2 _UVXYMax;
            public Vector2 UVXYMin {
                get => _UVXYMin;
                set {
                    _UVXYMin = value;
                    if (((_RecalcUVFlags |= 0b010000) & 0b110000) == 0b110000)
                        RecalcUV();
                }
            }
            public Vector2 UVXYMax {
                get => _UVXYMax;
                set {
                    _UVXYMax = value;
                    if (((_RecalcUVFlags |= 0b100000) & 0b110000) == 0b110000)
                        RecalcUV();
                }
            }
            public Vector2 UVXYSize {
                get => UVXYMax - UVXYMin;
                set => UVXYMax = UVXYMin + value;
            }
            private uint _RecalcUVFlags;
            private void RecalcUV() {
                if (_UVXYMin == default && _UVXYMax == default) {
                    UV1 = default;
                    UV2 = default;
                    UV3 = default;
                    UV4 = default;
                    return;
                }

                Vector2 min = _UVXYMin;
                Vector2 size = _UVXYMax - min;
                UV1 = (XY1 - min) / size;
                UV2 = (XY2 - min) / size;
                UV3 = (XY3 - min) / size;
                UV4 = (XY4 - min) / size;
            }
        }

        public struct Rect {
            public Color Color;
            public Vector2 XY1;
            public Vector2 XY2;
            public Vector2 Size {
                get => XY2 - XY1;
                set => XY2 = XY1 + value;
            }
            public bool HasUV => _UVXYMin != default || _UVXYMax != default;
            private Vector2 _UVXYMin;
            private Vector2 _UVXYMax;
            public Vector2 UVXYMin {
                get => _UVXYMin;
                set => _UVXYMin = value;
            }
            public Vector2 UVXYMax {
                get => _UVXYMax;
                set => _UVXYMax = value;
            }
            public Vector2 UVXYSize {
                get => UVXYMax - UVXYMin;
                set => UVXYMax = UVXYMin + value;
            }
            public float Border;
            public float RadiusTL;
            public float RadiusTR;
            public float RadiusBL;
            public float RadiusBR;
            public float Radius {
                get => Math.Max(Math.Max(RadiusTL, RadiusTR), Math.Max(RadiusBL, RadiusBR));
                set {
                    RadiusTL = value;
                    RadiusTR = value;
                    RadiusBL = value;
                    RadiusBR = value;
                }
            }
            public int RadiusPoints;
        }

        public struct Line {
            public static float DefaultWidth = 8f;
            public Color Color;
            public Vector2 XY1;
            public Vector2 XY2;
            public float Width;
            public float Radius;
            public int RadiusPoints;
        }
        
        public struct Arc {
            public static float DefaultWidth = 8f;
            public Color Color;
            public Vector2 XY;
            public float Width;

            public float Radius {
                get => RadiusX + RadiusY / 2;
                set {
                    RadiusX = value;
                    RadiusY = value;
                }
            }
            public float RadiusX;
            public float RadiusY;
            public int RadiusPoints;
            public float AngleStart;
            public float AngleEnd;
        }

    }
}
