using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BlenderSceneControls
{
    internal enum MeshRemoval { Vertices, Edges, Faces, OnlyEdgesAndFaces, OnlyFaces, DissolveVertices, DissolveEdges, DissolveFaces }

    // Operate on a clone; any refusal leaves the complete source document untouched.
    internal static class MeshDeleteDissolve
    {
        private static (int, int) Key(int a, int b) => a < b ? (a, b) : (b, a);
        internal static bool SameCorner(MeshDocument d, MeshCorner a, MeshCorner b) =>
            (!d.Normals || a.Normal.Equals(b.Normal)) && (!d.Tangents || a.Tangent.Equals(b.Tangent)) &&
            (!d.Colors || a.Color.Equals(b.Color)) && Enumerable.Range(0, 8).All(i => d.UVDimensions[i] == 0 || a.UV[i].Equals(b.UV[i]));

        internal static MeshDocument Apply(MeshDocument source, MeshRemoval operation, out string message)
        {
            source.Validate();
            var d = source.Clone();
            var vertices = new HashSet<int>(d.SelectedVertices);
            var edges = new HashSet<int>(d.SelectedEdges);
            var faces = new HashSet<int>(d.SelectedFaces);
            foreach (var f in d.Faces.Where(f => faces.Contains(f.Id))) vertices.UnionWith(f.Corners.Select(c => c.Vertex));
            foreach (var e in d.Edges.Where(e => edges.Contains(e.Id))) vertices.UnionWith(new[] { e.A, e.B });
            // Selection contains exactly one component type. Convert only complete boundaries.
            if (d.SelectedEdges.Length == 0)
                edges.UnionWith(d.Edges.Where(e => vertices.Contains(e.A) && vertices.Contains(e.B)).Select(e => e.Id));
            if (d.SelectedFaces.Length == 0)
                faces.UnionWith(d.Faces.Where(f => f.Corners.All(c => vertices.Contains(c.Vertex))).Select(f => f.Id));

            if (operation == MeshRemoval.DissolveVertices)
            {
                foreach (var id in vertices.OrderBy(id => id)) DissolveVertex(d, id);
            }
            else if (operation == MeshRemoval.DissolveEdges || operation == MeshRemoval.DissolveFaces)
            {
                var adjacency = d.EdgeFaces();
                var links = operation == MeshRemoval.DissolveEdges ? d.Edges.Where(e => edges.Contains(e.Id)).ToArray() :
                    d.Edges.Where(e => adjacency[e.Id].Length == 2 && adjacency[e.Id].All(faces.Contains)).ToArray();
                if (links.Any(e => adjacency[e.Id].Length != 2))
                    throw new InvalidOperationException("Dissolve requires two faces per selected edge; boundary, wire and non-manifold edges are unsupported.");
                var pending = new HashSet<int>(links.SelectMany(e => adjacency[e.Id]));
                while (pending.Count > 0)
                {
                    var region = new HashSet<int> { pending.Min() };
                    bool changed;
                    do
                    {
                        var count = region.Count;
                        foreach (var e in links) if (adjacency[e.Id].Any(region.Contains)) region.UnionWith(adjacency[e.Id]);
                        changed = count != region.Count;
                    } while (changed);
                    if (operation == MeshRemoval.DissolveEdges && d.Edges.Any(e => adjacency[e.Id].Length == 2 &&
                        adjacency[e.Id].All(region.Contains) && !edges.Contains(e.Id)))
                        throw new InvalidOperationException("Dissolve would also remove an unselected interior edge. Select all interior edges or use Dissolve Faces.");
                    Merge(d, region);
                    pending.ExceptWith(region);
                }
            }
            else
            {
                var removedVertices = operation == MeshRemoval.Vertices ? vertices : new HashSet<int>();
                var removedEdges = operation == MeshRemoval.Edges || operation == MeshRemoval.OnlyEdgesAndFaces ? edges : new HashSet<int>();
                var adjacency = d.EdgeFaces();
                var removedFaces = operation == MeshRemoval.Faces || operation == MeshRemoval.OnlyFaces || operation == MeshRemoval.OnlyEdgesAndFaces ? faces : new HashSet<int>();
                removedFaces.UnionWith(d.Faces.Where(f => f.Corners.Any(c => removedVertices.Contains(c.Vertex))).Select(f => f.Id));
                removedFaces.UnionWith(removedEdges.SelectMany(id => adjacency[id]));
                var affectedVertices = new HashSet<int>(d.Faces.Where(f => removedFaces.Contains(f.Id)).SelectMany(f => f.Corners).Select(c => c.Vertex));
                foreach (var e in d.Edges.Where(e => removedEdges.Contains(e.Id))) affectedVertices.UnionWith(new[] { e.A, e.B });
                d.Faces.RemoveAll(f => removedFaces.Contains(f.Id));
                d.Edges.RemoveAll(e => removedEdges.Contains(e.Id) || removedVertices.Contains(e.A) || removedVertices.Contains(e.B));
                if (operation == MeshRemoval.Faces || operation == MeshRemoval.Edges || operation == MeshRemoval.Vertices)
                {
                    var remaining = d.EdgeFaces();
                    d.Edges.RemoveAll(e => remaining[e.Id].Length == 0 && adjacency[e.Id].Any(removedFaces.Contains));
                    var used = new HashSet<int>(d.Edges.SelectMany(e => new[] { e.A, e.B }));
                    removedVertices.UnionWith(affectedVertices.Where(id => !used.Contains(id)));
                }
                d.Vertices.RemoveAll(v => removedVertices.Contains(v.Id));
            }
            CleanSelection(d);
            d.Validate();
            message = $"{operation}: {source.Vertices.Count - d.Vertices.Count} vertices, {source.Edges.Count - d.Edges.Count} edges, {source.Faces.Count - d.Faces.Count} faces removed. Preview only.";
            return d;
        }

        private static void CleanSelection(MeshDocument d)
        {
            d.SelectedVertices = d.SelectedVertices.Where(id => d.Vertices.Any(v => v.Id == id)).ToArray();
            d.SelectedEdges = d.SelectedEdges.Where(id => d.Edges.Any(e => e.Id == id)).ToArray();
            d.SelectedFaces = d.SelectedFaces.Where(id => d.Faces.Any(f => f.Id == id)).Distinct().ToArray();
            if (!d.Vertices.Any(v => v.Id == d.ActiveElement) && !d.Edges.Any(e => e.Id == d.ActiveElement) && !d.Faces.Any(f => f.Id == d.ActiveElement)) d.ActiveElement = 0;
        }

        private static void Merge(MeshDocument d, HashSet<int> region)
        {
            if (region.Count < 2) return;
            var fs = d.Faces.Where(f => region.Contains(f.Id)).OrderBy(f => f.Id).ToArray();
            var oldVertices = new HashSet<int>(fs.SelectMany(f => f.Corners).Select(c => c.Vertex));
            if (fs.Select(f => f.Material).Distinct().Count() != 1)
                throw new InvalidOperationException("Dissolve cannot cross material boundaries.");
            foreach (var group in fs.SelectMany(f => f.Corners).GroupBy(c => c.Vertex))
                if (group.Any(c => !SameCorner(d, group.First(), c)))
                    throw new InvalidOperationException("Dissolve cannot cross corner UV, normal, tangent or color seams.");
            var uses = new Dictionary<(int, int), List<(MeshCorner a, MeshCorner b)>>();
            foreach (var f in fs)
            for (var i = 0; i < f.Corners.Length; i++)
            {
                var a = f.Corners[i]; var b = f.Corners[(i + 1) % f.Corners.Length]; var key = Key(a.Vertex, b.Vertex);
                if (!uses.ContainsKey(key)) uses[key] = new List<(MeshCorner, MeshCorner)>();
                uses[key].Add((a, b));
            }
            var adjacency = d.EdgeFaces();
            if (d.Edges.Any(e => uses.ContainsKey(Key(e.A, e.B)) && adjacency[e.Id].Length > 2))
                throw new InvalidOperationException("Dissolve cannot modify non-manifold region boundaries.");
            var internalEdges = new HashSet<int>();
            foreach (var entry in uses.Where(p => p.Value.Count > 1))
            {
                var u = entry.Value;
                var e = d.Edges.First(e => Key(e.A, e.B) == entry.Key);
                if (u.Count != 2 || u[0].a.Vertex != u[1].b.Vertex || u[0].b.Vertex != u[1].a.Vertex || adjacency[e.Id].Length != 2)
                    throw new InvalidOperationException("Dissolve requires an oriented manifold surface.");
                internalEdges.Add(e.Id);
            }
            var boundary = uses.Values.Where(u => u.Count == 1).Select(u => u[0]).ToArray();
            if (boundary.Length < 3 || boundary.GroupBy(e => e.a.Vertex).Any(g => g.Count() != 1) || boundary.GroupBy(e => e.b.Vertex).Any(g => g.Count() != 1))
                throw new InvalidOperationException("Dissolve requires one simple boundary without holes or branches.");
            var outgoing = boundary.ToDictionary(e => e.a.Vertex);
            var loop = new List<MeshCorner>();
            var next = boundary[0].a.Vertex;
            while (loop.All(c => c.Vertex != next) && outgoing.TryGetValue(next, out var edge))
            { loop.Add(edge.a); next = edge.b.Vertex; }
            if (loop.Count != boundary.Length || next != loop[0].Vertex)
                throw new InvalidOperationException("Dissolve of holes or disconnected boundaries is unsupported.");
            var positions = d.Vertices.ToDictionary(v => v.Id, v => v.Position);
            var polygon = loop.Select(c => positions[c.Vertex]).ToArray();
            MeshTriangulation.Triangulate(polygon); // Strict planarity, not deformation projection.
            var normal = Vector3.Cross(polygon[1] - polygon[0], polygon[2] - polygon[0]);
            if (normal.sqrMagnitude < 1e-16f)
                normal = Enumerable.Range(2, polygon.Length - 2).Select(i => Vector3.Cross(polygon[i - 1] - polygon[0], polygon[i] - polygon[0])).OrderByDescending(n => n.sqrMagnitude).First();
            normal.Normalize();
            var tolerance = Mathf.Max(1e-6f, polygon.Max(p => (p - polygon[0]).magnitude) * 1e-5f);
            if (fs.SelectMany(f => f.Corners).Any(c => Mathf.Abs(Vector3.Dot(positions[c.Vertex] - polygon[0], normal)) > tolerance))
                throw new InvalidOperationException("Dissolve cannot flatten a non-planar region.");
            var survivor = fs[0];
            survivor.Corners = loop.ToArray();
            d.Faces.RemoveAll(f => region.Contains(f.Id) && f.Id != survivor.Id);
            d.Edges.RemoveAll(e => internalEdges.Contains(e.Id));
            var used = new HashSet<int>(d.Edges.SelectMany(e => new[] { e.A, e.B }));
            d.Vertices.RemoveAll(v => oldVertices.Contains(v.Id) && !used.Contains(v.Id));
            d.SelectedFaces = d.SelectedFaces.Select(id => region.Contains(id) ? survivor.Id : id).Distinct().ToArray();
            if (region.Contains(d.ActiveElement)) d.ActiveElement = survivor.Id;
        }

        private static void DissolveVertex(MeshDocument d, int id)
        {
            if (!d.Vertices.Any(v => v.Id == id)) return;
            var incident = d.Edges.Where(e => e.A == id || e.B == id).ToArray();
            var fs = d.Faces.Where(f => f.Corners.Any(c => c.Vertex == id)).ToArray();
            if (incident.Length == 0) { d.Vertices.RemoveAll(v => v.Id == id); return; }
            if (incident.Length != 2)
            {
                if (fs.Length < 2 || incident.Any(e => d.EdgeFaces()[e.Id].Length != 2))
                    throw new InvalidOperationException("Dissolve vertex requires a degree-two boundary or a closed planar face fan.");
                Merge(d, new HashSet<int>(fs.Select(f => f.Id)));
                if (d.Edges.Any(e => e.A == id || e.B == id))
                    throw new InvalidOperationException("Dissolve vertex fan has a branch or a wire edge.");
                d.Vertices.RemoveAll(v => v.Id == id);
                return;
            }
            if (fs.Any(f => f.Corners.Length <= 3))
                throw new InvalidOperationException("Dissolve vertex would collapse a polygon.");
            if (incident.Any(e => d.EdgeFaces()[e.Id].Length > 2))
                throw new InvalidOperationException("Dissolve vertex cannot modify non-manifold edges.");
            var neighbors = incident.Select(e => e.A == id ? e.B : e.A).ToArray();
            if (neighbors[0] == neighbors[1] || d.Edges.Any(e => Key(e.A, e.B) == Key(neighbors[0], neighbors[1])))
                throw new InvalidOperationException("Dissolve vertex would create duplicate edges.");
            foreach (var f in fs)
            {
                f.Corners = f.Corners.Where(c => c.Vertex != id).ToArray();
                MeshTriangulation.Triangulate(f.Corners.Select(c => d.Vertices.First(v => v.Id == c.Vertex).Position).ToArray());
            }
            d.Edges.RemoveAll(e => incident.Contains(e));
            d.Edges.Add(new MeshEdge { Id = d.NextId++, A = neighbors[0], B = neighbors[1] });
            d.Vertices.RemoveAll(v => v.Id == id);
        }
    }
}
