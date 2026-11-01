using System.Collections.Generic;
using UnityEngine;

namespace Viol3.Colorizer
{
    [CreateAssetMenu(fileName = "ColorPalette", menuName = "Viol3's Colorizer/Color Palette")]
    public class ColorPalette : ScriptableObject
    {
        public List<Color> colors = new List<Color> { Color.white, Color.gray, Color.black };
    }

    public static class BuiltInPalettes
    {
        public static readonly string[] Names = { "Earth", "Forest", "Ocean", "Autumn", "Desert", "Pastel", "Neon", "Retro" };

        static readonly string[][] Hex =
        {
            new[] { "2B2118", "4A3525", "6F4E37", "8C6A4A", "A98B65", "C7AE86", "6B7043", "E3D5B8" },
            new[] { "14281D", "23422F", "355E3B", "4F7942", "6E8F4E", "8FA65A", "5C4632", "C2C98A" },
            new[] { "0B2545", "13315C", "1F4E79", "2E7DA6", "3FA7C4", "7FD1D8", "C9EDEA", "F2E8CF" },
            new[] { "3B1F14", "6B2D16", "9C3F15", "C7591A", "DD8A1F", "E8B23A", "7A5230", "F1DDA8" },
            new[] { "4A2C1D", "7D4A2B", "A8683A", "C98B52", "DDAE76", "EBCFA0", "5F7F86", "F5E9D0" },
            new[] { "A5B4CB", "B5D3C8", "C8E0B8", "F1E3A6", "F6C9A4", "F2AFB2", "D2B7E0", "F8F4EC" },
            new[] { "0D0221", "26408B", "0FF0FC", "39FF14", "F5F500", "FF6B00", "FF2079", "B026FF" },
            new[] { "1A1C2C", "5D275D", "B13E53", "EF7D57", "FFCD75", "A7F070", "38B764", "257179", "29366F", "3B5DC9", "41A6F6", "73EFF7", "F4F4F4", "94B0C2", "566C86", "333C57" },
        };

        public static List<Color> Get(int index)
        {
            var colors = new List<Color>();
            foreach (string hex in Hex[index])
            {
                if (ColorUtility.TryParseHtmlString("#" + hex, out Color color))
                    colors.Add(color);
            }
            return colors;
        }
    }
}
