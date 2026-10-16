using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BlenderSceneControls
{
    internal static class MeshExtrusion
    {
        internal static MeshCorner Copy(MeshCorner corner, int vertex)
        {
            var copy = JsonUtility.FromJson<MeshCorner>(JsonUtility.ToJson(corner));
            copy.Vertex = vertex;
            return copy;
        }

        internal static MeshDocument Apply(MeshDocument source, MeshSelectionMode mode, out Vector3 normal)
        {
            source.Validate();
            var result = source.Clone();
            var selectedFaces = new HashSet<int>(source.SelectedFaces);
            var selectedEdges = new HashSet<int>(source.SelectedEdges);
            var selectedVertices = new HashSet<int>(source.SelectedVertices);
            var faces = source.Faces.Where(f => selectedFaces.Contains(f.Id)).ToArray();
            var edges = source.Edges.Where(e => selectedEdges.Contains(e.Id)).ToArray();
            var ids = (mode == MeshSelectionMode.Face ? faces.SelectMany(f => f.Corners.Select(c => c.Vertex)) :
                mode == MeshSelectionMode.Edge ? edges.SelectMany(e => new[] { e.A, e.B }) : selectedVertices).Distinct().OrderBy(id => id).ToArray();
            if (ids.Length == 0) throw new InvalidOperationException("Select mesh elements first.");
            var positions = source.Vertices.ToDictionary(v => v.Id, v => v.Position);
            var adjacency = source.EdgeFaces();
            var edgeMap = source.Edges.ToDictionary(e => Key(e.A, e.B));
            var boundary = new List<(MeshCorner a, MeshCorner b, int material)>();
            normal = Vector3.zero;
            if (mode == MeshSelectionMode.Face)
            {
                foreach (var face in faces)
                for (var i = 0; i < face.Corners.Length; i++)
                {
                    var a = face.Corners[i]; var b = face.Corners[(i + 1) % face.Corners.Length];
                    normal += Vector3.Cross(positions[a.Vertex], positions[b.Vertex]);
                    var neighbors = adjacency[edgeMap[Key(a.Vertex, b.Vertex)].Id];
                    if (neighbors.Length > 2) throw new InvalidOperationException("Extrude Region requires manifold selected edges.");
                    var inside = neighbors.Where(selectedFaces.Contains).ToArray();
                    if (inside.Length == 1) boundary.Add((a, b, face.Material));
                    else
                    {
                        var other = source.Faces.First(f => f.Id == inside.First(id => id != face.Id));
                        if (!Enumerable.Range(0, other.Corners.Length).Any(j => other.Corners[j].Vertex == b.Vertex && other.Corners[(j + 1) % other.Corners.Length].Vertex == a.Vertex))
                            throw new InvalidOperationException("Extrude Region requires consistently oriented faces.");
                    }
                }
            }
            else if (mode == MeshSelectionMode.Edge)
            {
                foreach (var edge in edges)
                {
                    // Basic edge extrusion extends a boundary or wire, never adds a third face.
                    var neighbors = adjacency[edge.Id];
                    if (neighbors.Length > 1) throw new InvalidOperationException("Edge extrusion supports boundary and wire edges only.");
                    if (neighbors.Length == 0)
                        boundary.Add((new MeshCorner { Vertex = edge.A }, new MeshCorner { Vertex = edge.B }, 0));
                    else
                    {
                        var face = source.Faces.First(f => f.Id == neighbors[0]);
                        var j = Enumerable.Range(0, face.Corners.Length).First(i => Key(face.Corners[i].Vertex, face.Corners[(i + 1) % face.Corners.Length].Vertex) == Key(edge.A, edge.B));
                        // Reverse the existing face edge so the new wall has consistent winding.
                        boundary.Add((face.Corners[(j + 1) % face.Corners.Length], face.Corners[j], face.Material));
                    }
                }
            }
            var faceCount = source.Faces.Count + boundary.Count;
            var cornerCount = source.Faces.Sum(f => f.Corners.Length) + boundary.Count * 4L;
            if (faceCount > MeshEditTransaction.FullPreviewFaceBudget || cornerCount > MeshEditTransaction.FullPreviewCornerBudget ||
                (long)source.Vertices.Count + ids.Length > 300000)
                throw new InvalidOperationException("Extrusion exceeds the 250 faces / 1500 corners preview budget.");
            var copies = new Dictionary<int, int>();
            foreach (var id in ids)
            {
                var copy = result.NextId++;
                copies.Add(id, copy);
                result.Vertices.Add(new MeshVertex { Id = copy, Position = positions[id] });
            }
            result.SelectedVertices = result.SelectedEdges = result.SelectedFaces = Array.Empty<int>();
            var capIds = new List<int>();
            if (mode == MeshSelectionMode.Face)
            {
                result.Faces.RemoveAll(f => selectedFaces.Contains(f.Id));
                foreach (var face in faces)
                {
                    var id = result.NextId++; capIds.Add(id);
                    result.Faces.Add(new MeshFace { Id = id, Material = face.Material,
                        Corners = face.Corners.Select(c => Copy(c, copies[c.Vertex])).ToArray() });
                }
                // Keep base edges used by unselected faces or the region boundary, not interior wires.
                var boundaryKeys = new HashSet<(int, int)>(boundary.Select(b => Key(b.a.Vertex, b.b.Vertex)));
                result.Edges.RemoveAll(e => adjacency[e.Id].Length > 0 && adjacency[e.Id].All(selectedFaces.Contains) && !boundaryKeys.Contains(Key(e.A, e.B)));
            }
            foreach (var side in boundary)
                // Inherit shading/colors/secondary UVs; UV0 receives automatic wall projection.
                result.Faces.Add(new MeshFace { Id = result.NextId++, Material = side.material, ExtrusionWall = true,
                    Corners = new[] { Copy(side.a, side.a.Vertex), Copy(side.b, side.b.Vertex), Copy(side.b, copies[side.b.Vertex]), Copy(side.a, copies[side.a.Vertex]) } });
            if (mode == MeshSelectionMode.Vertex)
                foreach (var id in ids) result.Edges.Add(new MeshEdge { Id = result.NextId++, A = id, B = copies[id] });
            result.RebuildEdges();
            if (mode == MeshSelectionMode.Face) result.SelectedFaces = capIds.ToArray();
            else if (mode == MeshSelectionMode.Edge)
                result.SelectedEdges = edges.Select(e => result.Edges.First(n => Key(n.A, n.B) == Key(copies[e.A], copies[e.B])).Id).ToArray();
            else result.SelectedVertices = ids.Select(id => copies[id]).ToArray();
            var selection = result.SelectedFaces.Concat(result.SelectedEdges).Concat(result.SelectedVertices).ToArray();
            var oldActiveIndex = mode == MeshSelectionMode.Face ? Array.FindIndex(faces, f => f.Id == source.ActiveElement) :
                mode == MeshSelectionMode.Edge ? Array.FindIndex(edges, e => e.Id == source.ActiveElement) : Array.IndexOf(ids, source.ActiveElement);
            result.ActiveElement = selection[Math.Max(0, oldActiveIndex)];
            // Remove only old interior region vertices no longer referenced by any surface or wire.
            var used = new HashSet<int>(result.Faces.SelectMany(f => f.Corners.Select(c => c.Vertex)).Concat(result.Edges.SelectMany(e => new[] { e.A, e.B })));
            result.Vertices.RemoveAll(v => copies.ContainsKey(v.Id) && !used.Contains(v.Id));
            MeshAutoUV.UpdateWalls(result, result.Clone());
            result.Validate();
            return result;
        }

        private static (int, int) Key(int a, int b) => a < b ? (a, b) : (b, a);
    }
}
