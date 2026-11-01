using System.Collections.Generic;
using UnityEngine;

namespace Viol3.Colorizer
{
    // Shows the edited texture through property blocks, which leave materials and scenes untouched.
    public class MaterialPreview
    {
        readonly Dictionary<(Renderer, int), MaterialPropertyBlock> originals = new Dictionary<(Renderer, int), MaterialPropertyBlock>();

        public void Apply(List<MaterialSlot> slots, Texture2D texture)
        {
            foreach (MaterialSlot slot in slots)
            {
                if (slot.Renderer == null)
                    continue;

                var key = (slot.Renderer, slot.Index);
                if (!originals.ContainsKey(key))
                {
                    var original = new MaterialPropertyBlock();
                    slot.Renderer.GetPropertyBlock(original, slot.Index);
                    originals.Add(key, original);
                }

                var block = new MaterialPropertyBlock();
                slot.Renderer.GetPropertyBlock(block, slot.Index);
                block.SetTexture(slot.Property, texture);
                slot.Renderer.SetPropertyBlock(block, slot.Index);
            }
        }

        public void Clear()
        {
            foreach (KeyValuePair<(Renderer, int), MaterialPropertyBlock> entry in originals)
            {
                Renderer renderer = entry.Key.Item1;
                if (renderer != null)
                    renderer.SetPropertyBlock(entry.Value.isEmpty ? null : entry.Value, entry.Key.Item2);
            }
            originals.Clear();
        }
    }
}
