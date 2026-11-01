using System.Collections.Generic;
using UnityEngine;

namespace Viol3.Colorizer
{
    public class MaterialSlot
    {
        public Renderer Renderer;
        public int Index;
        public Material Material;
        public string Property;
    }

    // Every material slot under a target that shares one atlas texture.
    public class TextureGroup
    {
        public Texture2D Texture;
        public readonly List<MaterialSlot> Slots = new List<MaterialSlot>();

        public static List<TextureGroup> Collect(GameObject target)
        {
            var groups = new List<TextureGroup>();
            if (target == null)
                return groups;

            foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>(true))
            {
                if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer))
                    continue;

                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    Material material = materials[i];
                    if (material == null)
                        continue;

                    string property = FindTextureProperty(material);
                    if (property == null || !(material.GetTexture(property) is Texture2D texture))
                        continue;

                    TextureGroup group = groups.Find(g => g.Texture == texture);
                    if (group == null)
                    {
                        group = new TextureGroup { Texture = texture };
                        groups.Add(group);
                    }

                    group.Slots.Add(new MaterialSlot
                    {
                        Renderer = renderer,
                        Index = i,
                        Material = material,
                        Property = property,
                    });
                }
            }

            return groups;
        }

        static string FindTextureProperty(Material material)
        {
            Shader shader = material.shader;
            for (int i = 0; i < shader.GetPropertyCount(); i++)
            {
                if ((shader.GetPropertyFlags(i) & UnityEngine.Rendering.ShaderPropertyFlags.MainTexture) != 0)
                    return shader.GetPropertyName(i);
            }

            if (material.HasProperty("_BaseMap"))
                return "_BaseMap";
            if (material.HasProperty("_MainTex"))
                return "_MainTex";
            return null;
        }
    }
}
