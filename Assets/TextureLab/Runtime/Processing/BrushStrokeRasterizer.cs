using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TextureLab.Runtime
{
    internal sealed class BrushStrokeRasterizer : IDisposable
    {
        private readonly Material material;
        private readonly Mesh mesh;
        private readonly List<Vector3> vertices = new();
        private readonly List<Vector2> uvs = new();
        private readonly List<Color> colors = new();
        private readonly List<int> triangles = new();

        internal BrushStrokeRasterizer(Shader shader)
        {
            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            mesh = new Mesh
            {
                name = "Texture Lab Brush Strokes",
                hideFlags = HideFlags.HideAndDontSave,
                indexFormat = IndexFormat.UInt32
            };
        }

        internal void Rasterize(RenderTexture target, ExposureBrushEffectData effect, ProcessingContext context)
        {
            vertices.Clear();
            uvs.Clear();
            colors.Clear();
            triangles.Clear();

            foreach (BrushStroke stroke in effect.Strokes)
            {
                float radiusX = Mathf.Max(0.5f, stroke.Size * 0.5f) / Mathf.Max(1, context.OriginalWidth);
                float radiusY = Mathf.Max(0.5f, stroke.Size * 0.5f) / Mathf.Max(1, context.OriginalHeight);
                float exposure = stroke.Mode == ExposureBrushMode.Lighten ? stroke.Exposure : -stroke.Exposure;
                foreach (Vector2 point in stroke.Points)
                    AddWrappedDab(point, radiusX, radiusY, stroke.Hardness, exposure, effect.Wrap == OffsetWrapMode.Repeat);
            }

            RenderTexture previous = RenderTexture.active;
            try
            {
                Graphics.SetRenderTarget(target);
                GL.Clear(false, true, Color.clear);
                if (vertices.Count == 0)
                    return;

                mesh.Clear();
                mesh.SetVertices(vertices);
                mesh.SetUVs(0, uvs);
                mesh.SetColors(colors);
                mesh.SetTriangles(triangles, 0, true);
                material.SetPass(0);
                Graphics.DrawMeshNow(mesh, Matrix4x4.identity);
            }
            finally
            {
                RenderTexture.active = previous;
            }
        }

        public void Dispose()
        {
            RuntimeObjectLifetime.Destroy(material);
            RuntimeObjectLifetime.Destroy(mesh);
        }

        private void AddWrappedDab(Vector2 point, float radiusX, float radiusY, float hardness, float exposure, bool repeat)
        {
            AddDab(point, radiusX, radiusY, hardness, exposure);
            if (!repeat)
                return;

            bool left = point.x - radiusX < 0f;
            bool right = point.x + radiusX > 1f;
            bool bottom = point.y - radiusY < 0f;
            bool top = point.y + radiusY > 1f;
            if (left) AddDab(point + Vector2.right, radiusX, radiusY, hardness, exposure);
            if (right) AddDab(point - Vector2.right, radiusX, radiusY, hardness, exposure);
            if (bottom) AddDab(point + Vector2.up, radiusX, radiusY, hardness, exposure);
            if (top) AddDab(point - Vector2.up, radiusX, radiusY, hardness, exposure);
            if (left && bottom) AddDab(point + Vector2.one, radiusX, radiusY, hardness, exposure);
            if (left && top) AddDab(point + new Vector2(1f, -1f), radiusX, radiusY, hardness, exposure);
            if (right && bottom) AddDab(point + new Vector2(-1f, 1f), radiusX, radiusY, hardness, exposure);
            if (right && top) AddDab(point - Vector2.one, radiusX, radiusY, hardness, exposure);
        }

        private void AddDab(Vector2 point, float radiusX, float radiusY, float hardness, float exposure)
        {
            int index = vertices.Count;
            float centerX = point.x * 2f - 1f;
            float centerY = point.y * 2f - 1f;
            float extentX = radiusX * 2f;
            float extentY = radiusY * 2f;
            Color settings = new(exposure, Mathf.Clamp01(hardness), 0f, 1f);

            vertices.Add(new Vector3(centerX - extentX, centerY - extentY));
            vertices.Add(new Vector3(centerX + extentX, centerY - extentY));
            vertices.Add(new Vector3(centerX + extentX, centerY + extentY));
            vertices.Add(new Vector3(centerX - extentX, centerY + extentY));
            uvs.Add(new Vector2(-1f, -1f));
            uvs.Add(new Vector2(1f, -1f));
            uvs.Add(new Vector2(1f, 1f));
            uvs.Add(new Vector2(-1f, 1f));
            colors.Add(settings);
            colors.Add(settings);
            colors.Add(settings);
            colors.Add(settings);
            triangles.Add(index);
            triangles.Add(index + 1);
            triangles.Add(index + 2);
            triangles.Add(index);
            triangles.Add(index + 2);
            triangles.Add(index + 3);
        }
    }
}
