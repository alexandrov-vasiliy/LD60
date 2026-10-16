using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace BlenderSceneControls
{
    internal sealed class MeshLoopCutTransaction : ModalTargetTransaction
    {
        private readonly MeshEditMode _mode;
        private readonly MeshDocument _before;
        private readonly Mesh _source;
        private readonly Matrix4x4 _world;
        private readonly MeshSelectionMode _selectionMode;
        private int _selectionRevision;
        private readonly int _documentRevision;
        private bool _finished;
        private int _seed;
        private MeshLoopRing _ring;
        private MeshDocument _candidate;
        private Mesh _preview;
        private MeshSurfacePreview _surface;
        internal bool Sliding { get; private set; }
        internal int Count { get; private set; } = 1;
        internal float Factor { get; private set; }
        internal string Message { get; private set; } = "Hover a quad edge";
        internal bool HasCandidate => _candidate != null;
        internal bool Closed => _ring?.Closed == true;
        internal Vector3 RailDirection { get; private set; }
        private Vector3 _pivot;
        internal override Vector3 Pivot => _pivot;
        internal override Quaternion Orientation => _mode.Target.transform.rotation;

        internal MeshLoopCutTransaction(MeshEditMode mode)
        {
            if (!mode.Valid || mode.SelectingBox || mode.Preparing) throw new InvalidOperationException("Mesh target is unavailable.");
            _mode = mode; _before = mode.Transaction.Working.Clone(); _source = mode.Target.sharedMesh;
            _world = mode.Target.transform.localToWorldMatrix;
            Vector3 x = _world.GetColumn(0), y = _world.GetColumn(1), z = _world.GetColumn(2);
            if (Enumerable.Range(0, 16).Any(i => !float.IsFinite(_world[i]) || !float.IsFinite(_world.inverse[i])) ||
                !float.IsFinite(_world.determinant) || _world.determinant == 0 ||
                Mathf.Abs(Vector3.Dot(Vector3.Cross(x.normalized, y.normalized), z.normalized)) < 1e-6f)
                throw new InvalidOperationException("Mesh transform must be finite and invertible.");
            var validation = mode.Transaction.EvaluatePreview(); UnityEngine.Object.DestroyImmediate(validation);
            _selectionMode = mode.Selection.Mode; _selectionRevision = mode.Selection.Revision;
            _documentRevision = _before.Revision;
            _pivot = _world.MultiplyPoint3x4(_before.Vertices.Count == 0 ? Vector3.zero : _before.Vertices[0].Position);
        }

        internal override bool IsValid => !_finished && _mode.Valid && _mode.Target.sharedMesh == _source &&
            _mode.Target.transform.localToWorldMatrix.Equals(_world) && _mode.Selection.Revision == _selectionRevision &&
            _mode.Transaction.Working.Revision == _documentRevision;

        internal void Hover(int seed)
        {
            if (Sliding || seed == _seed) return;
            if (!IsValid) throw new InvalidOperationException("Loop Cut target changed.");
            _seed = seed; _candidate = null; _ring = null;
            try { _ring = MeshLoopCut.Discover(_before, seed); Rebuild(0); }
            catch (InvalidOperationException error) { Message = error.Message; _candidate = null; }
        }

        internal void SetCount(int count)
        {
            if (Sliding) return;
            if (count < 1 || count > MeshLoopCut.MaxCuts) { Message = "Cuts must be an integer from 1 to 32"; return; }
            Count = count;
            if (_ring == null) return;
            try { Rebuild(0); }
            catch (InvalidOperationException error) { Message = error.Message; _candidate = null; }
        }

        private void Rebuild(float factor)
        {
            if (!IsValid) throw new InvalidOperationException("Loop Cut target, selection or transform changed.");
            var candidate = MeshLoopCut.Apply(_before, _ring, Count, factor, Sliding ? _selectionMode : MeshSelectionMode.Edge);
            var mesh = MeshNativeBridge.Evaluate(candidate); mesh.hideFlags = HideFlags.HideAndDontSave;
            if (Sliding)
            {
                _mode.RestoreDocument(candidate, true);
                _selectionRevision = _mode.Selection.Revision;
            }
            if (_preview) UnityEngine.Object.DestroyImmediate(_preview);
            _preview = mesh; _candidate = candidate; Factor = Mathf.Clamp(factor, -.999f, .999f);
            Message = (Closed ? "Closed" : "Open") + " quad ring · " + Count + " cuts";
        }

        internal bool BeginSlide()
        {
            if (!IsValid || !HasCandidate) return false;
            Sliding = true;
            try
            {
                Rebuild(0);
                var edge = _before.Edges.First(e => e.Id == _seed);
                var start = _ring.RailStarts[_seed]; var end = edge.A == start ? edge.B : edge.A;
                RailDirection = _world.MultiplyVector(_before.Vertices.First(v => v.Id == end).Position -
                    _before.Vertices.First(v => v.Id == start).Position);
                _pivot = _world.MultiplyPoint3x4((_before.Vertices.First(v => v.Id == end).Position +
                    _before.Vertices.First(v => v.Id == start).Position) * .5f);
                if (RailDirection.sqrMagnitude < 1e-12f) throw new InvalidOperationException("Slide rail has zero length.");
                _surface = new MeshSurfacePreview(_mode, () => _preview, () => IsValid);
                _mode.SurfacePreview?.Dispose(); _mode.SurfacePreview = _surface;
                return true;
            }
            catch { Cancel(); throw; }
        }
        internal void Slide(float factor)
        {
            if (!Sliding) throw new InvalidOperationException("Confirm the ring before sliding.");
            if (!float.IsFinite(factor)) throw new InvalidOperationException("Slide factor must be finite.");
            var clamped = Mathf.Clamp(factor, -.999f, .999f);
            if (clamped == Factor) return;
            Rebuild(clamped);
        }

        internal void Draw()
        {
            if (!HasCandidate || Sliding || Event.current.type != EventType.Repaint) return;
            var color = Handles.color; var depth = Handles.zTest;
            try
            {
                Handles.color = Color.yellow;
                Handles.zTest = _mode.Selection.XRay ? CompareFunction.Always : CompareFunction.LessEqual;
                var points = _candidate.Vertices.ToDictionary(v => v.Id, v => _world.MultiplyPoint3x4(v.Position));
                foreach (var edge in _candidate.Edges.Where(e => _candidate.SelectedEdges.Contains(e.Id)))
                    Handles.DrawAAPolyLine(3, points[edge.A], points[edge.B]);
                Handles.color = new Color(1, .6f, 0, .65f);
                foreach (var edge in _before.Edges.Where(e => _ring.RailStarts.ContainsKey(e.Id)))
                    Handles.DrawAAPolyLine(2, points[edge.A], points[edge.B]);
            }
            finally { Handles.color = color; Handles.zTest = depth; }
        }

        internal override void PreviewOffset(Vector3 offset) { }
        internal override void PreviewRotation(Vector3 pivot, Quaternion rotation) { }
        internal override void PreviewScale(Vector3 pivot, Quaternion basis, Vector3 factors) { }
        internal override bool Confirm()
        {
            if (!Sliding || !IsValid) { Cancel(); return false; }
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
            try { if (Sliding) _mode.RestoreDocument(_before, true); }
            finally { Finish(); }
        }
        private void Finish()
        {
            _finished = true; _surface?.Dispose();
            if (_mode.SurfacePreview == _surface) _mode.SurfacePreview = null;
            if (_preview) UnityEngine.Object.DestroyImmediate(_preview);
            _preview = null; _candidate = null;
        }
        internal int Pick(Vector2 mouse) => _mode.Pick(mouse, MeshSelectionMode.Edge);
    }
}
