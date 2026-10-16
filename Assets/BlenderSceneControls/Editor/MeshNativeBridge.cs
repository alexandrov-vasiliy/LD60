using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace BlenderSceneControls
{
    internal static class MeshNativeBridge
    {
        internal static MeshDocument Import(Mesh source)
        {
            if (!source || source.blendShapeCount != 0 || source.bindposes.Length != 0 ||
                source.HasVertexAttribute(VertexAttribute.BlendWeight) || source.HasVertexAttribute(VertexAttribute.BlendIndices))
                throw new InvalidOperationException("Only static meshes without skinning or blend shapes are supported.");
            if (source.vertexCount > 300000 || Enumerable.Range(0, source.subMeshCount).Sum(i => (long)source.GetIndexCount(i)) > 1000000)
                throw new InvalidOperationException("Source mesh exceeds the M02 allocation budget.");
            foreach (var attribute in source.GetVertexAttributes())
            {
                if (attribute.attribute > VertexAttribute.TexCoord7 ||
                    (attribute.format != VertexAttributeFormat.Float32 &&
                     !(attribute.attribute == VertexAttribute.Color && attribute.format == VertexAttributeFormat.UNorm8)))
                    throw new InvalidOperationException("Unsupported vertex attribute format: " + attribute);
                var dimension = attribute.dimension;
                if ((attribute.attribute == VertexAttribute.Position || attribute.attribute == VertexAttribute.Normal) && dimension != 3 ||
                    (attribute.attribute == VertexAttribute.Tangent || attribute.attribute == VertexAttribute.Color) && dimension != 4 ||
                    attribute.attribute >= VertexAttribute.TexCoord0 && (dimension < 2 || dimension > 4))
                    throw new InvalidOperationException("Unsupported vertex attribute dimension: " + attribute);
            }
            var document = new MeshDocument { MaterialSlots = Math.Max(1, source.subMeshCount) };
            MeshCorner[] sourceCorners;
            using (var dataArray = MeshUtility.AcquireReadOnlyMeshData(source))
            {
                var data = dataArray[0];
                var count = data.vertexCount;
                using (var positions = new NativeArray<Vector3>(count, Allocator.Temp))
                {
                    data.GetVertices(positions);
                    for (var i = 0; i < count; i++)
                        document.Vertices.Add(new MeshVertex { Id = document.NextId++, Position = positions[i] });
                }
                var corners = Enumerable.Range(0, count).Select(i => new MeshCorner { Vertex = document.Vertices[i].Id }).ToArray();
                sourceCorners = corners;
                document.Normals = source.HasVertexAttribute(VertexAttribute.Normal);
                document.Tangents = source.HasVertexAttribute(VertexAttribute.Tangent);
                document.Colors = source.HasVertexAttribute(VertexAttribute.Color);
                if (document.Normals)
                {
                    using (var values = new NativeArray<Vector3>(count, Allocator.Temp))
                    { data.GetNormals(values); for (var i = 0; i < count; i++) corners[i].Normal = values[i]; }
                }
                if (document.Tangents)
                {
                    using (var values = new NativeArray<Vector4>(count, Allocator.Temp))
                    { data.GetTangents(values); for (var i = 0; i < count; i++) corners[i].Tangent = values[i]; }
                }
                if (document.Colors)
                {
                    using (var values = new NativeArray<Color>(count, Allocator.Temp))
                    { data.GetColors(values); for (var i = 0; i < count; i++) corners[i].Color = values[i]; }
                }
                for (var channel = 0; channel < 8; channel++)
                {
                    var attribute = (VertexAttribute)((int)VertexAttribute.TexCoord0 + channel);
                    if (!source.HasVertexAttribute(attribute)) continue;
                    document.UVDimensions[channel] = source.GetVertexAttributeDimension(attribute);
                    using (var values = new NativeArray<Vector4>(count, Allocator.Temp))
                    { data.GetUVs(channel, values); for (var i = 0; i < count; i++) corners[i].UV[channel] = values[i]; }
                }
                for (var slot = 0; slot < data.subMeshCount; slot++)
                {
                    var subMesh = data.GetSubMesh(slot);
                    var stride = subMesh.topology == MeshTopology.Triangles ? 3 : subMesh.topology == MeshTopology.Quads ? 4 : 0;
                    if (stride == 0 || subMesh.indexCount % stride != 0)
                        throw new InvalidOperationException("Only triangle and quad source submeshes are supported.");
                    using (var indices = new NativeArray<int>(subMesh.indexCount, Allocator.Temp))
                    {
                        data.GetIndices(indices, slot, true);
                        for (var i = 0; i < indices.Length; i += stride)
                        {
                            var face = new MeshFace { Id = document.NextId++, Material = slot, Corners = new MeshCorner[stride] };
                            for (var j = 0; j < stride; j++)
                            {
                                if (indices[i + j] < 0 || indices[i + j] >= count) throw new InvalidOperationException("Invalid source indices.");
                                // Corners must own their attributes independently even across a shared vertex.
                                face.Corners[j] = JsonUtility.FromJson<MeshCorner>(JsonUtility.ToJson(corners[indices[i + j]]));
                            }
                            document.Faces.Add(face);
                        }
                    }
                }
            }
            document.RebuildEdges();
            var referenced = new HashSet<int>(document.Faces.SelectMany(f => f.Corners).Select(c => c.Vertex));
            // Delete can retain vertices after removing their last face corner. Such vertices
            // carry default attributes only; never silently discard authored loose source data.
            var defaults = new MeshCorner();
            if (sourceCorners.Any(c => !referenced.Contains(c.Vertex) && !MeshDeleteDissolve.SameCorner(document, c, defaults)))
                throw new InvalidOperationException("Authored attributes on loose source vertices are unsupported. Deleted face corners leave default loose attributes only.");
            document.Validate();
            return document;
        }

        internal static Mesh Evaluate(MeshDocument document)
        {
            document.Validate();
            var vertices = document.Vertices.ToDictionary(v => v.Id, v => v.Position);
            var corners = new List<MeshCorner>();
            var positions = new List<Vector3>();
            var renderCorners = new Dictionary<string, int>();
            var indices = Enumerable.Range(0, document.MaterialSlots).Select(i => new List<int>()).ToArray();
            foreach (var face in document.Faces)
            {
                var polygon = face.Corners.Select(c => vertices[c.Vertex]).ToArray();
                var triangles = MeshTriangulation.FaceTriangles(face, polygon);
                if (triangles.Length == 0) continue;
                var map = new int[face.Corners.Length];
                for (var i = 0; i < face.Corners.Length; i++)
                {
                    var key = JsonUtility.ToJson(face.Corners[i]);
                    if (!renderCorners.TryGetValue(key, out var index))
                    {
                        index = corners.Count;
                        renderCorners.Add(key, index);
                        corners.Add(face.Corners[i]);
                        positions.Add(polygon[i]);
                    }
                    map[i] = index;
                }
                indices[face.Material].AddRange(triangles.Select(i => map[i]));
            }
            // Loose vertices have no corner attributes, but remain in the document and native bounds.
            var used = new HashSet<int>(corners.Select(c => c.Vertex));
            foreach (var vertex in document.Vertices.Where(v => !used.Contains(v.Id)))
            { positions.Add(vertex.Position); corners.Add(new MeshCorner { Vertex = vertex.Id }); }
            var order = Enumerable.Range(0, corners.Count).OrderBy(i => corners[i].Vertex).ToArray();
            var remap = new int[order.Length];
            for (var i = 0; i < order.Length; i++) remap[order[i]] = i;
            corners = order.Select(i => corners[i]).ToList();
            positions = order.Select(i => positions[i]).ToList();
            foreach (var slot in indices)
                for (var i = 0; i < slot.Count; i++) slot[i] = remap[slot[i]];
            var mesh = new Mesh { name = "Editable Mesh", indexFormat = positions.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            try
            {
                mesh.SetVertices(positions);
                if (document.Normals) mesh.SetNormals(corners.Select(c => c.Normal).ToList());
                if (document.Tangents) mesh.SetTangents(corners.Select(c => c.Tangent).ToList());
                if (document.Colors) mesh.SetColors(corners.Select(c => c.Color).ToList());
                for (var channel = 0; channel < 8; channel++)
                {
                    var values = corners.Select(c => c.UV[channel]).ToList();
                    switch (document.UVDimensions[channel])
                    {
                        case 2: mesh.SetUVs(channel, values.Select(v => (Vector2)v).ToList()); break;
                        case 3: mesh.SetUVs(channel, values.Select(v => (Vector3)v).ToList()); break;
                        case 4: mesh.SetUVs(channel, values); break;
                    }
                }
                mesh.subMeshCount = document.MaterialSlots;
                for (var slot = 0; slot < indices.Length; slot++) mesh.SetTriangles(indices[slot], slot, false);
                mesh.RecalculateBounds();
                return mesh;
            }
            catch { UnityEngine.Object.DestroyImmediate(mesh); throw; }
        }

        internal static void CopyGeometry(Mesh source, Mesh destination)
        {
            // Serialized copying can leave native rendering buffers/selection geometry stale.
            // Use mesh setters so the existing asset identity gets geometry notifications too.
            destination.Clear(false);
            destination.indexFormat = source.indexFormat;
            destination.SetVertices(source.vertices);
            if (source.HasVertexAttribute(VertexAttribute.Normal)) destination.SetNormals(source.normals);
            if (source.HasVertexAttribute(VertexAttribute.Tangent)) destination.SetTangents(source.tangents);
            if (source.HasVertexAttribute(VertexAttribute.Color)) destination.SetColors(source.colors);
            for (var channel = 0; channel < 8; channel++)
            {
                var attribute = (VertexAttribute)((int)VertexAttribute.TexCoord0 + channel);
                if (!source.HasVertexAttribute(attribute)) continue;
                var values = new List<Vector4>();
                source.GetUVs(channel, values);
                switch (source.GetVertexAttributeDimension(attribute))
                {
                    case 2: destination.SetUVs(channel, values.Select(v => (Vector2)v).ToList()); break;
                    case 3: destination.SetUVs(channel, values.Select(v => (Vector3)v).ToList()); break;
                    case 4: destination.SetUVs(channel, values); break;
                }
            }
            destination.subMeshCount = source.subMeshCount;
            for (var slot = 0; slot < source.subMeshCount; slot++)
                destination.SetIndices(source.GetIndices(slot), source.GetTopology(slot), slot, false);
            destination.bounds = source.bounds;
            destination.UploadMeshData(false);
            destination.MarkModified();
        }

        internal static string Fingerprint(Mesh mesh)
        {
            // Canonical readback covers indices, slots, dimensions and every supported attribute.
            var canonical = Import(mesh);
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(MeshDocument.CanonicalJson(canonical)))).Replace("-", "");
        }
    }

    internal static class MeshTriangulation
    {
        internal static int[] FaceTriangles(MeshFace face, Vector3[] polygon)
        {
            // D083: a committed zero-move extrusion retains logical walls. Only exactly
            // collapsed, explicitly tagged swept quads omit surface triangles. Other
            // degeneracy remains an error, including untagged source polygons.
            if (face.ExtrusionWall && polygon.Length == 4 &&
                ((polygon[0].Equals(polygon[3]) && polygon[1].Equals(polygon[2])) ||
                 (polygon[0].Equals(polygon[1]) && polygon[2].Equals(polygon[3]))))
                return Array.Empty<int>();
            return Triangulate(polygon, true);
        }

        internal static int[] Triangulate(Vector3[] polygon, bool allowNonPlanar = false)
        {
            var normal = Vector3.zero;
            for (var i = 0; i < polygon.Length; i++) normal += Vector3.Cross(polygon[i], polygon[(i + 1) % polygon.Length]);
            if (normal.sqrMagnitude < 1e-16f) throw new InvalidOperationException("Degenerate polygon.");
            normal.Normalize();
            var extent = polygon.Max(p => (p - polygon[0]).magnitude);
            // Editing can bend a polygon; project its boundary for ear clipping while retaining
            // the original 3D corner positions. Quad recovery still requires a planar pair.
            if (!allowNonPlanar && polygon.Any(p => Math.Abs(Vector3.Dot(p - polygon[0], normal)) > Math.Max(1e-6f, extent * 1e-5f)))
                throw new InvalidOperationException("Non-planar polygon requires an explicit triangulation policy.");
            var axis = Mathf.Abs(normal.x) > Mathf.Abs(normal.y) ? 0 : 1;
            if (Mathf.Abs(normal.z) > Mathf.Abs(normal[axis])) axis = 2;
            var points = polygon.Select(p => axis == 0 ? new Vector2(p.y, p.z) : axis == 1 ? new Vector2(p.z, p.x) : new Vector2(p.x, p.y)).ToArray();
            var epsilon = Math.Max(1e-12f, extent * extent * 1e-7f);
            var area = 0f;
            for (var i = 0; i < points.Length; i++) area += Cross(points[i], points[(i + 1) % points.Length]);
            var sign = Math.Sign(area);
            for (var i = 0; i < points.Length; i++)
            for (var j = i + 1; j < points.Length; j++)
            {
                var ni = (i + 1) % points.Length;
                var nj = (j + 1) % points.Length;
                if (i == j || ni == j || nj == i) continue;
                if (Intersects(points[i], points[ni], points[j], points[nj], epsilon))
                    throw new InvalidOperationException("Self-intersecting polygon.");
            }
            var loop = Enumerable.Range(0, points.Length).ToList();
            var triangles = new List<int>();
            while (loop.Count > 3)
            {
                var clipped = false;
                for (var i = 0; i < loop.Count; i++)
                {
                    var a = loop[(i + loop.Count - 1) % loop.Count];
                    var b = loop[i];
                    var c = loop[(i + 1) % loop.Count];
                    if (sign * Cross(points[b] - points[a], points[c] - points[a]) <= epsilon) continue;
                    if (loop.Any(p => p != a && p != b && p != c &&
                        sign * Cross(points[b] - points[a], points[p] - points[a]) >= -epsilon &&
                        sign * Cross(points[c] - points[b], points[p] - points[b]) >= -epsilon &&
                        sign * Cross(points[a] - points[c], points[p] - points[c]) >= -epsilon)) continue;
                    triangles.AddRange(new[] { a, b, c });
                    loop.RemoveAt(i);
                    clipped = true;
                    break;
                }
                if (!clipped) throw new InvalidOperationException("Polygon cannot be triangulated safely.");
            }
            if (sign * Cross(points[loop[1]] - points[loop[0]], points[loop[2]] - points[loop[0]]) <= epsilon)
                throw new InvalidOperationException("Degenerate final triangle.");
            triangles.AddRange(loop);
            return triangles.ToArray();
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
        private static bool Intersects(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float epsilon)
        {
            var abC = Cross(b - a, c - a); var abD = Cross(b - a, d - a);
            var cdA = Cross(d - c, a - c); var cdB = Cross(d - c, b - c);
            if (abC * abD < 0 && cdA * cdB < 0) return true;
            return (Math.Abs(abC) <= epsilon && OnSegment(a, b, c)) ||
                (Math.Abs(abD) <= epsilon && OnSegment(a, b, d)) ||
                (Math.Abs(cdA) <= epsilon && OnSegment(c, d, a)) ||
                (Math.Abs(cdB) <= epsilon && OnSegment(c, d, b));
        }
        private static bool OnSegment(Vector2 a, Vector2 b, Vector2 p) =>
            p.x >= Math.Min(a.x, b.x) && p.x <= Math.Max(a.x, b.x) &&
            p.y >= Math.Min(a.y, b.y) && p.y <= Math.Max(a.y, b.y);
    }
}
