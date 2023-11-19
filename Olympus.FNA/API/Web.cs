using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json;
using OlympUI;
using System;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Olympus.API {
    // If you're confused about UrlManager and this class, check out UrlManager
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
        
        

        /// <summary>
        /// Saves a stream to a file, calling a callback in the meantime
        /// </summary>
        /// <param name="outputFile">The file to redirect the stream</param>
        /// <param name="stream">The data</param>
        /// <param name="length">The total bytes of data</param>
        /// <param name="progressCallback">The callback for the progress update</param>
        public static async Task<bool> Stream2FileWithProgress(string outputFile, Task<Stream> stream, int length, 
                Func<int, int, int, bool> progressCallback) {
            if (File.Exists(outputFile))
                File.Delete(outputFile);
            await using FileStream output = File.OpenWrite(outputFile);

            DateTime timeStart = DateTime.Now;
            await using Stream input = await stream;
            progressCallback(0, length, 0);
            
            byte[] buffer = new byte[4096];
            DateTime timeLastSpeed = timeStart;
            int read = 1;
            int readForSpeed = 0;
            int pos = 0;
            int speed = 0;
            while (read > 0) {
                int count = length > 0 ? (int) Math.Min(buffer.Length, length - pos) : buffer.Length;
                read = await input.ReadAsync(buffer, 0, count);
                output.Write(buffer, 0, read);
                pos += read;
                readForSpeed += read;

                TimeSpan td = DateTime.Now - timeLastSpeed;
                if (td.TotalMilliseconds > 100) {
                    speed = (int) ((readForSpeed / 1024D) / td.TotalSeconds);
                    readForSpeed = 0;
                    timeLastSpeed = DateTime.Now;
                }

                if (!progressCallback(pos, length, speed)) {
                    return false;
                }
            }

            progressCallback(pos, length, speed);

            return true;
        }

        // The following is copied and adapted from Everest (Everest.Updater.cs:510)

        /// <summary>
        /// Downloads a file and calls the progressCallback parameter periodically with progress information.
        /// This can be used to display the download progress on screen.
        /// </summary>
        /// <param name="url">The URL to download the file from</param>
        /// <param name="destPath">The path the file should be downloaded to</param>
        /// <param name="progressCallback">A method called periodically as the download progresses. Parameters are progress, length and speed in KiB/s.
        /// Should return true for the download to continue, false for it to be cancelled.</param>
        public static async Task<bool> DownloadFileWithProgress(string url, string destPath, Func<int, long, int, bool> progressCallback) {
            DateTime timeStart = DateTime.Now;

            if (File.Exists(destPath))
                File.Delete(destPath);

            HttpClient request =  new();
            request.Timeout = TimeSpan.FromMilliseconds(10000);
//             // disable IPv6 for this request, as it is known to cause "the request has timed out" issues for some users
            // request.ServicePoint.BindIPEndPointDelegate = delegate (ServicePoint servicePoint, IPEndPoint remoteEndPoint, int retryCount) {
            //     if (remoteEndPoint.AddressFamily != AddressFamily.InterNetwork) {
            //         throw new InvalidOperationException("no IPv4 address");
            //     }
            //     return new IPEndPoint(IPAddress.Any, 0);
            // };

            // Manual buffered copy from web input to file output.
            // Allows us to measure speed and progress.
            using HttpResponseMessage response = await request.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            long length = response.Content.Headers.ContentLength ?? 0;
            await using Stream input = await response.Content.ReadAsStreamAsync();
            await using FileStream output = File.OpenWrite(destPath);
            if (length == 0) 
                AppLogger.Log.Warning("Cannot determine file length!");

            progressCallback(0, length, 0);

            byte[] buffer = new byte[4096];
            DateTime timeLastSpeed = timeStart;
            int read = 1;
            int readForSpeed = 0;
            int pos = 0;
            int speed = 0;
            while (read > 0) {
                int count = length > 0 ? (int) Math.Min(buffer.Length, length - pos) : buffer.Length;
                read = await input.ReadAsync(buffer, 0, count);
                output.Write(buffer, 0, read);
                pos += read;
                readForSpeed += read;

                TimeSpan td = DateTime.Now - timeLastSpeed;
                if (td.TotalMilliseconds > 100) {
                    speed = (int) ((readForSpeed / 1024D) / td.TotalSeconds);
                    readForSpeed = 0;
                    timeLastSpeed = DateTime.Now;
                }

                if (!progressCallback(pos, length, speed)) {
                    return false;
                }
            }

            return true;
        }
    }
}
