using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace OlympUI.MegaCanvas {
    public static class Extensions {

        // Obtains the smallest element that's bigger or equal than the provided width and height
        public static bool TryGetSmallest<T>(this T?[] list, int width, int height, [NotNullWhen(true)] out T? best, out int index) where T : ISizeable {
            best = default;
            index = -1;
            for (int i = list.Length - 1; i >= 0; --i) {
                T? entry = list[i];
                if (entry is not null && !entry.IsDisposed &&
                    width <= entry.Width && height <= entry.Height && (
                        index == -1 || best is null ||
                        (entry.Width < best.Width && entry.Height < best.Height) ||
                        (width < height ? entry.Width <= best.Width : entry.Height <= best.Height)
                )) {
                    best = entry;
                    index = i;
                    if (width == entry.Width && height == entry.Height)
                        return true;
                }
            }
            return index != -1;
        }

        public static bool TryGetSmallest<T>(this List<T> list, int width, int height, [NotNullWhen(true)] out T? best, out int index) where T : ISizeable {
            best = default;
            index = -1;
            for (int i = list.Count - 1; i >= 0; --i) {
                T entry = list[i];
                if (!entry.IsDisposed && width <= entry.Width && height <= entry.Height && (
                        index == -1 || best is null ||
                        (entry.Width < best.Width && entry.Height < best.Height) ||
                        (width < height ? entry.Width <= best.Width : entry.Height <= best.Height)
                )) {
                    best = entry;
                    index = i;
                    if (width == entry.Width && height == entry.Height)
                        return true;
                }
            }
            return index != -1;
        }

        public static T? TryGetNextDisposed<T>(this List<T> list) where T : class, ISizeable {
            return list.FirstOrDefault(entry => entry.IsDisposed);
        }

        public static List<T?> RemoveNulls<T>(this List<T?> list) {
            for (int i = list.Count - 1; i >= 0; --i) {
                T? entry = list[i];
                if (entry is null)
                    list.RemoveAt(i);
            }
            return list;
        }

        public static long GetMemorySize(this Texture2D tex)
            => new Texture2DMeta(tex, null).MemorySize;

        public static long GetMemorySizePoT(this Texture2D tex)
            => new Texture2DMeta(tex, null).MemorySizePoT;

        public static long GetMemoryWaste(this Texture2D tex)
            => new Texture2DMeta(tex, null).MemoryWaste;

        public static int GetArea(this ISizeable size) 
            => size.Width * size.Height;

    }
}
