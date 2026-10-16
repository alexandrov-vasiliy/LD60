using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BlenderSceneControls
{
    internal enum MeshSelectionMode { Vertex, Edge, Face }
    internal enum MeshPivotMode { Median, Active }

    // Selection lives on the detached working document, never on source assets or Unity Selection.
    internal sealed class MeshElementSelection
    {
        internal MeshDocument Document { get; private set; }
        internal MeshSelectionMode Mode { get; private set; }
        internal int Revision { get; private set; }
        private MeshPivotMode _pivotMode;
        private bool _xRay;
        internal MeshPivotMode PivotMode { get => _pivotMode; set { if (_pivotMode != value) { _pivotMode = value; Revision++; } } }
        internal bool XRay { get => _xRay; set { if (_xRay != value) { _xRay = value; Revision++; } } }
        internal readonly Dictionary<int, MeshVertex> Vertices;
        internal readonly Dictionary<int, MeshEdge> Edges;
        internal readonly Dictionary<int, MeshFace> Faces;
        internal readonly Dictionary<int, int[]> EdgeFaces;
        private readonly Dictionary<int, List<int>> _vertexEdges;
        private readonly Dictionary<int, List<int>> _faceEdges;
        internal readonly List<(int face, int a, int b, int c)> Triangles = new List<(int, int, int, int)>();

        internal MeshElementSelection(MeshDocument document)
        {
            Document = document;
            Vertices = document.Vertices.ToDictionary(v => v.Id);
            Edges = document.Edges.ToDictionary(e => e.Id);
            Faces = document.Faces.ToDictionary(f => f.Id);
            EdgeFaces = document.EdgeFaces();
            _vertexEdges = Vertices.Keys.ToDictionary(id => id, id => new List<int>());
            foreach (var edge in Edges.Values) { _vertexEdges[edge.A].Add(edge.Id); _vertexEdges[edge.B].Add(edge.Id); }
            _faceEdges = Faces.Keys.ToDictionary(id => id, id => new List<int>());
            foreach (var entry in EdgeFaces) foreach (var face in entry.Value) _faceEdges[face].Add(entry.Key);
            RefreshTriangles();
            var selected = document.SelectedVertices.Concat(document.SelectedEdges.SelectMany(ElementVertices)).Concat(document.SelectedFaces.SelectMany(ElementVertices)).ToArray();
            Assign(selected, document.ActiveElement);
        }

        // G/R/S preserves topology IDs, but replaces absolute positions and derived triangulation.
        internal void RefreshGeometry(MeshDocument document)
        {
            Document = document;
            Vertices.Clear(); foreach (var v in document.Vertices) Vertices.Add(v.Id, v);
            Edges.Clear(); foreach (var e in document.Edges) Edges.Add(e.Id, e);
            Faces.Clear(); foreach (var f in document.Faces) Faces.Add(f.Id, f);
            RefreshTriangles();
        }
        private void RefreshTriangles()
        {
            Triangles.Clear();
            foreach (var face in Document.Faces)
            {
                var ids = face.Corners.Select(c => c.Vertex).ToArray();
                var indices = MeshTriangulation.FaceTriangles(face, ids.Select(id => Vertices[id].Position).ToArray());
                for (var i = 0; i < indices.Length; i += 3)
                    Triangles.Add((face.Id, ids[indices[i]], ids[indices[i + 1]], ids[indices[i + 2]]));
            }
        }

        internal void RefreshTopology(MeshDocument document)
        {
            RefreshGeometry(document);
            EdgeFaces.Clear(); foreach (var entry in document.EdgeFaces()) EdgeFaces.Add(entry.Key, entry.Value);
            _vertexEdges.Clear(); foreach (var id in Vertices.Keys) _vertexEdges.Add(id, new List<int>());
            foreach (var e in Edges.Values) { _vertexEdges[e.A].Add(e.Id); _vertexEdges[e.B].Add(e.Id); }
            _faceEdges.Clear(); foreach (var id in Faces.Keys) _faceEdges.Add(id, new List<int>());
            foreach (var entry in EdgeFaces) foreach (var id in entry.Value) _faceEdges[id].Add(entry.Key);
            Revision++;
        }

        internal int[] Selected => Mode == MeshSelectionMode.Vertex ? Document.SelectedVertices :
            Mode == MeshSelectionMode.Edge ? Document.SelectedEdges : Document.SelectedFaces;
        internal IEnumerable<int> All => Mode == MeshSelectionMode.Vertex ? Vertices.Keys :
            Mode == MeshSelectionMode.Edge ? Edges.Keys : Faces.Keys;

        internal int[] ElementVertices(int id)
        {
            if (Vertices.ContainsKey(id)) return new[] { id };
            if (Edges.TryGetValue(id, out var edge)) return new[] { edge.A, edge.B };
            return Faces.TryGetValue(id, out var face) ? face.Corners.Select(c => c.Vertex).ToArray() : Array.Empty<int>();
        }

        internal int[] SelectedVertexIds() => Selected.SelectMany(ElementVertices).Distinct().OrderBy(id => id).ToArray();
        internal Vector3 Center(int id)
        {
            var ids = ElementVertices(id);
            return ids.Length == 0 ? Vector3.zero : ids.Aggregate(Vector3.zero, (sum, v) => sum + Vertices[v].Position) / ids.Length;
        }
        internal Vector3 Pivot
        {
            get
            {
                if (PivotMode == MeshPivotMode.Active && Selected.Contains(Document.ActiveElement)) return Center(Document.ActiveElement);
                var ids = SelectedVertexIds();
                return ids.Length == 0 ? Vector3.zero : ids.Aggregate(Vector3.zero, (sum, id) => sum + Vertices[id].Position) / ids.Length;
            }
        }
        internal Bounds WorldBounds(Transform transform)
        {
            var ids = SelectedVertexIds();
            if (ids.Length == 0) ids = Vertices.Keys.ToArray();
            var bounds = new Bounds(transform.position, Vector3.zero);
            if (ids.Length == 0) return bounds;
            bounds = new Bounds(transform.TransformPoint(Vertices[ids[0]].Position), Vector3.zero);
            foreach (var id in ids.Skip(1)) bounds.Encapsulate(transform.TransformPoint(Vertices[id].Position));
            if (bounds.size.sqrMagnitude < .000001f) bounds.Expand(.1f);
            return bounds;
        }

        internal void SetMode(MeshSelectionMode mode)
        {
            var vertices = new HashSet<int>(SelectedVertexIds());
            var oldActive = Document.ActiveElement;
            Mode = mode;
            var converted = All.Where(id => ElementVertices(id).All(vertices.Contains)).OrderBy(id => id).ToArray();
            Assign(converted, converted.Contains(oldActive) ? oldActive : converted.FirstOrDefault());
        }
        internal void Assign(IEnumerable<int> ids, int active = 0)
        {
            var allowed = new HashSet<int>(All);
            var values = ids.Where(allowed.Contains).Distinct().OrderBy(id => id).ToArray();
            Document.SelectedVertices = Mode == MeshSelectionMode.Vertex ? values : Array.Empty<int>();
            Document.SelectedEdges = Mode == MeshSelectionMode.Edge ? values : Array.Empty<int>();
            Document.SelectedFaces = Mode == MeshSelectionMode.Face ? values : Array.Empty<int>();
            Document.ActiveElement = values.Contains(active) ? active : values.FirstOrDefault();
            Revision++;
        }
        internal void Click(int id, bool extend)
        {
            var selected = new HashSet<int>(extend ? Selected : Array.Empty<int>());
            if (id != 0 && (!extend || !selected.Remove(id))) selected.Add(id);
            Assign(selected, selected.Contains(id) ? id : 0);
        }
        internal void Add(IEnumerable<int> ids, bool extend) => Assign(extend ? Selected.Concat(ids) : ids);

        // Edge loop continues across a regular valence-four vertex without crossing a face.
        // Boundaries continue only at valence two; poles, triangles and non-manifold forks stop.
        internal int[] Loop(int seed)
        {
            if (!Edges.ContainsKey(seed)) return Array.Empty<int>();
            var result = new HashSet<int>();
            var pending = new Queue<int>(); pending.Enqueue(seed);
            while (pending.Count != 0)
            {
                var id = pending.Dequeue();
                if (!result.Add(id)) continue;
                var edge = Edges[id];
                foreach (var vertex in new[] { edge.A, edge.B })
                {
                    var incident = _vertexEdges[vertex];
                    var adjacent = EdgeFaces[id];
                    if (adjacent.Length > 2 || adjacent.Any(f => Faces[f].Corners.Length != 4)) continue;
                    var candidates = incident.Where(e => e != id && EdgeFaces[e].Length <= 2 &&
                        (incident.Count == 2 || !EdgeFaces[e].Intersect(adjacent).Any())).ToArray();
                    if ((incident.Count == 4 || incident.Count == 2) && candidates.Length == 1) pending.Enqueue(candidates[0]);
                }
            }
            if (Mode == MeshSelectionMode.Edge) return result.OrderBy(id => id).ToArray();
            if (Mode == MeshSelectionMode.Vertex) return result.SelectMany(ElementVertices).Distinct().OrderBy(id => id).ToArray();
            // Face loop: cross opposite edges of quads, stopping at boundaries/poles.
            var faces = new HashSet<int>();
            var crossed = new HashSet<int>(); pending.Enqueue(seed);
            while (pending.Count != 0)
            {
                var id = pending.Dequeue();
                if (!crossed.Add(id) || EdgeFaces[id].Length > 2) continue;
                foreach (var f in EdgeFaces[id])
                {
                    var face = Faces[f]; if (face.Corners.Length != 4) continue;
                    faces.Add(f);
                    var edge = Edges[id];
                    var opposite = _faceEdges[f].FirstOrDefault(e => Edges[e].A != edge.A && Edges[e].A != edge.B && Edges[e].B != edge.A && Edges[e].B != edge.B);
                    if (opposite != 0) pending.Enqueue(opposite);
                }
            }
            return faces.OrderBy(id => id).ToArray();
        }

        internal static bool RayTriangle(Ray ray, Vector3 a, Vector3 b, Vector3 c, out float distance)
        {
            distance = 0;
            var ab = b - a; var ac = c - a; var p = Vector3.Cross(ray.direction, ac);
            var determinant = Vector3.Dot(ab, p);
            if (Mathf.Abs(determinant) < 1e-9f) return false;
            var t = ray.origin - a; var u = Vector3.Dot(t, p) / determinant;
            if (u < -.00001f || u > 1.00001f) return false;
            var q = Vector3.Cross(t, ab); var v = Vector3.Dot(ray.direction, q) / determinant;
            if (v < -.00001f || u + v > 1.00001f) return false;
            distance = Vector3.Dot(ac, q) / determinant;
            return distance >= 0;
        }
    }
}

