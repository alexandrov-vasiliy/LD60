using System;
using System.Linq;
using UnityEngine;

namespace BlenderSceneControls
{
    // Face-local automatic UV0 for generated border/wall surfaces. Authored surfaces retain
    // their UVs. Geometry comes from the candidate; density/offset from the operation
    // snapshot, so preview never accumulates scaling or rewrites imported UV seams.
    internal static class MeshAutoUV
    {
        internal static void UpdateWalls(MeshDocument candidate, MeshDocument snapshot)
        {
            if (!candidate.Faces.Any(f => f.ExtrusionWall)) return;
            if (candidate.UVDimensions[0] == 0) candidate.UVDimensions[0] = 2;
            var positions = candidate.Vertices.ToDictionary(v => v.Id, v => v.Position);
            var oldPositions = snapshot.Vertices.ToDictionary(v => v.Id, v => v.Position);
            var oldFaces = snapshot.Faces.ToDictionary(f => f.Id);
            foreach (var face in candidate.Faces.Where(f => f.ExtrusionWall))
            {
                // A later boundary split can insert corners into a tagged wall without
                // making it part of the cut ring. Keep its automatic UV policy for G/R/S.
                if (face.Corners.Length < 3) throw new InvalidOperationException("Automatic wall UVs require a polygon.");
                var reference = oldFaces.TryGetValue(face.Id, out var original) ? original : face;
                var points = face.Corners.Select(c => positions[c.Vertex]).ToArray();
                var baseEdge = points[1] - points[0];
                var length = baseEdge.magnitude;
                var originalEdge = oldPositions.TryGetValue(reference.Corners[0].Vertex, out var a) &&
                    oldPositions.TryGetValue(reference.Corners[1].Vertex, out var b) ? (b - a).magnitude : length;
                Vector2 uvOrigin = reference.Corners[0].UV[0];
                Vector2 uvEdge = (Vector2)reference.Corners[1].UV[0] - uvOrigin;
                var uvLength = uvEdge.magnitude;
                var density = originalEdge > 1e-6f && uvLength > 1e-6f ? uvLength / originalEdge : 1f;
                var u = uvLength > 1e-6f ? uvEdge / uvLength : Vector2.right;
                var v = new Vector2(-u.y, u.x);
                var axis = length > 1e-6f ? baseEdge / length : Vector3.zero;
                var normal = Vector3.zero;
                for (var i = 0; i < points.Length; i++) normal += Vector3.Cross(points[i] - points[0], points[(i + 1) % points.Length] - points[0]);
                var vertical = Vector3.Cross(normal.normalized, axis).normalized;
                for (var i = 0; i < points.Length; i++)
                {
                    var delta = points[i] - points[0];
                    var uv = uvOrigin + density * (u * Vector3.Dot(delta, axis) + v * Vector3.Dot(delta, vertical));
                    if (!float.IsFinite(uv.x) || !float.IsFinite(uv.y))
                        throw new InvalidOperationException("Automatic UV projection produced a non-finite value.");
                    var value = face.Corners[i].UV[0];
                    value.x = uv.x; value.y = uv.y; // UV0 dimensions 3/4 retain authored z/w.
                    face.Corners[i].UV[0] = value;
                }
            }
        }
    }
}
