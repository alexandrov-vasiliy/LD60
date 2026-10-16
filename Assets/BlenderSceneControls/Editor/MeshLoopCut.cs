using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BlenderSceneControls
{
    // A ring crosses opposite quad edges. Rail direction is propagated explicitly so
    // unequal edge lengths, seams and closed strips use the same absolute factor.
    internal sealed class MeshLoopRing
    {
        internal readonly Dictionary<int, int> RailStarts = new Dictionary<int, int>();
        internal readonly Dictionary<int, int> FaceEntries = new Dictionary<int, int>();
        internal bool Closed;
    }

    internal static class MeshLoopCut
    {
        internal const int MaxCuts = 32;
        internal static (int, int) Key(int a, int b) => a < b ? (a, b) : (b, a);

        internal static MeshLoopRing Discover(MeshDocument source, int seed)
        {
            source.Validate();
            Budget(source, 0, 0);
            var edges = source.Edges.ToDictionary(e => e.Id);
            if (!edges.TryGetValue(seed, out var first)) throw new InvalidOperationException("Hover a quad edge to preview a ring.");
            var byPair = source.Edges.ToDictionary(e => Key(e.A, e.B));
            var faces = source.Faces.ToDictionary(f => f.Id);
            var adjacency = source.EdgeFaces();
            var result = new MeshLoopRing();
            var pending = new Queue<int>();
            result.RailStarts.Add(seed, first.A); pending.Enqueue(seed);
            while (pending.Count != 0)
            {
                var id = pending.Dequeue();
                if (adjacency[id].Length > 2) throw new InvalidOperationException("Loop Cut cannot split a non-manifold rail.");
                foreach (var faceId in adjacency[id].OrderBy(n => n))
                {
                    var face = faces[faceId];
                    if (face.Corners.Length != 4) continue;
                    var i = Array.FindIndex(face.Corners, c => c.Vertex == edges[id].A);
                    if (Key(face.Corners[i].Vertex, face.Corners[(i + 1) % 4].Vertex) != Key(edges[id].A, edges[id].B)) i = (i + 3) % 4;
                    var a = face.Corners[i].Vertex; var b = face.Corners[(i + 1) % 4].Vertex;
                    var c = face.Corners[(i + 2) % 4].Vertex; var d = face.Corners[(i + 3) % 4].Vertex;
                    var opposite = byPair[Key(c, d)].Id;
                    if (result.FaceEntries.TryGetValue(faceId, out var entry))
                    {
                        if (entry != id && entry != opposite) throw new InvalidOperationException("Ring crosses itself at a pole; choose another edge.");
                        continue;
                    }
                    var start = result.RailStarts[id] == a ? d : c;
                    if (result.RailStarts.TryGetValue(opposite, out var known))
                    {
                        if (known != start) throw new InvalidOperationException("Twisted ring has inconsistent slide rails.");
                    }
                    else { result.RailStarts.Add(opposite, start); pending.Enqueue(opposite); }
                    result.FaceEntries.Add(faceId, id);
                }
            }
            if (result.FaceEntries.Count == 0) throw new InvalidOperationException("Loop Cut needs a quad strip; triangles, n-gons and wires stop the ring.");
            result.Closed = result.RailStarts.Keys.All(e => adjacency[e].Count(result.FaceEntries.ContainsKey) == 2);
            return result;
        }

        private static void Budget(MeshDocument source, long faces, long vertices)
        {
            if (source.Faces.Count + faces > MeshEditTransaction.FullPreviewFaceBudget ||
                source.Faces.Sum(f => (long)f.Corners.Length) + faces * 4 + vertices > MeshEditTransaction.FullPreviewCornerBudget ||
                source.Vertices.Count + vertices > 300000)
                throw new InvalidOperationException("Loop Cut exceeds 250 faces / 1500 corners / 300k vertices preview budget.");
        }

        internal static MeshCorner Interpolate(MeshCorner a, MeshCorner b, int vertex, float t)
        {
            return new MeshCorner { Vertex = vertex, Normal = Vector3.LerpUnclamped(a.Normal, b.Normal, t),
                Tangent = Vector4.LerpUnclamped(a.Tangent, b.Tangent, t), Color = Color.LerpUnclamped(a.Color, b.Color, t),
                UV = Enumerable.Range(0, 8).Select(i => Vector4.LerpUnclamped(a.UV[i], b.UV[i], t)).ToArray() };
        }

        internal static MeshDocument Apply(MeshDocument source, MeshLoopRing ring, int count, float factor, MeshSelectionMode selection)
        {
            if (count < 1 || count > MaxCuts || !float.IsFinite(factor)) throw new InvalidOperationException("Loop Cut count must be 1–32 and slide finite.");
            // Keep a positive end segment: exact endpoints would collapse polygons.
            factor = Mathf.Clamp(factor, -.999f, .999f);
            Budget(source, (long)ring.FaceEntries.Count * count, (long)ring.RailStarts.Count * count);
            var result = source.Clone();
            var positions = source.Vertices.ToDictionary(v => v.Id, v => v.Position);
            var edges = source.Edges.ToDictionary(e => e.Id);
            var byPair = source.Edges.ToDictionary(e => Key(e.A, e.B));
            var points = new Dictionary<int, int[]>();
            foreach (var rail in ring.RailStarts.OrderBy(p => p.Key))
            {
                var edge = edges[rail.Key]; var end = rail.Value == edge.A ? edge.B : edge.A;
                var ids = new int[count];
                for (var k = 0; k < count; k++)
                {
                    var t = (k + 1f + factor) / (count + 1f);
                    ids[k] = result.NextId++;
                    result.Vertices.Add(new MeshVertex { Id = ids[k], Position = Vector3.LerpUnclamped(positions[rail.Value], positions[end], t) });
                }
                points.Add(rail.Key, ids);
            }
            MeshCorner[] Row(MeshCorner a, MeshCorner b)
            {
                var edge = byPair[Key(a.Vertex, b.Vertex)];
                var aligned = ring.RailStarts[edge.Id] == a.Vertex;
                var row = new List<MeshCorner> { MeshExtrusion.Copy(a, a.Vertex) };
                for (var k = 0; k < count; k++)
                {
                    var index = aligned ? k : count - 1 - k;
                    var t = (index + 1f + factor) / (count + 1f);
                    row.Add(Interpolate(a, b, points[edge.Id][index], aligned ? t : 1 - t));
                }
                row.Add(MeshExtrusion.Copy(b, b.Vertex));
                return row.ToArray();
            }
            result.Faces.Clear();
            var newFaces = new List<int>(); var cutPairs = new HashSet<(int, int)>();
            foreach (var face in source.Faces)
            {
                if (ring.FaceEntries.TryGetValue(face.Id, out var entry))
                {
                    var edge = edges[entry];
                    var i = Enumerable.Range(0, 4).First(n => Key(face.Corners[n].Vertex, face.Corners[(n + 1) % 4].Vertex) == Key(edge.A, edge.B));
                    var left = Row(face.Corners[i], face.Corners[(i + 1) % 4]);
                    var right = Row(face.Corners[(i + 3) % 4], face.Corners[(i + 2) % 4]);
                    for (var k = 0; k <= count; k++)
                    {
                        var id = result.NextId++; newFaces.Add(id);
                        result.Faces.Add(new MeshFace { Id = id, Material = face.Material, ExtrusionWall = face.ExtrusionWall,
                            Corners = new[] { left[k], left[k + 1], right[k + 1], right[k] }.Select(c => MeshExtrusion.Copy(c, c.Vertex)).ToArray() });
                        if (k < count) cutPairs.Add(Key(left[k + 1].Vertex, right[k + 1].Vertex));
                    }
                }
                else
                {
                    // Terminating triangles/n-gons receive boundary corners only. This keeps
                    // adjacency watertight without extending the cut through an arbitrary face.
                    var corners = new List<MeshCorner>();
                    for (var i = 0; i < face.Corners.Length; i++)
                    {
                        var a = face.Corners[i]; var b = face.Corners[(i + 1) % face.Corners.Length];
                        var edge = byPair[Key(a.Vertex, b.Vertex)];
                        if (points.ContainsKey(edge.Id)) corners.AddRange(Row(a, b).Take(count + 1));
                        else corners.Add(MeshExtrusion.Copy(a, a.Vertex));
                    }
                    result.Faces.Add(new MeshFace { Id = face.Id, Material = face.Material, ExtrusionWall = face.ExtrusionWall, Corners = corners.ToArray() });
                }
            }
            result.Edges.RemoveAll(e => points.ContainsKey(e.Id));
            result.RebuildEdges();
            result.SelectedVertices = selection == MeshSelectionMode.Vertex ? points.Values.SelectMany(a => a).ToArray() : Array.Empty<int>();
            result.SelectedEdges = selection == MeshSelectionMode.Edge ? result.Edges.Where(e => cutPairs.Contains(Key(e.A, e.B))).Select(e => e.Id).ToArray() : Array.Empty<int>();
            result.SelectedFaces = selection == MeshSelectionMode.Face ? newFaces.ToArray() : Array.Empty<int>();
            result.ActiveElement = result.SelectedVertices.Concat(result.SelectedEdges).Concat(result.SelectedFaces).FirstOrDefault();
            // Interpolation retains UV seams and wall tags. Existing auto-UV is updated on
            // subsequent G/R/S from its new snapshot, without repacking authored islands.
            result.Validate();
            return result;
        }
    }
}
