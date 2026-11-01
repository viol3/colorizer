using System.Collections.Generic;
using UnityEngine;

namespace Viol3.Colorizer
{
    public static class MeshUvSampler
    {
        // Counts how many atlas pixels each region gets from the UVs of the given slots.
        // Returns false when a mesh could not be read, so the usage data is incomplete.
        public static bool MarkUsage(AtlasData atlas, List<MaterialSlot> slots)
        {
            bool complete = true;
            var visited = new HashSet<(Mesh, int)>();
            var uvs = new List<Vector2>();

            foreach (MaterialSlot slot in slots)
            {
                Mesh mesh = GetMesh(slot.Renderer);
                if (mesh == null)
                    continue;
                if (!mesh.isReadable)
                {
                    complete = false;
                    continue;
                }

                int submesh = Mathf.Min(slot.Index, mesh.subMeshCount - 1);
                if (submesh < 0 || !visited.Add((mesh, submesh)) || mesh.GetTopology(submesh) != MeshTopology.Triangles)
                    continue;

                mesh.GetUVs(0, uvs);
                if (uvs.Count == 0)
                    continue;

                int[] triangles = mesh.GetTriangles(submesh);
                var scale = new Vector2(atlas.Width, atlas.Height);
                for (int i = 0; i + 2 < triangles.Length; i += 3)
                {
                    Rasterize(
                        atlas,
                        Vector2.Scale(uvs[triangles[i]], scale),
                        Vector2.Scale(uvs[triangles[i + 1]], scale),
                        Vector2.Scale(uvs[triangles[i + 2]], scale));
                }
            }

            return complete;
        }

        // Finds the region under a world ray by intersecting the slot meshes on the CPU.
        public static int PickRegion(AtlasData atlas, List<MaterialSlot> slots, Ray ray)
        {
            int region = -1;
            float closest = float.MaxValue;
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();

            foreach (MaterialSlot slot in slots)
            {
                Renderer renderer = slot.Renderer;
                if (renderer == null || !renderer.bounds.IntersectRay(ray))
                    continue;

                Mesh source = GetMesh(renderer);
                if (source == null || !source.isReadable)
                    continue;

                int submesh = Mathf.Min(slot.Index, source.subMeshCount - 1);
                if (submesh < 0 || source.GetTopology(submesh) != MeshTopology.Triangles)
                    continue;

                Mesh baked = null;
                if (renderer is SkinnedMeshRenderer skinned)
                {
                    baked = new Mesh();
                    skinned.BakeMesh(baked, true);
                }

                Mesh mesh = baked != null ? baked : source;
                mesh.GetVertices(vertices);
                source.GetUVs(0, uvs);
                int[] triangles = source.GetTriangles(submesh);

                // The direction stays unnormalized, so hit distances remain in world units across renderers.
                Matrix4x4 toLocal = renderer.transform.worldToLocalMatrix;
                Vector3 origin = toLocal.MultiplyPoint3x4(ray.origin);
                Vector3 direction = toLocal.MultiplyVector(ray.direction);

                if (uvs.Count == vertices.Count)
                {
                    for (int i = 0; i + 2 < triangles.Length; i += 3)
                    {
                        int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                        if (!Intersect(origin, direction, vertices[a], vertices[b], vertices[c], out float t, out float u, out float v) || t >= closest)
                            continue;

                        Vector2 uv = uvs[a] * (1f - u - v) + uvs[b] * u + uvs[c] * v;
                        closest = t;
                        region = atlas.RegionMap[PixelIndex(atlas, Mathf.FloorToInt(uv.x * atlas.Width), Mathf.FloorToInt(uv.y * atlas.Height))];
                    }
                }

                if (baked != null)
                    Object.DestroyImmediate(baked);
            }

            return region;
        }

        static Mesh GetMesh(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skinned)
                return skinned.sharedMesh;
            return renderer.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;
        }

        static void Rasterize(AtlasData atlas, Vector2 a, Vector2 b, Vector2 c)
        {
            // Atlas models often collapse a whole face onto a point, which has no area to scan.
            Vector2 center = (a + b + c) / 3f;
            Mark(atlas, Mathf.FloorToInt(center.x), Mathf.FloorToInt(center.y));

            float area = Edge(a, b, c);
            if (Mathf.Abs(area) < 0.0001f)
                return;

            int minX = Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x)));
            int minY = Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y)));
            int maxX = Mathf.Min(Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))), minX + atlas.Width - 1);
            int maxY = Mathf.Min(Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))), minY + atlas.Height - 1);
            float sign = Mathf.Sign(area);

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    var point = new Vector2(x + 0.5f, y + 0.5f);
                    if (Edge(a, b, point) * sign >= 0f && Edge(b, c, point) * sign >= 0f && Edge(c, a, point) * sign >= 0f)
                        Mark(atlas, x, y);
                }
            }
        }

        static void Mark(AtlasData atlas, int x, int y)
        {
            atlas.Regions[atlas.RegionMap[PixelIndex(atlas, x, y)]].UsedPixels++;
        }

        // UVs outside 0-1 wrap around, matching a repeating texture.
        static int PixelIndex(AtlasData atlas, int x, int y)
        {
            int wrappedX = (x % atlas.Width + atlas.Width) % atlas.Width;
            int wrappedY = (y % atlas.Height + atlas.Height) % atlas.Height;
            return wrappedY * atlas.Width + wrappedX;
        }

        static float Edge(Vector2 a, Vector2 b, Vector2 point)
        {
            return (b.x - a.x) * (point.y - a.y) - (b.y - a.y) * (point.x - a.x);
        }

        static bool Intersect(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c, out float t, out float u, out float v)
        {
            t = u = v = 0f;
            Vector3 edge1 = b - a;
            Vector3 edge2 = c - a;
            Vector3 p = Vector3.Cross(direction, edge2);
            float determinant = Vector3.Dot(edge1, p);
            if (Mathf.Abs(determinant) < 1e-9f)
                return false;

            float inverse = 1f / determinant;
            Vector3 s = origin - a;
            u = Vector3.Dot(s, p) * inverse;
            if (u < 0f || u > 1f)
                return false;

            Vector3 q = Vector3.Cross(s, edge1);
            v = Vector3.Dot(direction, q) * inverse;
            if (v < 0f || u + v > 1f)
                return false;

            t = Vector3.Dot(edge2, q) * inverse;
            return t > 0f;
        }
    }
}
