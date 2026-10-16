using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BlenderSceneControls
{
    internal sealed class SceneCreation
    {
        internal readonly GameObject[] Roots;
        internal readonly Transform[] Selected;
        internal readonly Transform Active;
        private bool _recorded;

        private SceneCreation(GameObject[] roots, Transform[] selected, Transform active)
        {
            Roots = roots;
            Selected = selected;
            Active = active;
        }

        internal static bool CanTransform(Transform[] selected)
        {
            var stage = StageUtility.GetCurrentStageHandle();
            return selected.Length > 0 && selected.All(t => t && !EditorUtility.IsPersistent(t) &&
                t.gameObject.scene.IsValid() && t.gameObject.scene.isLoaded && stage.Contains(t.gameObject));
        }

        internal static bool CanDuplicate(Transform[] selected)
        {
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            return CanTransform(selected) && (stage == null || !selected.Contains(stage.prefabContentsRoot.transform));
        }

        internal static SceneCreation Duplicate(Transform[] selected, Transform active)
        {
            if (selected == null || selected.Length == 0 || selected.Any(t => !t || EditorUtility.IsPersistent(t)))
                throw new ArgumentException("A scene selection is required.");
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && selected.Contains(stage.prefabContentsRoot.transform))
                throw new ArgumentException("The Prefab Mode contents root cannot be duplicated as a second root.");
            var unique = selected.Distinct().ToArray();
            var originals = unique.Where(t => !unique.Any(other => t != other && t.IsChildOf(other))).ToArray();
            // Native multi-object duplication preserves prefab links and remaps references between copies.
            // Defer creation Undo until the modal operation ends, so unrelated groups are never collapsed.
            var copies = GameObjectUtility.DuplicateGameObjects(originals.Select(t => t.gameObject).ToArray());
            // Unity 6000.3 has no recordUndo overload. Remove only the new copies' native
            // creation records; do not retain/collapse an Undo group across the modal preview.
            foreach (var copy in copies) Undo.ClearUndo(copy);
            if (copies.Length != originals.Length)
            {
                foreach (var copy in copies) if (copy) UnityEngine.Object.DestroyImmediate(copy);
                throw new InvalidOperationException("Unity did not duplicate every selected root.");
            }
            Transform Map(Transform target)
            {
                if (!target) return null;
                var rootIndex = Array.FindIndex(originals, root => target == root || target.IsChildOf(root));
                if (rootIndex < 0) return null;
                var indices = new System.Collections.Generic.Stack<int>();
                for (var node = target; node != originals[rootIndex]; node = node.parent)
                    indices.Push(node.GetSiblingIndex());
                var mapped = copies[rootIndex].transform;
                while (indices.Count > 0) mapped = mapped.GetChild(indices.Pop());
                return mapped;
            }
            return new SceneCreation(copies, unique.Select(Map).ToArray(), Map(active) ?? copies[0].transform);
        }

        internal static SceneCreation Primitive(PrimitiveType type, Vector3 pivot)
        {
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            return Primitive(type, pivot, stage != null ? stage.scene : SceneManager.GetActiveScene(),
                stage != null ? stage.prefabContentsRoot.transform : null);
        }

        internal static SceneCreation Primitive(PrimitiveType type, Vector3 pivot, Scene scene, Transform parent = null)
        {
            if (!scene.IsValid() || !scene.isLoaded || !GrabMath.Finite(pivot) ||
                parent && parent.gameObject.scene != scene || !Enum.IsDefined(typeof(PrimitiveType), type))
                throw new ArgumentException("A loaded destination scene, valid primitive and finite pivot are required.");
            var created = GameObject.CreatePrimitive(type);
            try
            {
                SceneManager.MoveGameObjectToScene(created, scene);
                if (parent) created.transform.SetParent(parent, false);
                created.transform.position = pivot;
                return new SceneCreation(new[] { created }, new[] { created.transform }, created.transform);
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(created);
                throw;
            }
        }

        internal void Select()
        {
            Selection.objects = Selected.Select(t => (UnityEngine.Object)t.gameObject).ToArray();
            Selection.activeGameObject = Active.gameObject;
        }

        internal void RecordUndo(string name)
        {
            if (_recorded) return;
            Undo.IncrementCurrentGroup();
            var group = Undo.GetCurrentGroup();
            try
            {
                Undo.SetCurrentGroupName(name);
                foreach (var root in Roots.Where(root => root))
                {
                    foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                        if (PrefabUtility.IsPartOfPrefabInstance(transform))
                            PrefabUtility.RecordPrefabInstancePropertyModifications(transform);
                    Undo.RegisterCreatedObjectUndo(root, name);
                }
                Undo.FlushUndoRecordObjects();
                Undo.CollapseUndoOperations(group);
                _recorded = true;
            }
            finally { Undo.IncrementCurrentGroup(); }
        }
    }
}
