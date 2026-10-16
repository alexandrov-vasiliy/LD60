using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BlenderSceneControls
{
    [Serializable]
    internal sealed class MeshVertex
    {
        public int Id;
        public Vector3 Position;
    }

    [Serializable]
    internal sealed class MeshCorner
    {
        public int Vertex;
        public Vector3 Normal;
        public Vector4 Tangent;
        public Color Color = Color.white;
        public Vector4[] UV = new Vector4[8];
    }

    [Serializable]
    internal sealed class MeshFace
    {
        public int Id;
        public int Material;
        public MeshCorner[] Corners = Array.Empty<MeshCorner>();
        public bool ExtrusionWall;
    }

    [Serializable]
    internal sealed class MeshEdge
    {
        public int Id;
        public int A;
        public int B;
    }

    // Editor data only. Render triangles are deliberately absent from this document.
    [Serializable]
    internal sealed class MeshDocument
    {
        public int NextId = 1;
        public int Revision;
        public int MaterialSlots = 1;
        public bool Normals;
        public bool Tangents;
        public bool Colors;
        public int[] UVDimensions = new int[8];
        public List<MeshVertex> Vertices = new List<MeshVertex>();
        public List<MeshEdge> Edges = new List<MeshEdge>();
        public List<MeshFace> Faces = new List<MeshFace>();
        public int[] SelectedVertices = Array.Empty<int>();
        public int[] SelectedEdges = Array.Empty<int>();
        public int[] SelectedFaces = Array.Empty<int>();
        public int ActiveElement;

        internal MeshDocument Clone() => JsonUtility.FromJson<MeshDocument>(JsonUtility.ToJson(this));

        // Preserve existing schema-1 hashes when the optional M07 wall marker is absent.
        internal static string CanonicalJson(MeshDocument document) =>
            JsonUtility.ToJson(document).Replace(",\"ExtrusionWall\":false", "");

        internal void RebuildEdges()
        {
            var previous = Edges.ToDictionary(e => Key(e.A, e.B), e => e);
            var result = new Dictionary<(int, int), MeshEdge>();
            foreach (var face in Faces)
            for (var i = 0; i < face.Corners.Length; i++)
            {
                var a = face.Corners[i].Vertex;
                var b = face.Corners[(i + 1) % face.Corners.Length].Vertex;
                var key = Key(a, b);
                if (!result.ContainsKey(key))
                    result.Add(key, previous.TryGetValue(key, out var edge) ? edge :
                        new MeshEdge { Id = NextId++, A = key.Item1, B = key.Item2 });
            }
            // Retain authored wire edges; tools must explicitly remove obsolete edges.
            foreach (var edge in Edges)
                if (!result.ContainsKey(Key(edge.A, edge.B))) result.Add(Key(edge.A, edge.B), edge);
            Edges = result.Values.OrderBy(e => e.Id).ToList();
        }

        internal Dictionary<int, int[]> EdgeFaces()
        {
            var edges = Edges.ToDictionary(e => Key(e.A, e.B), e => e.Id);
            var adjacency = Edges.ToDictionary(e => e.Id, e => new List<int>());
            foreach (var face in Faces)
            for (var i = 0; i < face.Corners.Length; i++)
                adjacency[edges[Key(face.Corners[i].Vertex,
                    face.Corners[(i + 1) % face.Corners.Length].Vertex)]].Add(face.Id);
            return adjacency.ToDictionary(p => p.Key, p => p.Value.ToArray());
        }

        internal void Validate()
        {
            if (Faces.Count > 100000 || Vertices.Count > 300000 || Faces.Sum(f => (long)f.Corners.Length) > 1000000)
                throw new InvalidOperationException("Mesh document exceeds the M02 allocation budget (100k faces, 300k vertices, 1m corners).");
            if (MaterialSlots < 1 || UVDimensions == null || UVDimensions.Length != 8 ||
                UVDimensions.Any(d => d != 0 && (d < 2 || d > 4)))
                throw new InvalidOperationException("Unsupported channel layout.");
            var ids = Vertices.Select(v => v.Id).Concat(Edges.Select(e => e.Id)).Concat(Faces.Select(f => f.Id)).ToArray();
            if (ids.Any(id => id <= 0 || id >= NextId) || ids.Distinct().Count() != ids.Length)
                throw new InvalidOperationException("Element IDs must be unique and below NextId.");
            var vertices = Vertices.ToDictionary(v => v.Id);
            if (Vertices.Any(v => !GrabMath.Finite(v.Position)))
                throw new InvalidOperationException("Non-finite vertex.");
            if (Edges.Any(e => e.A == e.B || !vertices.ContainsKey(e.A) || !vertices.ContainsKey(e.B)) ||
                Edges.Select(e => Key(e.A, e.B)).Distinct().Count() != Edges.Count)
                throw new InvalidOperationException("Invalid edges.");
            foreach (var face in Faces)
            {
                if (face.Material < 0 || face.Material >= MaterialSlots || face.Corners.Length < 3 ||
                    face.Corners.Select(c => c.Vertex).Distinct().Count() != face.Corners.Length ||
                    face.Corners.Any(c => !vertices.ContainsKey(c.Vertex) || c.UV == null || c.UV.Length != 8))
                    throw new InvalidOperationException("Invalid polygon loop or material slot.");
                foreach (var corner in face.Corners)
                {
                    if (!GrabMath.Finite(corner.Normal) || !Finite(corner.Tangent) ||
                        !Finite((Vector4)corner.Color) || corner.UV.Any(uv => !Finite(uv)))
                        throw new InvalidOperationException("Non-finite corner attributes.");
                }
            }
            EdgeFaces();
            if (SelectedVertices.Any(id => !vertices.ContainsKey(id)) ||
                SelectedEdges.Any(id => !Edges.Any(e => e.Id == id)) ||
                SelectedFaces.Any(id => !Faces.Any(f => f.Id == id)) ||
                (ActiveElement != 0 && !ids.Contains(ActiveElement)))
                throw new InvalidOperationException("Selection references missing elements.");
        }

        private static bool Finite(Vector4 v) => GrabMath.Finite(new Vector3(v.x, v.y, v.z)) &&
            !float.IsNaN(v.w) && !float.IsInfinity(v.w);
        private static (int, int) Key(int a, int b) => a < b ? (a, b) : (b, a);
    }
}
