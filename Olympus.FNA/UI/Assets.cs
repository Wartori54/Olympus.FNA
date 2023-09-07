using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace OlympUI {
    public static unsafe class Assets {

        #region FNA Helpers

        private static readonly PropertyInfo p_TitleLocation_Path =
            typeof(Game).Assembly
            .GetType("Microsoft.Xna.Framework.TitleLocation")
            ?.GetProperty("Path")
            ?? throw new Exception("TitleLocation.Path not found!");

        private static string? _Path;
        public static string Path => _Path ??= System.IO.Path.Combine((string) p_TitleLocation_Path.GetValue(null)!, "Content");

        public delegate IntPtr d_FNA3D_ReadImageStream(Stream stream, out int width, out int height, out int len, int forceW = -1, int forceH = -1, bool zoom = false);
        public static readonly d_FNA3D_ReadImageStream FNA3D_ReadImageStream =
            typeof(Game).Assembly
            .GetType("Microsoft.Xna.Framework.Graphics.FNA3D")
            ?.GetMethod("ReadImageStream")
            ?.CreateDelegate<d_FNA3D_ReadImageStream>()
            ?? throw new Exception("FNA3D_ReadImageStream not found!");

        public delegate void d_FNA3D_Image_Free(IntPtr mem);
        public static readonly d_FNA3D_Image_Free FNA3D_Image_Free =
            typeof(Game).Assembly
            .GetType("Microsoft.Xna.Framework.Graphics.FNA3D")
            ?.GetMethod("FNA3D_Image_Free")
            ?.CreateDelegate<d_FNA3D_Image_Free>()
            ?? throw new Exception("FNA3D_Image_Free not found!");

        #endregion

#if DEBUG_CONTENT
        private static readonly string PathDebug = System.IO.Path.GetFullPath($"{typeof(Assets).Assembly.Location}/../../../../../../Content");
#endif

        public static ulong ReloadID = 0;

        private static readonly Dictionary<string, object> _Gotten = new();

        public static readonly Reloadable<DynamicSpriteFont, NullMeta> Font = GetFont(
            20,
            "fonts/Poppins-Regular",
            "fonts/NotoSansCJKjp-Regular",
            "fonts/NotoSansCJKkr-Regular",
            "fonts/NotoSansCJKsc-Regular",
            "fonts/NotoSansCJKtc-Regular"
        );

        public static readonly Reloadable<DynamicSpriteFont, NullMeta> FontSmall = GetFont(
            16,
            "fonts/Poppins-Regular",
            "fonts/NotoSansCJKjp-Regular",
            "fonts/NotoSansCJKkr-Regular",
            "fonts/NotoSansCJKsc-Regular",
            "fonts/NotoSansCJKtc-Regular"
        );

        public static readonly Reloadable<DynamicSpriteFont, NullMeta> FontMono = GetFont(16, "fonts/Perfect DOS VGA 437");

        public static readonly Reloadable<DynamicSpriteFont, NullMeta> FontMonoSpace = GetFont(20,
            "fonts/SourceCodePro-Regular" // Only one because it's intended for logs
        );

        public static readonly Reloadable<DynamicSpriteFont, NullMeta> FontMonoOutlined =
            Get($"Font 'fonts/Perfect DOS VGA 437' Size '16' Outlined", default(NullMeta), () => {
                FontSystem font = new(new() {
                    TextureWidth = 2048,
                    TextureHeight = 2048,
                    PremultiplyAlpha = true,
                    // FontResolutionFactor = 2f,
                    KernelWidth = 1,
                    KernelHeight = 1,
                    Effect = FontSystemEffect.Stroked,
                    EffectAmount = 1
                });
                font.AddFonts("fonts/Perfect DOS VGA 437");
                return font.GetFont(16);
            });

        private static readonly Color[] _WhiteData = { Color.White };
        public static readonly Reloadable<Texture2D, Texture2DMeta> White = Get("White", new Texture2DMeta(1, 1, () => _WhiteData), () => {
            Texture2D tex = new(UI.Game.GraphicsDevice, 1, 1, false, SurfaceFormat.Color);
            tex.SetData(_WhiteData);
            return tex;
        });

        private static readonly Color[] _GradientQuadData = new Func<Color[]>(() => {
            Color[] data = new Color[256];
            fixed (Color* ptr = data) {
                for (int i = 0; i < data.Length; i++) {
                    float f = i / 255f;
                    f = f * f * f * i;
                    byte b = (byte) f;
                    ptr[i] = new Color(b, b, b, b);
                }
            }
            return data;
        })();
        public static readonly Reloadable<Texture2D, Texture2DMeta> GradientQuadY = Get("GradientQuadY", new Texture2DMeta(1, _GradientQuadData.Length, () => _GradientQuadData), () => {
            Texture2D tex = new(UI.Game.GraphicsDevice, 1, _GradientQuadData.Length, false, SurfaceFormat.Color);
            tex.SetData(_GradientQuadData);
            return tex;
        });

        private static readonly Color[] _GradientQuadInvData = new Func<Color[]>(() => {
            Color[] data = new Color[256];
            fixed (Color* ptr = data) {
                for (int i = 0; i < data.Length; i++) {
                    float f = 1f - i / 255f;
                    f = 255 - (1f - f * f * f) * i;
                    byte b = (byte) f;
                    ptr[i] = new Color(b, b, b, b);
                }
            }
            return data;
        })();
        public static readonly Reloadable<Texture2D, Texture2DMeta> GradientQuadYInv = Get("GradientQuadYInv", new Texture2DMeta(1, _GradientQuadInvData.Length, () => _GradientQuadInvData), () => {
            Texture2D tex = new(UI.Game.GraphicsDevice, 1, _GradientQuadInvData.Length, false, SurfaceFormat.Color);
            tex.SetData(_GradientQuadInvData);
            return tex;
        });

        public static readonly Reloadable<BasicEffect, NullMeta> BasicTextureEffect = Get("BasicTextureEffect", default(NullMeta), () => new BasicEffect(UI.Game.GraphicsDevice) {
            FogEnabled = false,
            LightingEnabled = false,
            TextureEnabled = true,
            VertexColorEnabled = false,
        });

        public static readonly Reloadable<BasicEffect, NullMeta> BasicColorEffect = Get("BasicColorEffect", default(NullMeta), () => new BasicEffect(UI.Game.GraphicsDevice) {
            FogEnabled = false,
            LightingEnabled = false,
            TextureEnabled = false,
            VertexColorEnabled = true,
        });

        public static readonly Reloadable<BasicEffect, NullMeta> BasicEffect = Get("BasicEffect", default(NullMeta), () => new BasicEffect(UI.Game.GraphicsDevice) {
            FogEnabled = false,
            LightingEnabled = false,
            TextureEnabled = true,
            VertexColorEnabled = true,
        });

        public static readonly Reloadable<RasterizerState, NullMeta> WireFrame = Get("WireFrame", default(NullMeta), () => new RasterizerState() {
            FillMode = FillMode.WireFrame,
            CullMode = CullMode.None
        });

        public static readonly Reloadable<Texture2D, Texture2DMeta> Test = GetTexture("icon");

        public static readonly Reloadable<Texture2D, Texture2DMeta> DebugUnused = GetTexture("debug/unused");
        public static readonly Reloadable<Texture2D, Texture2DMeta> DebugDisposed = GetTexture("debug/disposed");


        public static Reloadable<TValue, TMeta> Get<TValue, TMeta>(string id, TMeta meta, Func<TValue?> loader) where TMeta : struct {
            if (_Gotten.TryGetValue(id, out object? value))
                return (Reloadable<TValue, TMeta>) value;

            Reloadable<TValue, TMeta> reloadable = new(id, meta, loader);
            _Gotten[id] = reloadable;
            return reloadable;
        }


        public static bool TryGet<TValue, TMeta>(string id, [NotNullWhen(true)] out Reloadable<TValue, TMeta>? reloadable) where TMeta : struct {
            if (_Gotten.TryGetValue(id, out object? value)) {
                reloadable = (Reloadable<TValue, TMeta>) value;
                return true;
            }

            reloadable = null;
            return false;
        }


        public static Stream? OpenStream(string path, params string[] exts) {
            foreach (string ext in exts)
                if (OpenStream($"{path}.{ext}") is Stream stream)
                    return stream;
            return OpenStream(path);
        }
        public static Stream? OpenStream(string path) {
            try {
                string pathFull;

#if DEBUG_CONTENT
                pathFull = System.IO.Path.Combine(PathDebug, path);
                if (File.Exists(pathFull))
                    return File.OpenRead(pathFull);
#endif

                pathFull = System.IO.Path.Combine(Path, path);
                if (File.Exists(pathFull))
                    return File.OpenRead(pathFull);

                Console.WriteLine($"Couldn't find content file: {path}");
                return null;

            } catch (Exception e) {
                Console.WriteLine($"Failed loading content: {path}");
                Console.WriteLine(e);
                return null;
            }
        }

        public static string? GetPath(string fileTarget) {
            string pathFull;
            
            #if DEBUG_CONTENT
            pathFull = System.IO.Path.Combine(PathDebug, fileTarget);
            if (File.Exists(pathFull))
                return pathFull;
            #endif
            
            pathFull = System.IO.Path.Combine(Path, fileTarget);
            if (File.Exists(pathFull))
                return pathFull;

            Console.WriteLine($"Couldn't find content file: {fileTarget}");
            return null;
        }

        public static byte[]? OpenData(string path, params string[] exts) {
            foreach (string ext in exts)
                if (OpenData($"{path}.{ext}") is byte[] data)
                    return data;
            return OpenData(path);
        }
        public static byte[]? OpenData(string path) {
            using Stream? s = OpenStream(path);
            if (s is null)
                return null;

            using MemoryStream ms = new();
            s.CopyTo(ms);
            ms.Seek(0, SeekOrigin.Begin);
            return ms.ToArray();
        }

        public static Reloadable<DynamicSpriteFont, NullMeta> GetFont(int size, params string[] paths) {
            // FIXME: Possible memory leak!
            FontSystem font = OpenFont(paths);
            return Get($"Font '{string.Join(", ", paths)}' Size '{size}'", default(NullMeta), () => font.GetFont(size));
        }
        public static FontSystem OpenFont(params string[] paths) {
            FontSystem font = new(new() {
                TextureWidth = 256,
                TextureHeight = 256,
                PremultiplyAlpha = true,
                // FontResolutionFactor = 2f,
                KernelWidth = 1,
                KernelHeight = 1
            });
            font.AddFonts(paths);
            return font;
        }

        public static void AddFonts(this FontSystem font, params string[] paths) {
            foreach (string path in paths) {
                byte[]? data = OpenData(path, "ttf", "otf");
                if (data is not null)
                    font.AddFont(data);
            }
        }

        public static Reloadable<Texture2D, Texture2DMeta> GetTexturePremul(string path) {
            string id = $"Texture (Premultiplied) (Mipmapped) '{path}'";
            if (TryGet(id, out Reloadable<Texture2D, Texture2DMeta>? reloadable))
                return reloadable;

            using Stream? s = OpenStream(path, "png");
            if (s is null)
                return Get(id, default(Texture2DMeta), () => default(Texture2D));

            IntPtr ptr = FNA3D_ReadImageStream(s, out int w, out int h, out int len);
            if (ptr == IntPtr.Zero)
                return Get(id, default(Texture2DMeta), () => default(Texture2D));

            return Get(id, new Texture2DMeta(w, h, () => {
                using Stream? s = OpenStream(path, "png");
                if (s is null)
                    return null;

                IntPtr ptr = FNA3D_ReadImageStream(s, out int w, out int h, out int len);
                if (ptr == IntPtr.Zero)
                    return null;

                Color[] data = new Color[w * h];
                fixed (Color* dataPtr = data)
                    Unsafe.CopyBlock(dataPtr, (void*) ptr, (uint) len);

                FNA3D_Image_Free(ptr);
                return data;
            }), () => {
                if (ptr == IntPtr.Zero)
                    return OpenTexturePremul(path);

                Texture2D tex = OpenTextureData(ptr, w, h, len);
                FNA3D_Image_Free(ptr);
                ptr = IntPtr.Zero;
                return tex;
            });
        }

        public static Reloadable<Texture2D, Texture2DMeta> GetTexturePremulUnmipped(string path) {
            string id = $"Texture (Premultiplied) (Un-mipmapped) '{path}'";
            if (TryGet(id, out Reloadable<Texture2D, Texture2DMeta>? reloadable))
                return reloadable;

            using Stream? s = OpenStream(path, "png");
            if (s is null)
                return Get(id, default(Texture2DMeta), () => default(Texture2D));

            IntPtr ptr = FNA3D_ReadImageStream(s, out int w, out int h, out int len);
            if (ptr == IntPtr.Zero)
                return Get(id, default(Texture2DMeta), () => default(Texture2D));

            return Get(id, new Texture2DMeta(w, h, () => {
                using Stream? s = OpenStream(path, "png");
                if (s is null)
                    return null;

                IntPtr ptr = FNA3D_ReadImageStream(s, out int w, out int h, out int len);
                if (ptr == IntPtr.Zero)
                    return null;

                Color[] data = new Color[w * h];
                fixed (Color* dataPtr = data)
                    Unsafe.CopyBlock(dataPtr, (void*) ptr, (uint) len);

                FNA3D_Image_Free(ptr);
                return data;
            }), () => {
                if (ptr == IntPtr.Zero)
                    return OpenTexturePremulUnmipped(path);

                Texture2D tex = OpenTextureDataUnmipped(ptr, w, h, len);
                FNA3D_Image_Free(ptr);
                ptr = IntPtr.Zero;
                return tex;
            });
        }

        public static Texture2D? OpenTexturePremul(string path) {
            using Stream? s = OpenStream(path, "png");
            if (s is null)
                return null;
            return OpenTexturePremul(s);
        }
        public static Texture2D OpenTexturePremul(Stream s) {
            IntPtr ptr = FNA3D_ReadImageStream(s, out int w, out int h, out int len);
            Texture2D tex = OpenTextureData(ptr, w, h, len);
            FNA3D_Image_Free(ptr);
            return tex;
        }

        public static Texture2D? OpenTexturePremulUnmipped(string path) {
            using Stream? s = OpenStream(path, "png");
            if (s is null)
                return null;
            return OpenTexturePremulUnmipped(s);
        }
        public static Texture2D OpenTexturePremulUnmipped(Stream s) {
            IntPtr ptr = FNA3D_ReadImageStream(s, out int w, out int h, out int len);
            Texture2D tex = OpenTextureDataUnmipped(ptr, w, h, len);
            FNA3D_Image_Free(ptr);
            return tex;
        }

        public static Reloadable<Texture2D, Texture2DMeta> GetTexture(string path) {
            string id = $"Texture (Non-Premultiplied) (Mipmapped) '{path}'";
            if (TryGet(id, out Reloadable<Texture2D, Texture2DMeta>? reloadable))
                return reloadable;

            using Stream? s = OpenStream(path, "png");
            if (s is null)
                return Get(id, default(Texture2DMeta), () => default(Texture2D));

            IntPtr ptr = FNA3D_ReadImageStream(s, out int w, out int h, out int len);
            if (ptr == IntPtr.Zero)
                return Get(id, default(Texture2DMeta), () => default(Texture2D));

            PremultiplyTextureData(ptr, w, h, len);

            return Get(id, new Texture2DMeta(w, h, () => {
                using Stream? s = OpenStream(path, "png");
                if (s is null)
                    return null;

                IntPtr ptr = FNA3D_ReadImageStream(s, out int w, out int h, out int len);
                if (ptr == IntPtr.Zero)
                    return null;

                PremultiplyTextureData(ptr, w, h, len);

                Color[] data = new Color[w * h];
                fixed (Color* dataPtr = data)
                    Unsafe.CopyBlock(dataPtr, (void*) ptr, (uint) len);

                FNA3D_Image_Free(ptr);
                return data;
            }), () => {
                if (ptr == IntPtr.Zero)
                    return OpenTexture(path);

                Texture2D tex = OpenTextureData(ptr, w, h, len);
                FNA3D_Image_Free(ptr);
                ptr = IntPtr.Zero;
                return tex;
            });
        }

        public static Reloadable<Texture2D, Texture2DMeta> GetTextureUnmipped(string path) {
            string id = $"Texture (Non-Premultiplied) (Un-mipmapped) '{path}'";
            if (TryGet(id, out Reloadable<Texture2D, Texture2DMeta>? reloadable))
                return reloadable;

            using Stream? s = OpenStream(path, "png");
            if (s is null)
                return Get(id, default(Texture2DMeta), () => default(Texture2D));

            IntPtr ptr = FNA3D_ReadImageStream(s, out int w, out int h, out int len);
            if (ptr == IntPtr.Zero)
                return Get(id, default(Texture2DMeta), () => default(Texture2D));

            PremultiplyTextureData(ptr, w, h, len);

            return Get(id, new Texture2DMeta(w, h, () => {
                using Stream? s = OpenStream(path, "png");
                if (s is null)
                    return null;

                IntPtr ptr = FNA3D_ReadImageStream(s, out int w, out int h, out int len);
                if (ptr == IntPtr.Zero)
                    return null;

                PremultiplyTextureData(ptr, w, h, len);

                Color[] data = new Color[w * h];
                fixed (Color* dataPtr = data)
                    Unsafe.CopyBlock(dataPtr, (void*) ptr, (uint) len);

                FNA3D_Image_Free(ptr);
                return data;
            }), () => {
                if (ptr == IntPtr.Zero)
                    return OpenTextureUnmipped(path);

                Texture2D tex = OpenTextureDataUnmipped(ptr, w, h, len);
                FNA3D_Image_Free(ptr);
                ptr = IntPtr.Zero;
                return tex;
            });
        }

        public static Texture2D? OpenTexture(string path) {
            using Stream? s = OpenStream(path, "png");
            if (s is null)
                return null;
            return OpenTexture(s);
        }
        public static Texture2D OpenTexture(Stream s) {
            IntPtr ptr = FNA3D_ReadImageStream(s, out int w, out int h, out int len);
            PremultiplyTextureData(ptr, w, h, len);
            Texture2D tex = OpenTextureData(ptr, w, h, len);
            FNA3D_Image_Free(ptr);
            return tex;
        }

        public static Texture2D? OpenTextureUnmipped(string path) {
            using Stream? s = OpenStream(path, "png");
            if (s is null)
                return null;
            return OpenTextureUnmipped(s);
        }
        public static Texture2D OpenTextureUnmipped(Stream s) {
            IntPtr ptr = FNA3D_ReadImageStream(s, out int w, out int h, out int len);
            PremultiplyTextureData(ptr, w, h, len);
            Texture2D tex = OpenTextureDataUnmipped(ptr, w, h, len);
            FNA3D_Image_Free(ptr);
            return tex;
        }

        public static void PremultiplyTextureData(IntPtr ptr, int w, int h, int len) {
            byte* raw = (byte*) ptr;
            for (int i = len - 1 - 3; i > -1; i -= 4) {
                byte a = raw[i + 3];
                if (a == 0 || a == 255)
                    continue;
                raw[i + 0] = (byte) (raw[i + 0] * a / 255D);
                raw[i + 1] = (byte) (raw[i + 1] * a / 255D);
                raw[i + 2] = (byte) (raw[i + 2] * a / 255D);
                // raw[i + 3] = a;
            }
        }

        public static Texture2D OpenTextureDataUnmipped(IntPtr ptr, int w, int h, int len) {
            Texture2D texRaw = new(UI.Game.GraphicsDevice, w, h, false, SurfaceFormat.Color);
            texRaw.SetDataPointerEXT(0, null, ptr, len);
            return texRaw;
        }

        public static Texture2D OpenTextureData(IntPtr ptr, int w, int h, int len) {
            // Mipmaps are pain.

            Game g = UI.Game;
            GraphicsDevice gd = g.GraphicsDevice;
            GraphicsStateSnapshot gss = new(gd);

            using Texture2D texRaw = OpenTextureDataUnmipped(ptr, w, h, len);

            RenderTarget2D rt = new(gd, w, h, true, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.DiscardContents);
            gd.SetRenderTarget(rt);
            using BasicMesh mesh = new(g) {
                Shapes = {
                    new MeshShapes.Quad() {
                        XY1 = new(w * 0f, h * 0f),
                        XY2 = new(w * 1f, h * 0f),
                        XY3 = new(w * 0f, h * 1f),
                        XY4 = new(w * 1f, h * 1f),
                    },
                },
                MSAA = false,
                Texture = Reloadable.Temporary(new Texture2DMeta(w, h, null), () => texRaw, false),
                DisposeTexture = true,
                BlendState = BlendState.Opaque,
            };
            mesh.Draw();

            gss.Apply();
            return rt;
        }

    }

    public sealed class TrackedIntPtr : IDisposable {

        public IntPtr Value;
        public Action<IntPtr> Unloader;
        public bool IsValid;

        public TrackedIntPtr(IntPtr value, Action<IntPtr> unloader) {
            Value = value;
            Unloader = unloader;
            IsValid = true;
        }

        ~TrackedIntPtr() {
            Dispose(false);
        }

        public void Dispose() {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing) {
            if (!IsValid)
                return;
            IsValid = false;
            Unloader(Value);
        }

        public static implicit operator IntPtr(TrackedIntPtr tptr) => tptr.Value;

    }
}
