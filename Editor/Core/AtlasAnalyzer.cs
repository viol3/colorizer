using System.Collections.Generic;
using UnityEngine;

namespace Viol3.Colorizer
{
    public class AtlasRegion
    {
        public Color32 BaseColor;
        public int PixelCount;
        public int UsedPixels;
        public int MinX;
        public int MinY;
        public int MaxX;
        public int MaxY;
    }

    public class AtlasData
    {
        public int Width;
        public int Height;
        public Color32[] Pixels;
        public int[] RegionMap;
        public List<AtlasRegion> Regions;
    }

    public static class AtlasAnalyzer
    {
        // Groups connected pixels whose channels stay within tolerance of the region's first pixel.
        // Comparing against the first pixel keeps a soft gradient from leaking across a whole texture.
        public static AtlasData Build(Color32[] pixels, int width, int height, int tolerance)
        {
            var map = new int[pixels.Length];
            for (int i = 0; i < map.Length; i++)
                map[i] = -1;

            var regions = new List<AtlasRegion>();
            var stack = new int[pixels.Length];

            for (int start = 0; start < pixels.Length; start++)
            {
                if (map[start] >= 0)
                    continue;

                int id = regions.Count;
                Color32 seed = pixels[start];
                long r = 0, g = 0, b = 0, a = 0;
                var region = new AtlasRegion
                {
                    MinX = width,
                    MinY = height,
                    MaxX = -1,
                    MaxY = -1,
                };

                int top = 0;
                stack[top++] = start;
                map[start] = id;

                while (top > 0)
                {
                    int index = stack[--top];
                    int x = index % width;
                    int y = index / width;
                    Color32 pixel = pixels[index];

                    r += pixel.r;
                    g += pixel.g;
                    b += pixel.b;
                    a += pixel.a;
                    region.PixelCount++;
                    if (x < region.MinX) region.MinX = x;
                    if (y < region.MinY) region.MinY = y;
                    if (x > region.MaxX) region.MaxX = x;
                    if (y > region.MaxY) region.MaxY = y;

                    if (x > 0) Visit(index - 1);
                    if (x < width - 1) Visit(index + 1);
                    if (y > 0) Visit(index - width);
                    if (y < height - 1) Visit(index + width);
                }

                int count = region.PixelCount;
                region.BaseColor = new Color32(Average(r, count), Average(g, count), Average(b, count), Average(a, count));
                regions.Add(region);

                void Visit(int neighbor)
                {
                    if (map[neighbor] >= 0 || !IsClose(pixels[neighbor], seed, tolerance))
                        return;
                    map[neighbor] = id;
                    stack[top++] = neighbor;
                }
            }

            return new AtlasData
            {
                Width = width,
                Height = height,
                Pixels = pixels,
                RegionMap = map,
                Regions = regions,
            };
        }

        // Moves each pixel by its region's color change, so small differences inside a region survive.
        public static void Recolor(AtlasData atlas, IList<Color32> colors, Color32[] output)
        {
            int regionCount = atlas.Regions.Count;
            var deltaR = new int[regionCount];
            var deltaG = new int[regionCount];
            var deltaB = new int[regionCount];
            for (int i = 0; i < regionCount; i++)
            {
                Color32 from = atlas.Regions[i].BaseColor;
                Color32 to = colors[i];
                deltaR[i] = to.r - from.r;
                deltaG[i] = to.g - from.g;
                deltaB[i] = to.b - from.b;
            }

            Color32[] pixels = atlas.Pixels;
            int[] map = atlas.RegionMap;
            for (int i = 0; i < pixels.Length; i++)
            {
                int id = map[i];
                Color32 pixel = pixels[i];
                output[i] = new Color32(
                    Clamp(pixel.r + deltaR[id]),
                    Clamp(pixel.g + deltaG[id]),
                    Clamp(pixel.b + deltaB[id]),
                    pixel.a);
            }
        }

        public static bool SameColor(Color32 a, Color32 b)
        {
            return a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
        }

        static bool IsClose(Color32 a, Color32 b, int tolerance)
        {
            return Diff(a.r, b.r) <= tolerance
                && Diff(a.g, b.g) <= tolerance
                && Diff(a.b, b.b) <= tolerance
                && Diff(a.a, b.a) <= tolerance;
        }

        static int Diff(byte a, byte b)
        {
            return a > b ? a - b : b - a;
        }

        static byte Average(long sum, int count)
        {
            return (byte)((sum + count / 2) / count);
        }

        static byte Clamp(int value)
        {
            return value < 0 ? (byte)0 : value > 255 ? (byte)255 : (byte)value;
        }
    }
}
