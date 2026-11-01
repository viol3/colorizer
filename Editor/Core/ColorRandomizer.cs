using System.Collections.Generic;
using UnityEngine;

namespace Viol3.Colorizer
{
    public static class ColorRandomizer
    {
        // Rotating every hue by the same amount keeps the relationship between the colors.
        public static void ShiftHue(AtlasData atlas, IList<Color32> colors, IList<int> regions)
        {
            float shift = Random.Range(0.1f, 0.9f);
            foreach (int region in regions)
            {
                Color.RGBToHSV(colors[region], out float h, out float s, out float v);
                Set(atlas, colors, region, Color.HSVToRGB(Mathf.Repeat(h + shift, 1f), s, v));
            }
        }

        public static void Randomize(AtlasData atlas, IList<Color32> colors, IList<int> regions, float minSaturation, float maxSaturation, float minValue, float maxValue)
        {
            foreach (int region in regions)
            {
                Set(atlas, colors, region, Color.HSVToRGB(
                    Random.value,
                    Random.Range(minSaturation, maxSaturation),
                    Random.Range(minValue, maxValue)));
            }
        }

        // Dark regions take dark palette colors and bright regions take bright ones, keeping the model readable.
        public static void MapByBrightness(AtlasData atlas, IList<Color32> colors, IList<int> regions, IList<Color> palette)
        {
            if (palette.Count == 0 || regions.Count == 0)
                return;

            var sortedRegions = new List<int>(regions);
            sortedRegions.Sort((a, b) => Brightness(atlas.Regions[a].BaseColor).CompareTo(Brightness(atlas.Regions[b].BaseColor)));
            var sortedPalette = new List<Color>(palette);
            sortedPalette.Sort((a, b) => Brightness(a).CompareTo(Brightness(b)));

            for (int i = 0; i < sortedRegions.Count; i++)
            {
                float position = sortedRegions.Count > 1 ? (float)i / (sortedRegions.Count - 1) : 0.5f;
                Set(atlas, colors, sortedRegions[i], sortedPalette[Mathf.RoundToInt(position * (sortedPalette.Count - 1))]);
            }
        }

        // Deals the palette like a deck, so colors only repeat once all of them have been used.
        public static void ShufflePalette(AtlasData atlas, IList<Color32> colors, IList<int> regions, IList<Color> palette)
        {
            if (palette.Count == 0)
                return;

            var deck = new List<Color>();
            foreach (int region in regions)
            {
                if (deck.Count == 0)
                    deck.AddRange(palette);

                int pick = Random.Range(0, deck.Count);
                Set(atlas, colors, region, deck[pick]);
                deck.RemoveAt(pick);
            }
        }

        public static void Set(AtlasData atlas, IList<Color32> colors, int region, Color color)
        {
            Color32 value = color;
            value.a = atlas.Regions[region].BaseColor.a;
            colors[region] = value;
        }

        static float Brightness(Color color)
        {
            return color.r * 0.299f + color.g * 0.587f + color.b * 0.114f;
        }
    }
}
