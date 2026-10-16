using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BlenderSceneControls
{
    internal readonly struct InsetOptions
    {
        internal readonly bool Individual, Boundary, Even, Outward;
        internal InsetOptions(bool individual = false, bool boundary = true, bool even = true, bool outward = false)
        { Individual = individual; Boundary = boundary; Even = even; Outward = outward; }
    }

    // Independent implementation: intersect face-plane and boundary offset constraints.
    // A folded region is supported only when these constraints have a consistent solution.
    internal static class MeshInset
    {
        internal static (int, int) Key(int a, int b) => a < b ? (a, b) : (b, a);

        internal static MeshDocument Apply(MeshDocument source, float thickness, float depth, InsetOptions options)
        {
            source.Validate();
            if (!float.IsFinite(thickness) || !float.IsFinite(depth))
                throw new InvalidOperationException("Inset thickness and depth must be finite.");
            if (options.Outward || thickness < 0)
                throw new InvalidOperationException("Outset is unavailable: expanding caps would overlap the border. Use inward thickness.");
            var selected = source.SelectedFaces.ToHashSet();
            var faces = source.Faces.Where(f => selected.Contains(f.Id)).ToArray();
            if (faces.Length == 0) throw new InvalidOperationException("Select faces for Inset.");
            if (source.Faces.Count > MeshEditTransaction.FullPreviewFaceBudget ||
                source.Faces.Sum(f => (long)f.Corners.Length) > MeshEditTransaction.FullPreviewCornerBudget)
                throw new InvalidOperationException("Inset exceeds the 250 faces / 1500 corners preview budget.");
            if (thickness == 0 && depth == 0) return source.Clone();
            var positions = source.Vertices.ToDictionary(v => v.Id, v => v.Position);
            var edgeMap = source.Edges.ToDictionary(e => Key(e.A, e.B));
            var adjacency = source.EdgeFaces();
            var normals = new Dictionary<int, Vector3>();
            foreach (var face in faces)
            {
                var points = face.Corners.Select(c => positions[c.Vertex]).ToArray();
                MeshTriangulation.Triangulate(points); // Offset starts from planar faces, including concave n-gons.
                normals[face.Id] = AreaNormal(points);
                foreach (var c in face.Corners)
                {
                    var at = Array.IndexOf(face.Corners, c);
                    var next = face.Corners[(at + 1) % face.Corners.Length];
                    var neighbors = adjacency[edgeMap[Key(c.Vertex, next.Vertex)].Id];
                    if (neighbors.Length > 2) throw new InvalidOperationException("Inset requires manifold selected edges.");
                    if (neighbors.Length != 2) continue;
                    var other = source.Faces.First(f => f.Id == neighbors.First(id => id != face.Id));
                    if (!Enumerable.Range(0, other.Corners.Length).Any(i => other.Corners[i].Vertex == next.Vertex &&
                        other.Corners[(i + 1) % other.Corners.Length].Vertex == c.Vertex))
                        throw new InvalidOperationException("Inset requires consistently oriented neighboring faces.");
                }
            }
            var groups = new List<MeshFace[]>();
            var pending = new HashSet<int>(selected);
            while (pending.Count > 0)
            {
                var queue = new Queue<int>(); var group = new List<MeshFace>();
                var seed = pending.Min(); pending.Remove(seed); queue.Enqueue(seed);
                while (queue.Count > 0)
                {
                    var queuedId = queue.Dequeue();
                    var face = faces.First(f => f.Id == queuedId); group.Add(face);
                    if (options.Individual) continue;
                    for (var i = 0; i < face.Corners.Length; i++)
                        foreach (var id in adjacency[edgeMap[Key(face.Corners[i].Vertex, face.Corners[(i + 1) % face.Corners.Length].Vertex)].Id])
                            if (pending.Remove(id)) queue.Enqueue(id);
                }
                groups.Add(group.ToArray());
            }
            if (!options.Individual && groups.SelectMany(g => g.SelectMany(f => f.Corners.Select(c => c.Vertex)).Distinct())
                .GroupBy(id => id).Any(g => g.Count() > 1))
                throw new InvalidOperationException("Inset regions must not touch only at a vertex.");

            var result = source.Clone();
            var changed = new HashSet<int>();
            foreach (var group in groups)
            {
                var ids = group.Select(f => f.Id).ToHashSet();
                var boundary = new List<(MeshFace face, MeshCorner a, MeshCorner b, bool offset)>();
                foreach (var face in group)
                    for (var i = 0; i < face.Corners.Length; i++)
                    {
                        var a = face.Corners[i]; var b = face.Corners[(i + 1) % face.Corners.Length];
                        var neighbors = adjacency[edgeMap[Key(a.Vertex, b.Vertex)].Id];
                        if (neighbors.Count(ids.Contains) == 1)
                            boundary.Add((face, a, b, options.Individual || options.Boundary || neighbors.Length == 2));
                    }
                if (boundary.Count == 0) throw new InvalidOperationException("Inset Region needs an open selected region boundary.");
                var outgoing = boundary.GroupBy(b => b.a.Vertex).ToArray();
                if (outgoing.Any(g => g.Count() != 1) || boundary.GroupBy(b => b.b.Vertex).Any(g => g.Count() != 1))
                    throw new InvalidOperationException("Inset boundary branches at a vertex.");
                // One simple loop per connected region. Holes need a separate offset/overlap policy.
                var next = boundary.ToDictionary(b => b.a.Vertex, b => b.b.Vertex);
                var visited = new HashSet<int>(); var current = boundary[0].a.Vertex;
                while (visited.Add(current)) current = next[current];
                if (visited.Count != boundary.Count || current != boundary[0].a.Vertex)
                    throw new InvalidOperationException("Inset regions with holes are unavailable.");
                var vertices = group.SelectMany(f => f.Corners.Select(c => c.Vertex)).Distinct().OrderBy(id => id).ToArray();
                var copies = new Dictionary<int, int>();
                var proposed = new Dictionary<int, Vector3>();
                foreach (var id in vertices)
                {
                    var incident = group.Where(f => f.Corners.Any(c => c.Vertex == id)).ToArray();
                    var rows = new List<(Vector3 axis, float value)>();
                    foreach (var face in incident) rows.Add((normals[face.Id].normalized, 0));
                    foreach (var side in boundary.Where(b => b.a.Vertex == id || b.b.Vertex == id))
                    {
                        var inward = Vector3.Cross(normals[side.face.Id].normalized, (positions[side.b.Vertex] - positions[side.a.Vertex]).normalized);
                        rows.Add((inward, side.offset ? thickness : 0));
                    }
                    var offset = Solve(rows);
                    if (!options.Even && offset.sqrMagnitude > 0) offset = offset.normalized * thickness;
                    var normal = incident.Aggregate(Vector3.zero, (sum, f) => sum + normals[f.Id]).normalized;
                    var position = positions[id] + offset + normal * depth;
                    if (!GrabMath.Finite(position)) throw new InvalidOperationException("Inset produced a non-finite position.");
                    proposed[id] = position;
                    if (position.Equals(positions[id])) copies[id] = id;
                    else copies[id] = result.NextId++;
                }
                var strips = boundary.Where(b => b.offset && (copies[b.a.Vertex] != b.a.Vertex || copies[b.b.Vertex] != b.b.Vertex)).ToArray();
                if (result.Faces.Count + strips.Length > MeshEditTransaction.FullPreviewFaceBudget ||
                    result.Faces.Sum(f => (long)f.Corners.Length) + strips.Length * 4L > MeshEditTransaction.FullPreviewCornerBudget ||
                    (long)result.Vertices.Count + copies.Count(p => p.Key != p.Value) > 300000)
                    throw new InvalidOperationException("Inset result exceeds the 250 faces / 1500 corners preview budget.");
                foreach (var pair in copies.Where(p => p.Key != p.Value))
                    result.Vertices.Add(new MeshVertex { Id = pair.Value, Position = proposed[pair.Key] });
                foreach (var face in group)
                {
                    var points = face.Corners.Select(c => proposed[c.Vertex]).ToArray();
                    if (Vector3.Dot(AreaNormal(points), normals[face.Id]) <= 1e-12f)
                        throw new InvalidOperationException("Inset would collapse or invert a cap.");
                    for (var i = 0; i < points.Length; i++)
                    {
                        var j = (i + 1) % points.Length;
                        if (Vector3.Dot(points[j] - points[i], positions[face.Corners[j].Vertex] - positions[face.Corners[i].Vertex]) <= 1e-12f)
                            throw new InvalidOperationException("Inset thickness crosses a narrow edge. Reduce thickness.");
                    }
                    var cap = result.Faces.First(f => f.Id == face.Id);
                    cap.Corners = face.Corners.Select(c => MeshExtrusion.Copy(c, copies[c.Vertex])).ToArray();
                    changed.Add(cap.Id);
                }
                foreach (var side in strips)
                {
                    var strip = new MeshFace { Id = result.NextId++, Material = side.face.Material, ExtrusionWall = true,
                        Corners = new[] { MeshExtrusion.Copy(side.a, side.a.Vertex), MeshExtrusion.Copy(side.b, side.b.Vertex),
                            MeshExtrusion.Copy(side.b, copies[side.b.Vertex]), MeshExtrusion.Copy(side.a, copies[side.a.Vertex]) } };
                    result.Faces.Add(strip); changed.Add(strip.Id);
                }
            }
            var used = result.Faces.SelectMany(f => Enumerable.Range(0, f.Corners.Length)
                .Select(i => Key(f.Corners[i].Vertex, f.Corners[(i + 1) % f.Corners.Length].Vertex))).ToHashSet();
            result.Edges.RemoveAll(e => adjacency[e.Id].Length > 0 && !used.Contains(Key(e.A, e.B)));
            result.RebuildEdges();
            var referenced = result.Faces.SelectMany(f => f.Corners.Select(c => c.Vertex))
                .Concat(result.Edges.SelectMany(e => new[] { e.A, e.B })).ToHashSet();
            var regionVertices = faces.SelectMany(f => f.Corners.Select(c => c.Vertex)).ToHashSet();
            result.Vertices.RemoveAll(v => regionVertices.Contains(v.Id) && !referenced.Contains(v.Id));
            result.SelectedVertices = result.SelectedEdges = Array.Empty<int>();
            result.SelectedFaces = faces.Select(f => f.Id).ToArray();
            result.ActiveElement = selected.Contains(source.ActiveElement) ? source.ActiveElement : result.SelectedFaces[0];
            // Existing generated caps retain their UV island during Inset. New border faces
            // use the same persisted tag and later G/R/S policy as extrusion walls.
            var uv = result.Clone();
            uv.Faces.RemoveAll(f => !changed.Contains(f.Id) || selected.Contains(f.Id));
            MeshAutoUV.UpdateWalls(uv, uv.Clone());
            result.UVDimensions[0] = uv.UVDimensions[0];
            foreach (var wall in uv.Faces) result.Faces.First(f => f.Id == wall.Id).Corners = wall.Corners;
            result.Validate();
            CheckIntersections(result, changed);
            return result;
        }

        private static Vector3 AreaNormal(Vector3[] points)
        {
            var sum = Vector3.zero;
            for (var i = 0; i < points.Length; i++) sum += Vector3.Cross(points[i] - points[0], points[(i + 1) % points.Length] - points[0]);
            return sum;
        }

        private static Vector3 Solve(List<(Vector3 axis, float value)> constraints)
        {
            var basis = new List<(Vector3 axis, float value)>();
            foreach (var row in constraints)
            {
                var axis = row.axis; var value = row.value;
                foreach (var b in basis) { var dot = Vector3.Dot(axis, b.axis); axis -= dot * b.axis; value -= dot * b.value; }
                var length = axis.magnitude;
                if (length < 1e-5f)
                {
                    if (Mathf.Abs(value) > Mathf.Max(1e-6f, Mathf.Abs(row.value) * 1e-4f))
                        throw new InvalidOperationException("Bent region offset constraints disagree. Use Individual or a smaller planar region.");
                }
                else basis.Add((axis / length, value / length));
            }
            return basis.Aggregate(Vector3.zero, (sum, b) => sum + b.axis * b.value);
        }

        // Reject actual surface crossings, including across faces. Merely validating each
        // polygon separately would allow a concave offset to pass through a nearby cap.
        internal static void CheckIntersections(MeshDocument document, HashSet<int> changed)
        {
            var positions = document.Vertices.ToDictionary(v => v.Id, v => v.Position);
            var triangles = new List<(int face, Vector3[] p)>();
            foreach (var face in document.Faces)
            {
                var p = face.Corners.Select(c => positions[c.Vertex]).ToArray();
                var indices = MeshTriangulation.FaceTriangles(face, p);
                for (var i = 0; i < indices.Length; i += 3)
                    triangles.Add((face.Id, new[] { p[indices[i]], p[indices[i + 1]], p[indices[i + 2]] }));
            }
            for (var i = 0; i < triangles.Count; i++)
            for (var j = i + 1; j < triangles.Count; j++)
            {
                var a = triangles[i]; var b = triangles[j];
                if (a.face == b.face || !changed.Contains(a.face) && !changed.Contains(b.face)) continue;
                if (Crosses(a.p, b.p)) throw new InvalidOperationException("Generated surfaces overlap or intersect. Reduce the offset.");
            }
        }

        private static bool Crosses(Vector3[] a, Vector3[] b)
        {
            const float tolerance = 1e-6f;
            for (var axis = 0; axis < 3; axis++)
                if (a.Max(p => p[axis]) < b.Min(p => p[axis]) - tolerance || b.Max(p => p[axis]) < a.Min(p => p[axis]) - tolerance) return false;
            var normal = Vector3.Cross(a[1] - a[0], a[2] - a[0]).normalized;
            if (b.All(p => Mathf.Abs(Vector3.Dot(normal, p - a[0])) < tolerance))
            {
                // Separating-axis theorem with strict positive area: shared edges/vertices
                // are valid contacts, coincident triangle interiors are not.
                foreach (var tri in new[] { a, b })
                    for (var i = 0; i < 3; i++)
                    {
                        var axis = Vector3.Cross(normal, tri[(i + 1) % 3] - tri[i]).normalized;
                        if (Mathf.Min(a.Max(p => Vector3.Dot(p, axis)), b.Max(p => Vector3.Dot(p, axis))) -
                            Mathf.Max(a.Min(p => Vector3.Dot(p, axis)), b.Min(p => Vector3.Dot(p, axis))) <= tolerance) return false;
                    }
                return true;
            }
            return Penetrates(a, b) || Penetrates(b, a);
        }

        private static bool Penetrates(Vector3[] edges, Vector3[] tri)
        {
            var n = Vector3.Cross(tri[1] - tri[0], tri[2] - tri[0]).normalized;
            for (var i = 0; i < 3; i++)
            {
                var a = edges[i]; var b = edges[(i + 1) % 3];
                var da = Vector3.Dot(a - tri[0], n); var db = Vector3.Dot(b - tri[0], n);
                if (Mathf.Abs(da - db) < 1e-7f || da * db > 0) continue;
                var t = da / (da - db);
                if (t < 0 || t > 1) continue;
                var p = Vector3.Lerp(a, b, t);
                if (Enumerable.Range(0, 3).All(j => Vector3.Dot(Vector3.Cross(tri[(j + 1) % 3] - tri[j], p - tri[j]), n) > 1e-7f)) return true;
            }
            return false;
        }
    }
}

