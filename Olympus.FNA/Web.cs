using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json;
using OlympUI;
using System;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Olympus {
    public class Web : IDisposable {

        public readonly App App;

        public HttpClient Client = new();
        public JsonSerializer JSON = new();

        public Web(App app) {
            App = app;
        }

        public void Dispose() {
            Client.Dispose();
        }

        public async Task<T?> GetJSON<T>(string url) {
            using Stream s = await Client.GetStreamAsync(url);
            using StreamReader sr = new(s);
            using JsonTextReader jtr = new(sr);
            return JSON.Deserialize<T>(jtr);
        }

        public async Task<(byte[]? dataCompressed, byte[]? dataRaw, int w, int h)> GetTextureData(string url) {
            byte[] data;
            int w, h, len;
            IntPtr ptr;
            byte[] dataCompressed;

            try {
                dataCompressed = await Client.GetByteArrayAsync(url);
            } catch (Exception e) {
                AppLogger.Log.Error($"Failed to download texture data \"{url}\":\n{e}");
                return (null, null, 0, 0);
            }

            if (dataCompressed.Length == 0)
                return (null, null, 0, 0);

            using (MemoryStream ms = new(dataCompressed))
                ptr = OlympUI.Assets.FNA3D_ReadImageStream(ms, out w, out h, out len);

            OlympUI.Assets.PremultiplyTextureData(ptr, w, h, len);

            unsafe {
                data = new byte[len];
                using MemoryStream ms = new(data);
                ms.Write(new ReadOnlySpan<byte>((void*) ptr, len));
            }

            OlympUI.Assets.FNA3D_Image_Free(ptr);

            return (dataCompressed, data, w, h);
        }

        public async Task<IReloadable<Texture2D, Texture2DMeta>?> GetTexture(string url) {
            // TODO: Preserve downloaded textures on disk instead of in RAM.
            (byte[]? dataCompressed, byte[]? dataRaw, int w, int h) = await GetTextureData(url);

            if (dataCompressed is null || dataRaw is null || dataRaw.Length == 0)
                return null;

            return App.MarkTemporary(Texture2DMeta.Reloadable($"Texture (Web) (Mipmapped) '{url}'", w, h, () => {
                unsafe {
                    Color[] data = new Color[w * h];

                    // TODO: Cache and unload dataRaw after timeout.

                    if (dataRaw is not null) {
                        fixed (byte* dataRawPtr = dataRaw)
                        fixed (Color* dataPtr = data)
                            Unsafe.CopyBlock(dataPtr, dataRawPtr, (uint) dataRaw.Length);
                        dataRaw = null;
                        return data;
                    }

                    IntPtr ptr;
                    int len;
                    using (MemoryStream ms = new(dataCompressed))
                        ptr = OlympUI.Assets.FNA3D_ReadImageStream(ms, out _, out _, out len);

                    OlympUI.Assets.PremultiplyTextureData(ptr, w, h, len);

                    fixed (Color* dataPtr = data)
                        Unsafe.CopyBlock(dataPtr, (void*) ptr, (uint) len);

                    OlympUI.Assets.FNA3D_Image_Free(ptr);
                    return data;
                }
            }));
        }

        public async Task<IReloadable<Texture2D, Texture2DMeta>?> GetTextureUnmipped(string url) {
            // TODO: Preserve downloaded textures on disk instead of in RAM.
            (byte[]? dataCompressed, byte[]? dataRaw, int w, int h) = await GetTextureData(url);

            if (dataCompressed is null || dataRaw is null || dataRaw.Length == 0)
                return null;

            return App.MarkTemporary(Texture2DMeta.Reloadable($"Texture (Web) (Unmipmapped) '{url}'", w, h, () => {
                unsafe {
                    Color[] data = new Color[w * h];

                    // TODO: Cache and unload dataRaw after timeout.

                    if (dataRaw is not null) {
                        fixed (byte* dataRawPtr = dataRaw)
                        fixed (Color* dataPtr = data)
                            Unsafe.CopyBlock(dataPtr, dataRawPtr, (uint) dataRaw.Length);
                        dataRaw = null;
                        return data;
                    }

                    IntPtr ptr;
                    int len;
                    using (MemoryStream ms = new(dataCompressed))
                        ptr = OlympUI.Assets.FNA3D_ReadImageStream(ms, out _, out _, out len);

                    OlympUI.Assets.PremultiplyTextureData(ptr, w, h, len);

                    fixed (Color* dataPtr = data)
                        Unsafe.CopyBlock(dataPtr, (void*) ptr, (uint) len);

                    OlympUI.Assets.FNA3D_Image_Free(ptr);
                    return data;
                }
            }));
        }

    }
}
