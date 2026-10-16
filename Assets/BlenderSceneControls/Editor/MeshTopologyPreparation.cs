using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BlenderSceneControls
{
    // Deliberately conservative preparation, not a position-based Merge by Distance.
    internal static class MeshTopologyPreparation
    {
        private static (int, int) Key(int a, int b) => a < b ? (a, b) : (b, a);

        internal static MeshDocument Recover(MeshDocument source, out string message)
        {
            source.Validate();
            var result = source.Clone();
            var positions = result.Vertices.ToDictionary(v => v.Id, v => v.Position);
            var adjacency = result.EdgeFaces();
            var parent = result.Vertices.ToDictionary(v => v.Id, v => v.Id);
            int Root(int id) { while (parent[id] != id) id = parent[id]; return id; }
            void Join(int a, int b) { a = Root(a); b = Root(b); parent[Math.Max(a, b)] = Math.Min(a, b); }
            // Exact endpoint equality only. No tolerance can silently catch a nearby shell.
            var boundaries = result.Edges.Where(e => adjacency[e.Id].Length == 1).ToArray();
            var proposals = new List<(int a, int b)>();
            var rejected = new HashSet<int>();
            var visited = new HashSet<int>();
            foreach (var edge in boundaries)
            {
                if (!visited.Add(edge.Id)) continue;
                var matches = boundaries.Where(e =>
                    positions[e.A].Equals(positions[edge.A]) && positions[e.B].Equals(positions[edge.B]) ||
                    positions[e.A].Equals(positions[edge.B]) && positions[e.B].Equals(positions[edge.A])).ToArray();
                foreach (var e in matches) visited.Add(e.Id);
                if (matches.Length != 2)
                { foreach (var e in matches) { rejected.Add(e.A); rejected.Add(e.B); } continue; }
                MeshCorner[] Loop(MeshEdge e) => result.Faces.First(f => f.Id == adjacency[e.Id][0]).Corners;
                (int a, int b) Directed(MeshEdge e)
                {
                    var loop = Loop(e);
                    for (var i = 0; i < loop.Length; i++)
                    {
                        var a = loop[i].Vertex; var b = loop[(i + 1) % loop.Length].Vertex;
                        if (Key(a, b) == Key(e.A, e.B)) return (a, b);
                    }
                    throw new InvalidOperationException("Boundary edge is missing from its face.");
                }
                var x = Directed(matches[0]); var y = Directed(matches[1]);
                if (!positions[x.a].Equals(positions[y.b]) || !positions[x.b].Equals(positions[y.a]))
                { rejected.UnionWith(new[] { x.a, x.b, y.a, y.b }); continue; }
                proposals.Add((x.a, y.b)); proposals.Add((x.b, y.a));
                Join(x.a, y.b); Join(x.b, y.a);
            }
            // A proposed surface component must close completely. Open/touching/overlapping
            // ambiguous components are left unchanged, rather than partly stitching their seams.
            var surface = result.Vertices.ToDictionary(v => v.Id, v => v.Id);
            int Surface(int id) { while (surface[id] != id) id = surface[id]; return id; }
            void Connect(int a, int b) { a = Surface(a); b = Surface(b); surface[Math.Max(a, b)] = Math.Min(a, b); }
            foreach (var edge in result.Edges) Connect(edge.A, edge.B);
            foreach (var p in proposals) Connect(p.a, p.b);
            var blocked = new HashSet<int>(rejected.Select(Surface));
            foreach (var group in result.Faces.GroupBy(f => Surface(f.Corners[0].Vertex)))
            {
                var uses = new Dictionary<(int, int), List<(int a, int b)>>();
                double volume = 0;
                foreach (var face in group)
                {
                    var ids = face.Corners.Select(c => Root(c.Vertex)).ToArray();
                    if (ids.Distinct().Count() != ids.Length) { blocked.Add(group.Key); continue; }
                    for (var i = 0; i < ids.Length; i++)
                    {
                        var a = ids[i]; var b = ids[(i + 1) % ids.Length]; var key = Key(a, b);
                        if (!uses.ContainsKey(key)) uses.Add(key, new List<(int, int)>());
                        uses[key].Add((a, b));
                    }
                    var origin = positions[ids[0]];
                    for (var i = 1; i < ids.Length - 1; i++)
                        volume += Vector3.Dot(origin, Vector3.Cross(positions[ids[i]], positions[ids[i + 1]])) / 6.0;
                }
                if (uses.Values.Any(u => u.Count != 2 || u[0].a != u[1].b || u[0].b != u[1].a) || Math.Abs(volume) < 1e-12)
                    blocked.Add(group.Key);
            }
            var map = parent.Keys.ToDictionary(id => id, id => blocked.Contains(Surface(id)) ? id : Root(id));
            var groups = map.Where(p => p.Key != p.Value).Select(p => p.Value).Distinct().ToArray();
            foreach (var c in result.Faces.SelectMany(f => f.Corners)) c.Vertex = map[c.Vertex];
            result.Vertices.RemoveAll(v => map[v.Id] != v.Id);
            var edgeMap = new Dictionary<int, int>();
            var edges = new Dictionary<(int, int), MeshEdge>();
            foreach (var edge in result.Edges)
            {
                edge.A = map[edge.A]; edge.B = map[edge.B]; var key = Key(edge.A, edge.B);
                if (!edges.TryGetValue(key, out var kept)) { kept = edge; edges.Add(key, kept); }
                edgeMap[edge.Id] = kept.Id;
            }
            result.Edges = edges.Values.ToList();
            result.SelectedVertices = result.SelectedVertices.Select(id => map[id]).Distinct().ToArray();
            result.SelectedEdges = result.SelectedEdges.Select(id => edgeMap[id]).Distinct().ToArray();
            if (map.ContainsKey(result.ActiveElement)) result.ActiveElement = map[result.ActiveElement];
            else if (edgeMap.ContainsKey(result.ActiveElement)) result.ActiveElement = edgeMap[result.ActiveElement];
            result.Validate();
            message = $"{groups.Length} vertex groups proposed. Open or ambiguous surface components remain separate. Exact closed seams only.";
            return result;
        }

        internal static MeshDocument Quads(MeshDocument source, out string message, bool selectedOnly = true)
        {
            source.Validate();
            var result = source.Clone();
            var vertices = result.Vertices.ToDictionary(v => v.Id, v => v.Position);
            var faces = result.Faces.ToDictionary(f => f.Id);
            var candidates = new List<(MeshFace a, MeshFace b, MeshEdge edge, MeshCorner[] loop)>();
            var selected = new HashSet<int>(selectedOnly ? source.SelectedFaces : Array.Empty<int>());
            foreach (var entry in result.EdgeFaces().Where(p => p.Value.Length == 2))
            {
                var a = faces[entry.Value[0]]; var b = faces[entry.Value[1]];
                if (a.Corners.Length != 3 || b.Corners.Length != 3 || a.Material != b.Material ||
                    selected.Count > 0 && (!selected.Contains(a.Id) || !selected.Contains(b.Id))) continue;
                var edge = result.Edges.First(e => e.Id == entry.Key);
                bool Same(MeshCorner x, MeshCorner y) =>
                    (!source.Normals || x.Normal.Equals(y.Normal)) && (!source.Tangents || x.Tangent.Equals(y.Tangent)) &&
                    (!source.Colors || x.Color.Equals(y.Color)) && Enumerable.Range(0, 8).All(i => source.UVDimensions[i] == 0 || x.UV[i].Equals(y.UV[i]));
                if (new[] { edge.A, edge.B }.Any(id => !Same(a.Corners.First(c => c.Vertex == id), b.Corners.First(c => c.Vertex == id)))) continue;
                var boundary = new List<(MeshCorner a, MeshCorner b)>();
                foreach (var face in new[] { a, b })
                for (var i = 0; i < 3; i++)
                {
                    var x = face.Corners[i]; var y = face.Corners[(i + 1) % 3];
                    if (Key(x.Vertex, y.Vertex) != Key(edge.A, edge.B)) boundary.Add((x, y));
                }
                var loop = new List<MeshCorner> { boundary[0].a };
                while (loop.Count < 4)
                {
                    var next = boundary.Where(e => e.a.Vertex == loop.Last().Vertex).ToArray();
                    if (next.Length != 1 || loop.Any(c => c.Vertex == next[0].b.Vertex)) break;
                    loop.Add(next[0].b);
                }
                if (loop.Count != 4 || !boundary.Any(e => e.a.Vertex == loop[3].Vertex && e.b.Vertex == loop[0].Vertex)) continue;
                var points = loop.Select(c => vertices[c.Vertex]).ToArray();
                var n = Vector3.Cross(points[1] - points[0], points[2] - points[1]).normalized;
                if (Enumerable.Range(0, 4).Any(i => Vector3.Dot(Vector3.Cross(points[(i + 1) % 4] - points[i], points[(i + 2) % 4] - points[(i + 1) % 4]), n) <= 1e-10f)) continue;
                try { MeshTriangulation.Triangulate(points); }
                catch (InvalidOperationException) { continue; }
                candidates.Add((a, b, edge, loop.ToArray()));
            }
            var counts = candidates.SelectMany(c => new[] { c.a.Id, c.b.Id }).GroupBy(id => id).ToDictionary(g => g.Key, g => g.Count());
            var accepted = candidates.Where(c => counts[c.a.Id] == 1 && counts[c.b.Id] == 1).ToArray();
            var faceMap = new Dictionary<int, int>();
            foreach (var c in accepted)
            {
                // Keep one face ID; never reuse a deleted ID. Corners retain their original data.
                c.a.Corners = c.loop;
                faceMap[c.b.Id] = c.a.Id;
                result.Faces.Remove(c.b); result.Edges.Remove(c.edge);
            }
            result.SelectedFaces = result.SelectedFaces.Select(id => faceMap.TryGetValue(id, out var kept) ? kept : id).Distinct().ToArray();
            result.SelectedEdges = result.SelectedEdges.Where(id => result.Edges.Any(e => e.Id == id)).ToArray();
            if (faceMap.TryGetValue(result.ActiveElement, out var active)) result.ActiveElement = active;
            if (!result.Vertices.Any(v => v.Id == result.ActiveElement) && !result.Edges.Any(e => e.Id == result.ActiveElement) && !result.Faces.Any(f => f.Id == result.ActiveElement)) result.ActiveElement = 0;
            result.RebuildEdges(); result.Validate();
            message = $"{accepted.Length} quads proposed. Ambiguous pairs and diagonal attribute seams remain triangles.";
            return result;
        }
    }
}
