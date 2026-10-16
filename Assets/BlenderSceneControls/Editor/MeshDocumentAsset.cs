using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BlenderSceneControls
{
    internal sealed class MeshDocumentAsset : ScriptableObject
    {
        public int Schema = 1;
        public string MeshGuid;
        public string Fingerprint;
        public string DocumentFingerprint;
        public MeshDocument Data = new MeshDocument();

        internal static MeshDocumentAsset Find(Mesh mesh, bool validatingRecovery = false)
        {
            if (!validatingRecovery && MeshPairRecovery.IsPending(AssetDatabase.GetAssetPath(mesh)))
                throw new InvalidOperationException("Mesh persistence has a durable before-image. Explicit recovery is required before editing.");
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mesh, out string guid, out long localId)) return null;
            var identity = MeshOwnership.Read(mesh);
            if (!validatingRecovery && identity != null && identity.Pending)
                throw new InvalidOperationException("Mesh persistence was interrupted. The asset pair requires recovery before editing.");
            var matches = AssetDatabase.FindAssets("t:MeshDocumentAsset").Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<MeshDocumentAsset>).Where(a => a.MeshGuid == guid).ToArray();
            if (matches.Length == 0)
            {
                if (identity != null) throw new InvalidOperationException("Editable mesh metadata is missing. Explicit recovery is required.");
                return null;
            }
            if (identity != null && identity.DocumentGuid != AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(matches[0])))
                throw new InvalidOperationException("Editable mesh companion identity does not match.");
            if (!AssetDatabase.GetAssetPath(matches[0]).Split('/').Any(p => p.Equals("Editor", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Mesh document must remain in an Editor folder.");
            if (matches.Length != 1 || matches[0].Schema != 1 || matches[0].Fingerprint != MeshNativeBridge.Fingerprint(mesh) ||
                matches[0].DocumentFingerprint != HashDocument(matches[0].Data))
                throw new InvalidOperationException("Mesh metadata is ambiguous, stale or unsupported. Explicit triangle reimport is required.");
            matches[0].Data.Validate();
            return matches[0];
        }

        internal static string HashDocument(MeshDocument document)
        {
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(MeshDocument.CanonicalJson(document)))).Replace("-", "");
        }
    }

    // Stored in the native asset's importer, so renames/moves and a missing companion cannot erase identity.
    [Serializable]
    internal sealed class MeshOwnerIdentity
    {
        public string DocumentGuid;
        public string GlobalOwner;
        public string SessionOwner;
        public bool Pending;
    }

    [InitializeOnLoad]
    internal static class MeshOwnership
    {
        private const string Prefix = "BSC.MeshOwner.v1:";
        static MeshOwnership() { EditorSceneManager.sceneSaved += SceneSaved; }

        internal static string SessionOwner(MeshFilter target)
        {
            var nonce = SessionState.GetString("BSC.MeshOwner.Session", "");
            if (nonce.Length == 0)
            {
                nonce = Guid.NewGuid().ToString("N");
                SessionState.SetString("BSC.MeshOwner.Session", nonce);
            }
            return nonce + ":" + target.GetInstanceID();
        }

        private static string GlobalOwner(MeshFilter target)
        {
            if (string.IsNullOrEmpty(target.gameObject.scene.path)) return "";
            var id = GlobalObjectId.GetGlobalObjectIdSlow(target);
            return id.targetObjectId == 0 ? "" : id.ToString();
        }

        internal static MeshOwnerIdentity Create(MeshFilter target, MeshDocumentAsset document) => new MeshOwnerIdentity
        {
            GlobalOwner = GlobalOwner(target), SessionOwner = SessionOwner(target),
            DocumentGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(document))
        };

        internal static MeshOwnerIdentity Read(Mesh mesh)
        {
            var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(mesh));
            var lines = importer ? importer.userData.Split('\n').Where(l => l.StartsWith(Prefix, StringComparison.Ordinal)).ToArray() : Array.Empty<string>();
            if (lines.Length == 0) return null;
            if (lines.Length != 1) throw new InvalidOperationException("Ambiguous mesh ownership marker.");
            try
            {
                var identity = JsonUtility.FromJson<MeshOwnerIdentity>(lines[0].Substring(Prefix.Length));
                if (identity == null || string.IsNullOrEmpty(identity.DocumentGuid) || string.IsNullOrEmpty(identity.SessionOwner))
                    throw new InvalidOperationException("Incomplete mesh ownership marker.");
                identity.Pending = File.Exists(PendingPath(mesh));
                return identity;
            }
            catch (Exception exception) { throw new InvalidOperationException("Invalid mesh ownership marker.", exception); }
        }

        internal static void Write(Mesh mesh, MeshOwnerIdentity identity)
        {
            var path = AssetDatabase.GetAssetPath(mesh);
            var importer = AssetImporter.GetAtPath(path);
            if (!importer) throw new InvalidOperationException("Mesh importer is missing.");
            var other = importer.userData.Split('\n').Where(l => !l.StartsWith(Prefix, StringComparison.Ordinal)).ToList();
            // Pending is a journal, not importer state: changing importer settings between two saves
            // can schedule a reimport of the very native object currently recorded by Undo.
            var pending = identity.Pending;
            var marker = new MeshOwnerIdentity
            {
                DocumentGuid = identity.DocumentGuid, GlobalOwner = identity.GlobalOwner, SessionOwner = identity.SessionOwner
            };
            other.Add(Prefix + JsonUtility.ToJson(marker));
            var userData = string.Join("\n", other);
            if (importer.userData != userData)
            {
                MeshEditTransaction.PersistenceFault?.Invoke("before-owner-meta-write");
                importer.userData = userData;
                EditorUtility.SetDirty(importer);
                string error = null;
                Application.LogCallback capture = (message, stack, type) =>
                {
                    if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) error = message;
                };
                Application.logMessageReceived += capture;
                bool written;
                try { written = AssetDatabase.WriteImportSettingsIfDirty(path); }
                finally { Application.logMessageReceived -= capture; }
                if (!written || error != null)
                    throw new IOException("Could not persist mesh ownership marker: " + error);
                // Publish the changed owner before reopening a scene. Importer objects may otherwise
                // continue exposing an older artifact's userData after WriteImportSettingsIfDirty.
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                MeshEditTransaction.PersistenceFault?.Invoke("after-owner-meta-write");
            }
            var journal = PendingPath(mesh);
            if (pending)
            {
                MeshEditTransaction.PersistenceFault?.Invoke("before-pending-write");
                Directory.CreateDirectory(Path.GetDirectoryName(journal));
                File.WriteAllText(journal, JsonUtility.ToJson(identity));
                MeshEditTransaction.PersistenceFault?.Invoke("after-pending-write");
            }
            else if (File.Exists(journal)) ClearPending(mesh);
        }

        private static string PendingPath(Mesh mesh) => "ProjectSettings/BlenderSceneControls/MeshPending/" +
            AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(mesh)) + ".pending";

        internal static void ClearPending(Mesh mesh)
        {
            var path = PendingPath(mesh);
            if (File.Exists(path))
            {
                MeshEditTransaction.PersistenceFault?.Invoke("before-pending-delete");
                File.Delete(path);
            }
        }

        internal static bool IsOwner(MeshFilter target, Mesh mesh)
        {
            var identity = Read(mesh);
            if (identity == null || identity.Pending) return false;
            var global = GlobalOwner(target);
            return identity.SessionOwner == SessionOwner(target) ||
                (!string.IsNullOrEmpty(global) && identity.GlobalOwner == global);
        }

        internal static bool IsShared(MeshFilter target, Mesh mesh)
        {
            if (Resources.FindObjectsOfTypeAll<MeshFilter>().Any(f => f != target && f.sharedMesh == mesh) ||
                Resources.FindObjectsOfTypeAll<MeshCollider>().Any(c => c.gameObject != target.gameObject && c.sharedMesh == mesh)) return true;
            var meshPath = AssetDatabase.GetAssetPath(mesh);
            // Loaded-object scans cannot see saved, unloaded scenes or prefab references.
            return AssetDatabase.FindAssets("t:Scene t:Prefab").Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p != target.gameObject.scene.path)
                .Any(p => AssetDatabase.GetDependencies(p, true).Contains(meshPath));
        }

        private static void SceneSaved(Scene scene)
        {
            foreach (var target in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<MeshFilter>(true)))
            {
                if (!target.sharedMesh) continue;
                var identity = Read(target.sharedMesh);
                if (identity == null || identity.Pending || identity.SessionOwner != SessionOwner(target)) continue;
                identity.GlobalOwner = GlobalOwner(target);
                Write(target.sharedMesh, identity);
            }
        }
    }

    [InitializeOnLoad]
    internal static class MeshDocumentUndoPersistence
    {
        internal static bool Suppress;
        static MeshDocumentUndoPersistence() { Undo.undoRedoPerformed += SaveRestoredDocuments; }

        private static void SaveRestoredDocuments()
        {
            if (Suppress) return;
            // Scene Undo restores both native serialized data and document. Persist only our own pair.
            foreach (var document in Resources.FindObjectsOfTypeAll<MeshDocumentAsset>())
            {
                if (!AssetDatabase.Contains(document)) continue;
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(AssetDatabase.GUIDToAssetPath(document.MeshGuid));
                if (!EditorUtility.IsDirty(document) && (!mesh || !EditorUtility.IsDirty(mesh))) continue;
                if (mesh && (MeshOwnership.Read(mesh)?.Pending == true || MeshPairRecovery.IsPending(AssetDatabase.GetAssetPath(mesh)))) continue;
                if (!mesh || document.DocumentFingerprint != MeshDocumentAsset.HashDocument(document.Data) ||
                    document.Fingerprint != MeshNativeBridge.Fingerprint(mesh)) continue;
                EditorUtility.SetDirty(mesh);
                EditorUtility.SetDirty(document);
                var identity = MeshOwnership.Read(mesh);
                try
                {
                    MeshPairRecovery.Begin(AssetDatabase.GetAssetPath(mesh), AssetDatabase.GetAssetPath(document));
                    if (identity != null) { identity.Pending = true; MeshOwnership.Write(mesh, identity); }
                    MeshEditTransaction.SaveOwnedAsset(mesh);
                    MeshEditTransaction.SaveOwnedAsset(document);
                    if (identity != null) { identity.Pending = false; MeshOwnership.Write(mesh, identity); }
                    MeshPairRecovery.Complete(AssetDatabase.GetAssetPath(mesh));
                    mesh.UploadMeshData(false);
                    mesh.MarkModified();
                    foreach (var collider in Resources.FindObjectsOfTypeAll<MeshCollider>().Where(c =>
                        !EditorUtility.IsPersistent(c) && c.gameObject.scene.IsValid() && c.sharedMesh == mesh && !c.convex))
                        MeshEditTransaction.RebindCollider(collider, mesh);
                }
                catch (Exception exception) { Debug.LogError("Mesh Undo persistence failed; recovery is required. " + exception.Message); }
            }
        }
    }
}
