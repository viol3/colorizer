using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Viol3.Colorizer
{
    public static class AtlasExporter
    {
        // Returns null when the source file can be replaced, otherwise the reason it can't.
        public static string GetOverwriteProblem(Texture2D source, AtlasData atlas)
        {
            string path = AssetDatabase.GetAssetPath(source);
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension != ".png" && extension != ".tga" && extension != ".jpg" && extension != ".jpeg")
                return "Only PNG, TGA and JPG files can be overwritten. Save a new variant instead.";
            if (!AssetDatabase.IsOpenForEdit(path))
                return "This texture is read-only. Save a new variant instead.";

            // TGA pixels come from the imported texture, so a reduced Max Size would shrink the file.
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.GetSourceTextureWidthAndHeight(out int width, out int height);
                if (width != atlas.Width || height != atlas.Height)
                    return "The imported texture is smaller than its source file. Raise Max Size or save a new variant.";
            }
            return null;
        }

        public static void Overwrite(Texture2D source, Texture2D edited)
        {
            string path = AssetDatabase.GetAssetPath(source);
            File.WriteAllBytes(Path.GetFullPath(path), Encode(edited, Path.GetExtension(path).ToLowerInvariant()));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }

        // Writes the edited PNG beside a copy of each material and points the renderers at the copies.
        public static void SaveVariant(TextureGroup group, Texture2D edited, string texturePath)
        {
            string sourcePath = AssetDatabase.GetAssetPath(group.Texture);
            File.WriteAllBytes(Path.GetFullPath(texturePath), edited.EncodeToPNG());
            AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceSynchronousImport);
            CopyImportSettings(sourcePath, texturePath);
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);

            var variants = new Dictionary<Material, Material>();
            var uniqueMaterials = new HashSet<Material>();
            foreach (MaterialSlot slot in group.Slots)
                uniqueMaterials.Add(slot.Material);

            string folder = Path.GetDirectoryName(texturePath).Replace('\\', '/');
            string baseName = Path.GetFileNameWithoutExtension(texturePath);

            foreach (MaterialSlot slot in group.Slots)
            {
                if (slot.Renderer == null)
                    continue;

                if (!variants.TryGetValue(slot.Material, out Material variant))
                {
                    string name = uniqueMaterials.Count > 1 ? baseName + "_" + slot.Material.name : baseName;
                    variant = new Material(slot.Material) { name = name };
                    variant.SetTexture(slot.Property, texture);
                    AssetDatabase.CreateAsset(variant, AssetDatabase.GenerateUniqueAssetPath(folder + "/" + name + ".mat"));
                    variants.Add(slot.Material, variant);
                }

                Undo.RecordObject(slot.Renderer, "Colorizer Save Variant");
                Material[] materials = slot.Renderer.sharedMaterials;
                materials[slot.Index] = variant;
                slot.Renderer.sharedMaterials = materials;
                PrefabUtility.RecordPrefabInstancePropertyModifications(slot.Renderer);
                EditorUtility.SetDirty(slot.Renderer);
            }

            AssetDatabase.SaveAssets();
        }

        static byte[] Encode(Texture2D texture, string extension)
        {
            switch (extension)
            {
                case ".tga":
                    return texture.EncodeToTGA();
                case ".jpg":
                case ".jpeg":
                    return texture.EncodeToJPG(100);
                default:
                    return texture.EncodeToPNG();
            }
        }

        static void CopyImportSettings(string sourcePath, string targetPath)
        {
            if (!(AssetImporter.GetAtPath(sourcePath) is TextureImporter source) || !(AssetImporter.GetAtPath(targetPath) is TextureImporter target))
                return;

            var settings = new TextureImporterSettings();
            source.ReadTextureSettings(settings);
            target.SetTextureSettings(settings);
            target.maxTextureSize = source.maxTextureSize;
            target.textureCompression = source.textureCompression;
            target.crunchedCompression = source.crunchedCompression;
            target.compressionQuality = source.compressionQuality;
            target.SaveAndReimport();
        }
    }
}
