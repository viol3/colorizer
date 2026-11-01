using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Viol3.Colorizer
{
    public static class AtlasReader
    {
        // Reads the source file when possible: imported textures are often compressed or resized,
        // which smears the flat color cells this tool depends on.
        public static Color32[] Read(Texture2D source, out int width, out int height)
        {
            Color32[] pixels = TryReadSourceFile(source, out width, out height);
            return pixels ?? ReadFromGpu(source, out width, out height);
        }

        static Color32[] TryReadSourceFile(Texture2D source, out int width, out int height)
        {
            width = 0;
            height = 0;

            string path = AssetDatabase.GetAssetPath(source);
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension != ".png" && extension != ".jpg" && extension != ".jpeg")
                return null;

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(Path.GetFullPath(path));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return null;
            }

            var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!decoded.LoadImage(bytes))
                    return null;
                width = decoded.width;
                height = decoded.height;
                return decoded.GetPixels32();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(decoded);
            }
        }

        static Color32[] ReadFromGpu(Texture2D source, out int width, out int height)
        {
            width = source.width;
            height = source.height;

            // Matching the texture's color space keeps the blit from converting the stored values.
            bool srgb = IsSrgb(source);
            RenderTexture target = RenderTexture.GetTemporary(
                width, height, 0, RenderTextureFormat.ARGB32,
                srgb ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear);
            RenderTexture previous = RenderTexture.active;
            var readable = new Texture2D(width, height, TextureFormat.RGBA32, false, !srgb);
            try
            {
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                readable.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                readable.Apply(false);
                return readable.GetPixels32();
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.DestroyImmediate(readable);
            }
        }

        public static bool IsSrgb(Texture2D texture)
        {
            return GraphicsFormatUtility.IsSRGBFormat(texture.graphicsFormat);
        }
    }
}
