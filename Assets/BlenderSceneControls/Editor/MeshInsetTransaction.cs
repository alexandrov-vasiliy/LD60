using System;
using System.Linq;
using UnityEngine;

namespace BlenderSceneControls
{
    internal sealed class MeshInsetTransaction : ModalTargetTransaction
    {
        private readonly MeshEditMode _mode;
        private readonly MeshDocument _before;
        private readonly Mesh _source;
        private readonly Matrix4x4 _world;
        private int _selectionRevision;
        private readonly int _documentRevision;
        private Mesh _preview;
        private readonly MeshSurfacePreview _surface;
        private bool _finished, _changed;
        internal float Thickness { get; private set; }
        internal float Depth { get; private set; }
        internal InsetOptions Options { get; private set; } = new InsetOptions(false, true, true, false);
        internal bool CanConfirm { get; private set; } = true;
        internal string Message { get; private set; } = "Move or enter thickness";
        internal override Vector3 Pivot { get; }
        internal override Quaternion Orientation => _mode.Target.transform.rotation;

        internal MeshInsetTransaction(MeshEditMode mode)
        {
            if (!mode.Valid || mode.SelectingBox || mode.Preparing || mode.Selection.Mode != MeshSelectionMode.Face || mode.Selection.Selected.Length == 0)
                throw new InvalidOperationException("Select faces for Inset.");
            _mode = mode; _before = mode.Transaction.Working.Clone(); _source = mode.Target.sharedMesh;
            _world = mode.Target.transform.localToWorldMatrix;
            Vector3 x = _world.GetColumn(0), y = _world.GetColumn(1), z = _world.GetColumn(2);
            if (Enumerable.Range(0, 16).Any(i => !float.IsFinite(_world[i]) || !float.IsFinite(_world.inverse[i])) ||
                !float.IsFinite(_world.determinant) || _world.determinant == 0 ||
                Mathf.Abs(Vector3.Dot(Vector3.Cross(x.normalized, y.normalized), z.normalized)) < 1e-6f)
                throw new InvalidOperationException("Mesh transform must be finite and invertible.");
            _selectionRevision = mode.Selection.Revision; _documentRevision = _before.Revision;
            Pivot = _world.MultiplyPoint3x4(mode.Selection.Pivot);
            _preview = mode.Transaction.EvaluatePreview();
            _preview.hideFlags = HideFlags.HideAndDontSave;
            _surface = new MeshSurfacePreview(mode, () => _preview, () => IsValid);
            mode.SurfacePreview?.Dispose(); mode.SurfacePreview = _surface;
        }
        internal override bool IsValid => !_finished && _mode.Valid && _mode.Target.sharedMesh == _source &&
            _mode.Target.transform.localToWorldMatrix.Equals(_world) && _mode.Selection.Revision == _selectionRevision &&
            _mode.Transaction.Working.Revision == _documentRevision;
        internal float LocalPerWorld(Vector3 direction) => _world.inverse.MultiplyVector(direction).magnitude;

        internal void Preview(float thickness, float depth, InsetOptions options)
        {
            if (!IsValid) throw new InvalidOperationException("Inset target, selection or transform changed.");
            Thickness = thickness; Depth = depth; Options = options;
            try
            {
                var candidate = MeshInset.Apply(_before, thickness, depth, options);
                var mesh = MeshNativeBridge.Evaluate(candidate); mesh.hideFlags = HideFlags.HideAndDontSave;
                _changed = MeshDocument.CanonicalJson(candidate) != MeshDocument.CanonicalJson(_before);
                _mode.RestoreDocument(candidate, true); _selectionRevision = _mode.Selection.Revision;
                if (_preview) UnityEngine.Object.DestroyImmediate(_preview);
                _preview = mesh; CanConfirm = true;
                Message = _changed ? "Valid preview" : "Zero / unchanged: no assets or Undo";
            }
            catch (InvalidOperationException error)
            {
                // An invalid value must never confirm the previous valid preview.
                _mode.RestoreDocument(_before, true); _selectionRevision = _mode.Selection.Revision;
                if (_preview) UnityEngine.Object.DestroyImmediate(_preview);
                _preview = null; CanConfirm = false; _changed = false; Message = error.Message;
            }
        }
        internal override void PreviewOffset(Vector3 offset) { }
        internal override void PreviewRotation(Vector3 pivot, Quaternion rotation) { }
        internal override void PreviewScale(Vector3 pivot, Quaternion basis, Vector3 factors) { }
        internal override bool Confirm()
        {
            if (!IsValid || !CanConfirm || !_changed) { Cancel(); return false; }
            try
            {
                var path = _mode.Transaction.OwnedMesh ? null : _mode.Transaction.GeneratedPath();
                _mode.Transaction.Working.Revision++;
                var committed = _mode.Transaction.Commit(path);
                if (!committed) _mode.RestoreDocument(_before, true);
                _mode.RefreshSource();
                return committed;
            }
            catch { _mode.RestoreDocument(_before, true); throw; }
            finally { Finish(); }
        }
        internal override void Cancel()
        {
            if (_finished) return;
            try { _mode.RestoreDocument(_before, true); }
            finally { Finish(); }
        }
        private void Finish()
        {
            _finished = true; _surface.Dispose();
            if (_mode.SurfacePreview == _surface) _mode.SurfacePreview = null;
            if (_preview) UnityEngine.Object.DestroyImmediate(_preview);
            _preview = null;
        }
    }
}
