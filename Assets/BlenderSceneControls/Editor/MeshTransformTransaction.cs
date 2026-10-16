using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BlenderSceneControls
{
    // Geometry uses the complete affine matrix, never the Object Mode TRS approximation.
    internal sealed class MeshTransformTransaction : ModalTargetTransaction
    {
        private readonly MeshEditMode _mode;
        private readonly MeshDocument _snapshot;
        private readonly MeshDocument _rollback;
        internal bool Extruding { get; }
        internal Vector3 RegionNormal { get; }
        private readonly int[] _vertices;
        private readonly Matrix4x4 _world;
        private readonly Matrix4x4 _local;
        private readonly Mesh _source;
        private readonly int _selectionRevision;
        private readonly int _documentRevision;
        private Mesh _preview;
        private readonly MeshSurfacePreview _surface;
        private bool _finished;
        private bool _changed;
        internal Mesh PreviewMesh => _preview;
        internal override Vector3 Pivot { get; }
        internal override Quaternion Orientation { get; }

        internal MeshTransformTransaction(MeshEditMode mode, bool extrude = false)
        {
            _mode = mode;
            if (!mode.Valid || mode.SelectingBox || mode.Preparing) throw new InvalidOperationException("Mesh target is unavailable.");
            if (mode.Selection.Selected.Length == 0) throw new InvalidOperationException("Select mesh elements first.");
            _world = mode.Target.transform.localToWorldMatrix;
            _local = _world.inverse;
            Vector3 x = _world.GetColumn(0), y = _world.GetColumn(1), z = _world.GetColumn(2);
            // A zero ancestor scale can produce a tiny nonzero determinant after float multiplication.
            var volume = Mathf.Abs(Vector3.Dot(Vector3.Cross(x.normalized, y.normalized), z.normalized));
            if (!Finite(_world) || !Finite(_local) || !float.IsFinite(_world.determinant) ||
                _world.determinant == 0 || !float.IsFinite(volume) || volume < 1e-6f)
                throw new InvalidOperationException("Mesh transform must be finite and invertible.");
            _rollback = mode.Transaction.Working.Clone();
            if (_rollback.Faces.Count > MeshEditTransaction.FullPreviewFaceBudget ||
                _rollback.Faces.Sum(f => (long)f.Corners.Length) > MeshEditTransaction.FullPreviewCornerBudget)
                throw new InvalidOperationException("Full document preview exceeds 250 faces / 1500 corners.");
            Extruding = extrude;
            var normal = Vector3.zero;
            _snapshot = extrude ? MeshExtrusion.Apply(_rollback, mode.Selection.Mode, out normal) : _rollback.Clone();
            if (extrude && mode.Selection.Mode == MeshSelectionMode.Face)
            {
                RegionNormal = Mathf.Sign(_world.determinant) * _local.transpose.MultiplyVector(normal).normalized;
                if (!GrabMath.Finite(RegionNormal) || RegionNormal.sqrMagnitude < 1e-12f)
                    throw new InvalidOperationException("Selected region has no stable area normal. Select an open face region.");
            }
            // Evaluate before publishing topology; failure leaves the mode untouched.
            _preview = MeshNativeBridge.Evaluate(_snapshot);
            try
            {
                if (extrude) mode.RestoreDocument(_snapshot, true);
                _vertices = mode.Selection.SelectedVertexIds();
                _source = mode.Target.sharedMesh;
                _selectionRevision = mode.Selection.Revision;
                _documentRevision = _snapshot.Revision;
                Pivot = _world.MultiplyPoint3x4(mode.Selection.Pivot);
                Orientation = mode.Target.transform.rotation;
                if (!GrabMath.Finite(Pivot)) throw new InvalidOperationException("Mesh pivot must be finite.");
                _changed = extrude;
                _preview.hideFlags = HideFlags.HideAndDontSave;
                _surface = new MeshSurfacePreview(mode, () => _preview, () => IsValid);
                mode.SurfacePreview?.Dispose(); mode.SurfacePreview = _surface;
            }
            catch
            {
                mode.RestoreDocument(_rollback, extrude);
                _surface?.Dispose();
                UnityEngine.Object.DestroyImmediate(_preview);
                throw;
            }
        }

        private static bool Finite(Matrix4x4 matrix) => Enumerable.Range(0, 16).All(i => float.IsFinite(matrix[i]));
        internal override bool IsValid => !_finished && _mode.Valid && _mode.Target.sharedMesh == _source &&
            _mode.Target.transform.localToWorldMatrix.Equals(_world) &&
            _mode.Selection.Revision == _selectionRevision && _mode.Selection.Document.Revision == _documentRevision;

        private void Apply(Func<Vector3, Vector3> transform, bool identity)
        {
            if (!IsValid) throw new InvalidOperationException("Mesh target, selection or transform changed.");
            var candidate = _snapshot.Clone();
            var selected = new System.Collections.Generic.HashSet<int>(_vertices);
            foreach (var vertex in candidate.Vertices.Where(v => selected.Contains(v.Id)))
            {
                if (identity) continue;
                vertex.Position = _local.MultiplyPoint3x4(transform(_world.MultiplyPoint3x4(vertex.Position)));
                if (!GrabMath.Finite(vertex.Position)) throw new ArgumentException("Resulting mesh position must be finite.");
            }
            // Preserve authored corners; generated walls refresh automatic UV0 from the snapshot.
            // No global normal/tangent recalculation or rewrite of imported UV islands.
            MeshAutoUV.UpdateWalls(candidate, _snapshot);
            var mesh = MeshNativeBridge.Evaluate(candidate);
            mesh.hideFlags = HideFlags.HideAndDontSave;
            _changed = Extruding || candidate.Vertices.Where((v, i) => !v.Position.Equals(_snapshot.Vertices[i].Position)).Any();
            _mode.RestoreDocument(candidate);
            UnityEngine.Object.DestroyImmediate(_preview);
            _preview = mesh;
        }

        internal override void PreviewOffset(Vector3 offset)
        {
            if (!GrabMath.Finite(offset)) throw new ArgumentException("Offset must be finite.");
            Apply(p => p + offset, offset.Equals(Vector3.zero));
        }

        internal override void PreviewRotation(Vector3 pivot, Quaternion rotation)
        {
            if (!GrabMath.Finite(pivot) || !float.IsFinite(rotation.x) || !float.IsFinite(rotation.y) ||
                !float.IsFinite(rotation.z) || !float.IsFinite(rotation.w) ||
                !float.IsFinite(Quaternion.Dot(rotation, rotation)) || Quaternion.Dot(rotation, rotation) < 1e-6f)
                throw new ArgumentException("Rotation must be finite and nonzero.");
            rotation = rotation.normalized;
            Apply(p => pivot + rotation * (p - pivot), rotation.Equals(Quaternion.identity));
        }

        internal override void PreviewScale(Vector3 pivot, Quaternion basis, Vector3 factors)
        {
            if (!GrabMath.Finite(pivot) || !GrabMath.Finite(factors) || !float.IsFinite(basis.x) ||
                !float.IsFinite(basis.y) || !float.IsFinite(basis.z) || !float.IsFinite(basis.w) ||
                !float.IsFinite(Quaternion.Dot(basis, basis)) || Quaternion.Dot(basis, basis) < 1e-6f)
                throw new ArgumentException("Scale and basis must be finite.");
            basis = basis.normalized;
            var inverse = Quaternion.Inverse(basis);
            Apply(p => pivot + basis * Vector3.Scale(inverse * (p - pivot), factors), factors.Equals(Vector3.one));
        }

        internal override bool Confirm() => Confirm(null, true);
        internal bool Confirm(string path, bool choosePath = false)
        {
            if (!IsValid) { Cancel(); return false; }
            if (!_changed) { Cancel(); return false; }
            try
            {
                if (!_mode.Transaction.OwnedMesh && choosePath)
                    path = _mode.Transaction.GeneratedPath();
                if (!IsValid) { Cancel(); return false; }
                _mode.Transaction.Working.Revision++;
                var committed = _mode.Transaction.Commit(path);
                if (!committed) _mode.RestoreDocument(_rollback, Extruding);
                _mode.RefreshSource();
                return committed;
            }
            catch { _mode.RestoreDocument(_rollback, Extruding); throw; }
            finally { Finish(); }
        }

        internal override void Cancel()
        {
            if (_finished) return;
            try { _mode.RestoreDocument(_rollback, Extruding); }
            finally { Finish(); }
        }
        internal bool CancelMove(string path = null, bool generated = true)
        {
            if (!Extruding) throw new InvalidOperationException("Cancel Move requires extrusion.");
            if (!IsValid) { Cancel(); return false; }
            PreviewOffset(Vector3.zero);
            return Confirm(path, generated);
        }
        private void Finish()
        {
            _finished = true;
            _surface?.Dispose();
            if (_mode.SurfacePreview == _surface) _mode.SurfacePreview = null;
            if (_preview) UnityEngine.Object.DestroyImmediate(_preview);
            _preview = null;
        }
    }
}
