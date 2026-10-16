using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BlenderSceneControls
{
    // M02 data transaction only: no mode, picking, toolbar or mesh operator integration.
    internal sealed class MeshEditTransaction : IDisposable
    {
        private readonly MeshFilter _target;
        private readonly bool _forceCopy;
        private Mesh _expectedSource;
        private MeshDocument _baseline;
        private Mesh _ownedMesh;
        private MeshDocumentAsset _metadata;
        private bool _committing;
        internal MeshDocument Working { get; private set; }
        internal MeshDocumentAsset Metadata => _metadata;
        internal Mesh OwnedMesh => _ownedMesh;
        // Test seams are invoked before/after each individual persistence boundary, never during preview.
        internal static Action<string> PersistenceFault;

        internal MeshEditTransaction(MeshFilter target, bool forceCopy = false)
        {
            if (!target || !target.sharedMesh || EditorApplication.isPlayingOrWillChangePlaymode ||
                EditorUtility.IsPersistent(target) || target.GetComponent<SkinnedMeshRenderer>())
                throw new InvalidOperationException("A scene static MeshFilter in Edit Mode is required.");
            if (PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Prefab Stage editing is not enabled until its persistence spike passes.");
            if (target.GetComponents<MeshCollider>().Any(c => c.sharedMesh == target.sharedMesh && c.convex))
                throw new InvalidOperationException("Convex collider cooking is not supported by this data spike.");
            var renderer = target.GetComponent<MeshRenderer>();
            if (renderer && renderer.additionalVertexStreams)
                throw new InvalidOperationException("Additional vertex streams require an explicit attribute policy.");
            _target = target;
            _forceCopy = forceCopy;
            ReadTarget();
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        private void ReadTarget()
        {
            _expectedSource = _target.sharedMesh;
            var metadata = MeshDocumentAsset.Find(_expectedSource);
            if (_expectedSource.name.StartsWith("BSC Mesh ", StringComparison.Ordinal) && metadata == null)
                throw new InvalidOperationException("Editable mesh metadata is missing. Explicit triangle reimport is required.");
            _baseline = metadata ? metadata.Data.Clone() : MeshNativeBridge.Import(_expectedSource);
            Working = _baseline.Clone();
            var resume = !_forceCopy && metadata && MeshOwnership.IsOwner(_target, _expectedSource) && !MeshOwnership.IsShared(_target, _expectedSource);
            _ownedMesh = resume ? _expectedSource : null;
            _metadata = resume ? metadata : null;
        }

        internal void ReloadTarget() => ReadTarget();

        private void OnUndoRedo()
        {
            if (!_committing && _target && _target.sharedMesh) ReadTarget();
        }

        internal const int FullPreviewFaceBudget = 250;
        internal const int FullPreviewCornerBudget = 1500;
        internal Mesh EvaluatePreview()
        {
            if (Working.Faces.Count > FullPreviewFaceBudget || Working.Faces.Sum(f => (long)f.Corners.Length) > FullPreviewCornerBudget)
                throw new InvalidOperationException("Full document preview exceeds 250 faces / 1500 corners. A cached position-update path is required for larger interactive previews.");
            return MeshNativeBridge.Evaluate(Working);
        }

        internal void Cancel() { Working = _baseline.Clone(); }

        internal void RestoreWorking(MeshDocument snapshot) { Working = snapshot.Clone(); }

        // Called only by a changed Confirm; reserve the whole pair, including orphan meta files.
        internal string GeneratedPath()
        {
            const string folder = "Assets/BlenderSceneControlsMeshes";
            if (!AssetDatabase.IsValidFolder(folder))
            {
                if (File.Exists(folder) || Directory.Exists(folder))
                    throw new IOException("Generated mesh folder is occupied or has not been imported.");
                if (string.IsNullOrEmpty(AssetDatabase.CreateFolder("Assets", "BlenderSceneControlsMeshes")))
                    throw new IOException("Could not create the generated mesh folder.");
            }
            var name = new string(_target.name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()).Trim();
            if (name.Length > 80) name = name.Substring(0, 80);
            // Keep names outside ValidatePath's reserved Editor/Dev directory policy.
            name = "BSC Mesh " + (name.Length == 0 ? "Object" : name);
            name = System.Text.RegularExpressions.Regex.Replace(name, "Dev", "D_v", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            for (var i = 0; ; i++)
            {
                var stem = name + (i == 0 ? "" : " " + i);
                var native = folder + "/" + stem + ".asset";
                var companion = folder + "/Editor/" + stem + ".meshdoc.asset";
                if (new[] { native, companion }.All(p => !File.Exists(p) && !File.Exists(p + ".meta") &&
                    !Directory.Exists(p) && !AssetDatabase.LoadMainAssetAtPath(p)) && !MeshPairRecovery.IsPending(native))
                    return native;
            }
        }

        internal bool Commit(string newMeshPath, Action afterReferences = null, bool copyUnchanged = false)
        {
            if (!_target || _target.sharedMesh != _expectedSource)
                throw new InvalidOperationException("Mesh target/reference changed during the operation.");
            if (!copyUnchanged && JsonUtility.ToJson(Working) == JsonUtility.ToJson(_baseline)) return false;
            if (copyUnchanged && !_forceCopy) throw new InvalidOperationException("Explicit Save As requires a new independent pair.");
            var first = !_ownedMesh;
            if (!first && (!MeshOwnership.IsOwner(_target, _ownedMesh) || MeshOwnership.IsShared(_target, _ownedMesh)))
                throw new InvalidOperationException("The editable mesh became shared. Start a fresh copy-on-edit transaction with a new asset path.");
            if (!first) MeshDocumentAsset.Find(_ownedMesh);
            if (!first && (EditorUtility.IsDirty(_ownedMesh) || EditorUtility.IsDirty(_metadata)))
                throw new InvalidOperationException("The editable pair has unsaved external changes. Persist or resolve them before starting a durable commit.");
            if (!first) ValidatePath(AssetDatabase.GetAssetPath(_ownedMesh), false);
            if (first && string.IsNullOrEmpty(newMeshPath)) { Cancel(); return false; }
            string metadataPath = null;
            if (first)
            {
                newMeshPath = ValidatePath(newMeshPath);
                if (copyUnchanged && (File.Exists(newMeshPath + ".meta") || Directory.Exists(newMeshPath) || AssetDatabase.LoadMainAssetAtPath(newMeshPath)))
                    throw new InvalidOperationException("Save As requires an unused Mesh path, including its meta file.");
                metadataPath = Path.GetDirectoryName(newMeshPath).Replace('\\', '/') + "/Editor/" + Path.GetFileNameWithoutExtension(newMeshPath) + ".meshdoc.asset";
                if (File.Exists(metadataPath) || AssetDatabase.LoadMainAssetAtPath(metadataPath) || copyUnchanged && (File.Exists(metadataPath + ".meta") || Directory.Exists(metadataPath)))
                    throw new InvalidOperationException("Metadata path already exists.");
            }
            using (var result = new TemporaryMesh(MeshNativeBridge.Evaluate(Working)))
            {
                // Validate everything before creating assets or recording Undo.
                var fingerprint = MeshNativeBridge.Fingerprint(result.Mesh);
                var colliders = _target.GetComponents<MeshCollider>().Where(c => c.sharedMesh == _expectedSource).ToArray();
                if (colliders.Any(c => c.convex)) throw new InvalidOperationException("Convex collider is unsupported.");
                if (colliders.Any(c => c.GetComponentInParent<Rigidbody>() && !c.GetComponentInParent<Rigidbody>().isKinematic))
                    throw new InvalidOperationException("A non-convex MeshCollider requires a static or kinematic Rigidbody target.");
                foreach (var collider in colliders)
                {
                    var matrix = collider.transform.localToWorldMatrix;
                    if (matrix.determinant == 0 || float.IsNaN(matrix.determinant) || float.IsInfinity(matrix.determinant) ||
                        Enumerable.Range(0, 16).Any(i => float.IsNaN(matrix[i]) || float.IsInfinity(matrix[i])))
                        throw new InvalidOperationException("MeshCollider transform must be finite and invertible.");
                    Vector3 x = matrix.GetColumn(0), y = matrix.GetColumn(1), z = matrix.GetColumn(2);
                    if (Mathf.Abs(Vector3.Dot(x.normalized, y.normalized)) > .0001f ||
                        Mathf.Abs(Vector3.Dot(x.normalized, z.normalized)) > .0001f ||
                        Mathf.Abs(Vector3.Dot(y.normalized, z.normalized)) > .0001f)
                        throw new InvalidOperationException("Sheared MeshCollider transforms are unsupported; native shape parity has not passed.");
                }
                foreach (var collider in colliders)
                    ValidateCooking(result.Mesh, collider.cookingOptions);
                var pairPath = first ? newMeshPath : AssetDatabase.GetAssetPath(_ownedMesh);
                if (MeshPairRecovery.IsPending(pairPath))
                    throw new IOException("An earlier commit requires explicit recovery before reusing this path.");
                Undo.IncrementCurrentGroup();
                var group = Undo.GetCurrentGroup();
                _committing = true;
                try
                {
                    MeshPairRecovery.Begin(pairPath, first ? metadataPath : AssetDatabase.GetAssetPath(_metadata),
                        first ? _target : null, first ? colliders : null);
                    if (first)
                    {
                        _ownedMesh = MeshNativeBridge.Evaluate(_baseline);
                        _ownedMesh.name = Path.GetFileNameWithoutExtension(newMeshPath);
                        PersistenceFault?.Invoke("before-native-create");
                        AssetDatabase.CreateAsset(_ownedMesh, newMeshPath);
                        PersistenceFault?.Invoke("after-native-create-before-guid");
                        MeshPairRecovery.RecordCreatedIdentity(newMeshPath);
                        PersistenceFault?.Invoke("after-native-create");
                        var directory = Path.GetDirectoryName(metadataPath).Replace('\\', '/');
                        if (!AssetDatabase.IsValidFolder(directory))
                            AssetDatabase.CreateFolder(Path.GetDirectoryName(directory).Replace('\\', '/'), "Editor");
                        _metadata = ScriptableObject.CreateInstance<MeshDocumentAsset>();
                        _metadata.MeshGuid = AssetDatabase.AssetPathToGUID(newMeshPath);
                        _metadata.Data = _baseline.Clone();
                        _metadata.Fingerprint = MeshNativeBridge.Fingerprint(_ownedMesh);
                        _metadata.DocumentFingerprint = MeshDocumentAsset.HashDocument(_metadata.Data);
                        PersistenceFault?.Invoke("before-document-create");
                        AssetDatabase.CreateAsset(_metadata, metadataPath);
                        PersistenceFault?.Invoke("after-document-create");
                        MeshOwnership.Write(_ownedMesh, MeshOwnership.Create(_target, _metadata));
                        _ownedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(newMeshPath);
                    }
                    var identity = MeshOwnership.Read(_ownedMesh);
                    identity.Pending = true;
                    MeshOwnership.Write(_ownedMesh, identity);
                    var objects = new List<Object> { _ownedMesh, _metadata, _target };
                    objects.AddRange(colliders);
                    Undo.SetCurrentGroupName("Edit Mesh");
                    Undo.RegisterCompleteObjectUndo(objects.ToArray(), "Edit Mesh");
                    MeshNativeBridge.CopyGeometry(result.Mesh, _ownedMesh);
                    _ownedMesh.name = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(_ownedMesh));
                    _metadata.Data = Working.Clone();
                    _metadata.Fingerprint = fingerprint;
                    _metadata.DocumentFingerprint = MeshDocumentAsset.HashDocument(_metadata.Data);
                    _target.sharedMesh = _ownedMesh;
                    foreach (var collider in colliders) RebindCollider(collider, _ownedMesh);
                    foreach (var component in new Component[] { _target }.Concat(colliders))
                        if (PrefabUtility.IsPartOfPrefabInstance(component)) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                    afterReferences?.Invoke();
                    EditorUtility.SetDirty(_ownedMesh);
                    EditorUtility.SetDirty(_metadata);
                    EditorSceneManager.MarkSceneDirty(_target.gameObject.scene);
                    SavePair(_ownedMesh, _metadata, true);
                    identity.Pending = false;
                    MeshOwnership.Write(_ownedMesh, identity);
                    MeshPairRecovery.Complete(pairPath);
                    Undo.FlushUndoRecordObjects();
                    Undo.CollapseUndoOperations(group);
                    _expectedSource = _ownedMesh;
                    _baseline = Working.Clone();
                    return true;
                }
                catch
                {
                    try
                    {
                        MeshDocumentUndoPersistence.Suppress = true;
                        try { Undo.RevertAllDownToGroup(group); }
                        finally { MeshDocumentUndoPersistence.Suppress = false; }
                        if (first)
                        {
                            // A failed commit must not leave assets; successful Undo intentionally retains them.
                            if (_ownedMesh) MeshOwnership.ClearPending(_ownedMesh);
                            if (MeshPairRecovery.IsPending(pairPath)) MeshPairRecovery.Recover(pairPath);
                            _metadata = null;
                            _ownedMesh = null;
                        }
                        else
                        {
                            // Undo restores memory, but an earlier save may already have reached disk.
                            // If recovery itself fails, keep Pending durable and refuse the pair on reopen.
                            if (MeshPairRecovery.IsPending(pairPath)) MeshPairRecovery.Recover(pairPath);
                        }
                    }
                    finally { Cancel(); }
                    throw;
                }
                finally { _committing = false; Undo.IncrementCurrentGroup(); }
            }
        }

        private static void SavePair(Mesh mesh, MeshDocumentAsset metadata, bool inject)
        {
            if (inject) PersistenceFault?.Invoke("before-native-save");
            EditorUtility.SetDirty(mesh);
            SaveOwnedAsset(mesh);
            if (inject) PersistenceFault?.Invoke("after-native-save");
            EditorUtility.SetDirty(metadata);
            SaveOwnedAsset(metadata);
            if (inject) PersistenceFault?.Invoke("after-document-save");
        }

        internal static void SaveOwnedAsset(Object asset)
        {
            string error = null;
            Application.LogCallback capture = (message, stack, type) =>
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) error = message;
            };
            Application.logMessageReceived += capture;
            try { AssetDatabase.SaveAssetIfDirty(asset); }
            finally { Application.logMessageReceived -= capture; }
            if (error != null || EditorUtility.IsDirty(asset))
                throw new IOException("Could not save owned mesh asset " + AssetDatabase.GetAssetPath(asset) + ": " + error);
        }

        internal static void RebindCollider(MeshCollider collider, Mesh mesh)
        {
            // Assigning the same reference leaves the old cooked shape on the supported Editor.
            collider.sharedMesh = null;
            collider.sharedMesh = mesh;
        }

        internal static void ValidateCooking(Mesh mesh, MeshColliderCookingOptions options)
        {
            if (Enumerable.Range(0, mesh.subMeshCount).Sum(i => (long)mesh.GetIndexCount(i)) == 0)
                throw new InvalidOperationException("An empty render mesh cannot replace an existing MeshCollider.");
            // BakeMesh returns void on the supported Editor. Capture its synchronous error diagnostic.
            string error = null;
            Application.LogCallback capture = (message, stack, type) =>
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) error = message;
            };
            Application.logMessageReceived += capture;
            try { Physics.BakeMesh(mesh.GetEntityId(), false, options); }
            finally { Application.logMessageReceived -= capture; }
            if (error != null) throw new InvalidOperationException("MeshCollider cooking failed before commit: " + error);
        }

        private static string ValidatePath(string path, bool requireNew = true)
        {
            path = path.Replace('\\', '/');
            var segments = path.Split('/');
            if (!path.StartsWith("Assets/", StringComparison.Ordinal) || segments.Any(s =>
                s.Length == 0 || s == "." || s == ".." || s.Equals("Editor", StringComparison.OrdinalIgnoreCase) ||
                s.EndsWith("~") || s.IndexOf(':') >= 0 || s.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                s.IndexOf("Dev", StringComparison.OrdinalIgnoreCase) >= 0) ||
                !path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) ||
                !AssetDatabase.IsValidFolder(Path.GetDirectoryName(path).Replace('\\', '/')) || requireNew && File.Exists(path))
                throw new InvalidOperationException("Choose a new Mesh .asset path in an existing runtime Assets folder outside Editor/Dev.");
            return path;
        }

        public void Dispose() { Undo.undoRedoPerformed -= OnUndoRedo; Cancel(); }

        private sealed class TemporaryMesh : IDisposable
        {
            internal readonly Mesh Mesh;
            internal TemporaryMesh(Mesh mesh) { Mesh = mesh; }
            public void Dispose() { Object.DestroyImmediate(Mesh); }
        }
    }
}
