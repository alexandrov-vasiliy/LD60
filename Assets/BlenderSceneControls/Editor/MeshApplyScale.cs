using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BlenderSceneControls
{
    internal static class MeshApplyScale
    {
        internal static string Unavailable(MeshFilter target)
        {
            if (!target || !target.sharedMesh || !target.gameObject.scene.IsValid() || EditorUtility.IsPersistent(target))
                return "Select one scene object with a static MeshFilter.";
            if (EditorApplication.isPlayingOrWillChangePlaymode || PrefabStageUtility.GetCurrentPrefabStage() != null)
                return "Apply Scale is unavailable in Play Mode or Prefab Stage.";
            if (target.transform.childCount != 0)
                return "Apply Scale currently requires an object without children; child transforms must remain unchanged.";
            if (target.GetComponent<SkinnedMeshRenderer>() || target.GetComponent<Animator>() || target.GetComponent<Animation>() ||
                target.GetComponent<Camera>() || target.GetComponent<Light>() || target.GetComponent<ParticleSystem>() ||
                target.GetComponents<Renderer>().Any(r => !(r is MeshRenderer)))
                return "Apply Scale supports static mesh objects without animated or other scaled visual components.";
            if (target.GetComponents<Collider>().Any(c => !(c is BoxCollider) && (!(c is MeshCollider mesh) || mesh.sharedMesh != target.sharedMesh)))
                return "Apply Scale supports BoxCollider and matching MeshCollider; other collider shapes are unavailable.";
            if (target.GetComponents<Component>().Any(c => c is UnityEngine.Animations.IConstraint))
                return "Apply Scale is unavailable on constrained objects.";
            var s = target.transform.localScale;
            var world = target.transform.localToWorldMatrix;
            if (!GrabMath.Finite(s) || Mathf.Min(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z)) < 1e-6f ||
                !float.IsFinite(world.determinant) || world.determinant == 0 ||
                Enumerable.Range(0, 16).Any(i => !float.IsFinite(world[i]) || !float.IsFinite(world.inverse[i])))
                return "Apply Scale requires finite, nonzero scale and an invertible transform.";
            if (target.GetComponent<BoxCollider>())
            {
                Vector3 x = world.GetColumn(0), y = world.GetColumn(1), z = world.GetColumn(2);
                if (Mathf.Abs(Vector3.Dot(x.normalized,y.normalized)) > .0001f ||
                    Mathf.Abs(Vector3.Dot(x.normalized,z.normalized)) > .0001f ||
                    Mathf.Abs(Vector3.Dot(y.normalized,z.normalized)) > .0001f)
                    return "Apply Scale with a sheared BoxCollider is unavailable.";
            }
            return null;
        }

        internal static MeshDocument Bake(MeshDocument source, Vector3 scale)
        {
            if (!GrabMath.Finite(scale) || Mathf.Min(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)) < 1e-6f)
                throw new InvalidOperationException("Apply Scale requires finite, nonzero scale.");
            var result = source.Clone();
            var reflected = Mathf.Sign(scale.x) * Mathf.Sign(scale.y) * Mathf.Sign(scale.z) < 0;
            foreach (var v in result.Vertices) v.Position = Vector3.Scale(v.Position, scale);
            foreach (var face in result.Faces)
            {
                foreach (var c in face.Corners)
                {
                    if (result.Normals) c.Normal = new Vector3(c.Normal.x / scale.x, c.Normal.y / scale.y, c.Normal.z / scale.z).normalized;
                    if (result.Tangents)
                    {
                        var direction = Vector3.Scale(new Vector3(c.Tangent.x, c.Tangent.y, c.Tangent.z), scale).normalized;
                        c.Tangent = new Vector4(direction.x, direction.y, direction.z, c.Tangent.w * (reflected ? -1 : 1));
                    }
                }
                // Unity normally compensates mirrored object transforms in rendering.
                // Once that sign lives in the mesh, winding must carry the compensation.
                if (reflected) Array.Reverse(face.Corners);
            }
            // Baking keeps the same world surface and texture: leave every UV channel
            // literal, including generated islands. Later G/R/S uses this new baseline.
            result.Revision++;
            result.Validate();
            return result;
        }

        internal static bool Apply(MeshFilter target, MeshEditMode mode = null)
        {
            var reason = Unavailable(target);
            if (reason != null) throw new InvalidOperationException(reason);
            if (mode != null && (!mode.Valid || mode.Target != target || mode.Preparing || mode.SelectingBox))
                throw new InvalidOperationException("Finish the current mesh operation first.");
            var scale = target.transform.localScale;
            if (scale.Equals(Vector3.one)) return false;
            // Always fork, even for owned assets. The old on-disk scene still references
            // its old mesh and scale until the user explicitly saves that scene.
            using (var transaction = new MeshEditTransaction(target, forceCopy: true))
            {
                var before = mode != null ? mode.Transaction.Working.Clone() : transaction.Working.Clone();
                transaction.RestoreWorking(Bake(before, scale));
                var boxes = target.GetComponents<BoxCollider>();
                var centers = boxes.Select(c => c.center).ToArray();
                var sizes = boxes.Select(c => c.size).ToArray();
                var path = transaction.GeneratedPath();
                bool committed;
                try
                {
                    committed = transaction.Commit(path, () =>
                {
                    Undo.RegisterCompleteObjectUndo(new UnityEngine.Object[] { target.transform }.Concat(boxes).ToArray(), "Apply Scale");
                    target.transform.localScale = Vector3.one;
                    var absolute = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                    for (var i = 0; i < boxes.Length; i++)
                    {
                        boxes[i].center = Vector3.Scale(centers[i], scale);
                        boxes[i].size = Vector3.Scale(sizes[i], absolute);
                        if (PrefabUtility.IsPartOfPrefabInstance(boxes[i]))
                            PrefabUtility.RecordPrefabInstancePropertyModifications(boxes[i]);
                    }
                    if (PrefabUtility.IsPartOfPrefabInstance(target.transform))
                        PrefabUtility.RecordPrefabInstancePropertyModifications(target.transform);
                    Undo.SetCurrentGroupName("Apply Scale");
                    });
                }
                catch
                {
                    // Commit's Undo rollback reloads registered M02 transactions from
                    // source data. Restore the idle mode's detached preparation/selection.
                    if (mode != null)
                    {
                        mode.Transaction.ReloadTarget();
                        mode.RefreshSource();
                        mode.RestoreDocument(before, true);
                    }
                    throw;
                }
                if (committed && mode != null)
                {
                    mode.Transaction.ReloadTarget();
                    mode.RefreshSource();
                    mode.RestoreDocument(transaction.Working, true);
                }
                return committed;
            }
        }
    }
}
