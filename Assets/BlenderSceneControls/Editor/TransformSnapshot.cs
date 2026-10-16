using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BlenderSceneControls
{
    internal sealed class TransformSnapshot
    {
        internal readonly Transform Target;
        internal readonly Transform Parent;
        internal readonly int SceneHandle;
        internal readonly Vector3 LocalPosition;
        internal readonly Quaternion LocalRotation;
        internal readonly Vector3 LocalScale;
        internal readonly Vector3 WorldPosition;
        internal readonly Quaternion WorldRotation;
        internal readonly Matrix4x4 WorldMatrix;
        private readonly PropertyModification[] _prefabModifications;

        internal TransformSnapshot(Transform target)
        {
            Target = target;
            Parent = target.parent;
            SceneHandle = target.gameObject.scene.handle;
            LocalPosition = target.localPosition;
            LocalRotation = target.localRotation;
            LocalScale = target.localScale;
            WorldPosition = target.position;
            WorldRotation = target.rotation;
            WorldMatrix = target.localToWorldMatrix;
            if (PrefabUtility.IsPartOfPrefabInstance(target))
                _prefabModifications = PrefabUtility.GetPropertyModifications(target);
        }

        internal bool IsValid => Target && Target.parent == Parent && Target.gameObject.scene.handle == SceneHandle;
        internal bool IsChanged => Target &&
            (!Target.localPosition.Equals(LocalPosition) || !Target.localRotation.Equals(LocalRotation) ||
             !Target.localScale.Equals(LocalScale));

        internal void Restore()
        {
            // Do not reparent or resurrect an object changed by another editor action.
            if (!IsValid) return;
            if (!Target.localPosition.Equals(LocalPosition)) Target.localPosition = LocalPosition;
            if (!Target.localRotation.Equals(LocalRotation)) Target.localRotation = LocalRotation;
            if (!Target.localScale.Equals(LocalScale)) Target.localScale = LocalScale;
        }

        internal void RestorePrefabModifications()
        {
            if (IsValid && PrefabUtility.IsPartOfPrefabInstance(Target))
                PrefabUtility.SetPropertyModifications(Target, _prefabModifications);
        }
    }

    internal sealed class TransformTransaction : ModalTargetTransaction
    {
        internal readonly TransformSnapshot[] Snapshots;
        internal readonly TransformSnapshot[] Roots;
        internal override Vector3 Pivot { get; }
        internal override Quaternion Orientation { get; }
        internal bool Finished { get; private set; }
        internal int UndoGroup { get; private set; } = -1;
        private readonly string _undoName;
        private readonly SceneCreation _creation;

        internal TransformTransaction(Transform[] selected, string undoName = "Blender Scene Controls Diagnostic", SceneCreation creation = null,
            Transform active = null, bool center = false)
        {
            _undoName = undoName;
            _creation = creation;
            if (selected == null || selected.Length == 0 || selected.Any(t => !t || EditorUtility.IsPersistent(t)))
                throw new ArgumentException("A scene Transform selection is required.");
            var unique = selected.Distinct().ToArray();
            Snapshots = unique.Select(t => new TransformSnapshot(t)).ToArray();
            Roots = Snapshots.Where(s => !HasSelectedAncestor(s.Target, unique)).ToArray();
            Pivot = center ? Snapshots.Aggregate(Vector3.zero, (sum, snapshot) => sum + snapshot.WorldPosition) / Snapshots.Length
                : active ? active.position : Roots[0].WorldPosition;
            Orientation = active ? active.rotation : Quaternion.identity;
        }

        private static bool HasSelectedAncestor(Transform target, Transform[] selected)
        {
            for (var parent = target.parent; parent; parent = parent.parent)
                if (Array.IndexOf(selected, parent) >= 0) return true;
            return false;
        }

        internal override bool IsValid => Snapshots.All(s => s.IsValid);

        // Absolute world offset supplied by the active operation; never accumulate on the target.
        internal override void PreviewOffset(Vector3 offset)
        {
            if (Finished || !IsValid) throw new InvalidOperationException("The session is no longer valid.");
            if (!float.IsFinite(offset.x) || !float.IsFinite(offset.y) || !float.IsFinite(offset.z))
                throw new ArgumentException("Offset must be finite.");
            foreach (var snapshot in Roots)
            {
                if (offset == Vector3.zero) snapshot.Restore();
                else
                {
                    var position = snapshot.WorldPosition + offset;
                    if (!GrabMath.Finite(position)) throw new ArgumentException("Resulting position must be finite.");
                    if (!snapshot.Target.position.Equals(position)) snapshot.Target.position = position;
                }
            }
        }

        internal override void PreviewRotation(Vector3 pivot, Quaternion rotation)
        {
            if (Finished || !IsValid) throw new InvalidOperationException("The session is no longer valid.");
            if (!GrabMath.Finite(pivot) || !float.IsFinite(rotation.x) || !float.IsFinite(rotation.y) ||
                !float.IsFinite(rotation.z) || !float.IsFinite(rotation.w) || Quaternion.Dot(rotation, rotation) < 0.000001f)
                throw new ArgumentException("Rotation must be finite and nonzero.");
            rotation = rotation.normalized;
            var positions = Roots.Select(s => pivot + rotation * (s.WorldPosition - pivot)).ToArray();
            if (positions.Any(p => !GrabMath.Finite(p))) throw new ArgumentException("Result must be finite.");
            for (var i = 0; i < Roots.Length; i++)
            {
                var snapshot = Roots[i];
                if (rotation == Quaternion.identity) snapshot.Restore();
                else
                {
                    snapshot.Target.position = positions[i];
                    snapshot.Target.rotation = rotation * snapshot.WorldRotation;
                }
            }
        }

        internal override void PreviewScale(Vector3 pivot, Quaternion basis, Vector3 factors)
        {
            if (Finished || !IsValid) throw new InvalidOperationException("The session is no longer valid.");
            if (!GrabMath.Finite(pivot) || !GrabMath.Finite(factors)) throw new ArgumentException("Scale must be finite.");
            var inverse = Quaternion.Inverse(basis);
            Vector3 Scaled(Vector3 vector) => basis * Vector3.Scale(inverse * vector, factors);
            var positions = Roots.Select(s => pivot + Scaled(s.WorldPosition - pivot)).ToArray();
            var scales = Roots.Select(s =>
            {
                var multipliers = Vector3.one;
                for (var axis = 0; axis < 3; axis++)
                {
                    Vector3 direction = s.WorldMatrix.GetColumn(axis);
                    if (direction.sqrMagnitude < 0.00000001f)
                        direction = s.WorldRotation * (axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward);
                    direction.Normalize();
                    // Keep Unity TRS: project the affine result onto each original local basis vector.
                    // Off-diagonal shear cannot be represented without changing the hierarchy/geometry.
                    multipliers[axis] = Vector3.Dot(direction, Scaled(direction));
                }
                return Vector3.Scale(s.LocalScale, multipliers);
            }).ToArray();
            if (positions.Any(p => !GrabMath.Finite(p)) || scales.Any(s => !GrabMath.Finite(s)))
                throw new ArgumentException("Result must be finite.");
            for (var i = 0; i < Roots.Length; i++)
            {
                var snapshot = Roots[i];
                if (factors == Vector3.one) snapshot.Restore();
                else
                {
                    snapshot.Target.position = positions[i];
                    snapshot.Target.localScale = scales[i];
                }
            }
        }

        internal override void Cancel()
        {
            if (Finished) return;
            try
            {
                foreach (var snapshot in Roots) snapshot.Restore();
            }
            finally
            {
                try { _creation?.RecordUndo(_undoName); }
                finally { Finished = true; }
            }
        }

        internal override bool Confirm()
        {
            if (Finished) return false;
            if (!IsValid) { Cancel(); return false; }
            if (_creation != null)
            {
                try { _creation.RecordUndo(_undoName); return true; }
                finally { Finished = true; }
            }
            var changed = Roots.Where(s => s.IsChanged).ToArray();
            if (changed.Length == 0) { Finished = true; return false; }
            var final = changed.Select(s => new TransformSnapshot(s.Target)).ToArray();
            foreach (var snapshot in changed) snapshot.Restore();
            // No Undo is registered during preview. Cancellation cannot revert unrelated work.
            Undo.IncrementCurrentGroup();
            UndoGroup = Undo.GetCurrentGroup();
            try
            {
                Undo.SetCurrentGroupName(_undoName);
                Undo.RegisterCompleteObjectUndo(changed.Select(s => (UnityEngine.Object)s.Target).ToArray(),
                    _undoName);
                foreach (var snapshot in final)
                {
                    snapshot.Restore();
                    if (PrefabUtility.IsPartOfPrefabInstance(snapshot.Target))
                        PrefabUtility.RecordPrefabInstancePropertyModifications(snapshot.Target);
                }
                Undo.FlushUndoRecordObjects();
                Undo.CollapseUndoOperations(UndoGroup);
                Finished = true;
                return true;
            }
            catch
            {
                Undo.RevertAllDownToGroup(UndoGroup);
                foreach (var snapshot in changed)
                {
                    snapshot.Restore();
                    snapshot.RestorePrefabModifications();
                }
                Finished = true;
                throw;
            }
            finally { Undo.IncrementCurrentGroup(); }
        }
    }
}
