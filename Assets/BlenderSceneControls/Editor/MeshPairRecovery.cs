using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace BlenderSceneControls
{
    // A write-ahead before-image, outside Assets and Library. Recovery is explicit and retryable.
    internal static class MeshPairRecovery
    {
        private const string DirectoryPath = "ProjectSettings/BlenderSceneControls/MeshPending";
        [Serializable]
        private sealed class Image
        {
            public bool Exists;
            public string Path;
            public string Bytes;
            public string Hash;
        }
        [Serializable]
        private sealed class Journal
        {
            public int Version = 1;
            public string MeshPath;
            public string MeshGuid;
            public string DocumentGuid;
            public Image[] Images;
            public Reference[] References;
            public Reference Source;
            public string SourceFingerprint;
            public string SourceSerialized;
            public string SourceDocument;
            public string IntendedNative;
        }

        [Serializable]
        private sealed class Reference
        {
            public string GlobalId;
            public string Session;
            public int InstanceId;
        }

        private static string Session
        {
            get
            {
                const string key = "BSC.MeshRecovery.Session";
                var value = SessionState.GetString(key, "");
                if (value.Length == 0) SessionState.SetString(key, value = Guid.NewGuid().ToString("N"));
                return value;
            }
        }

        private static Reference Identify(UnityEngine.Object value) => new Reference
        {
            GlobalId = GlobalObjectId.GetGlobalObjectIdSlow(value).ToString(),
            Session = Session, InstanceId = value.GetInstanceID()
        };

        private static UnityEngine.Object Resolve(Reference reference)
        {
            if (reference == null) return null;
            if (GlobalObjectId.TryParse(reference.GlobalId, out var id) && id.targetObjectId != 0)
            {
                var value = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id);
                if (value) return value;
            }
            return reference.Session == Session ? Resources.FindObjectsOfTypeAll<UnityEngine.Object>()
                .FirstOrDefault(value => value.GetInstanceID() == reference.InstanceId) : null;
        }

        private static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
        }

        private static string JournalPath(string meshPath) => DirectoryPath + "/" +
            Hash(Encoding.UTF8.GetBytes(meshPath.ToLowerInvariant())) + ".recovery";

        internal static bool IsPending(string meshPath)
        {
            if (File.Exists(JournalPath(meshPath))) return true;
            var guid = AssetDatabase.AssetPathToGUID(meshPath);
            if (string.IsNullOrEmpty(guid) || !Directory.Exists(DirectoryPath)) return false;
            return Directory.GetFiles(DirectoryPath, "*.recovery").Any(path =>
                JsonUtility.FromJson<Journal>(File.ReadAllText(path))?.MeshGuid == guid);
        }

        internal static void Begin(string meshPath, string documentPath, MeshFilter owner = null, MeshCollider[] colliders = null)
        {
            if (IsPending(meshPath)) throw new IOException("A mesh before-image already requires recovery.");
            var sourceDocument = owner ? (MeshDocumentAsset.Find(owner.sharedMesh)?.Data ?? MeshNativeBridge.Import(owner.sharedMesh)) : null;
            string intendedNative = null;
            if (sourceDocument != null)
            {
                var intended = MeshNativeBridge.Evaluate(sourceDocument);
                try { intended.name = Path.GetFileNameWithoutExtension(meshPath); intendedNative = EditorJsonUtility.ToJson(intended); }
                finally { UnityEngine.Object.DestroyImmediate(intended); }
            }
            var journal = new Journal
            {
                MeshPath = meshPath,
                MeshGuid = AssetDatabase.AssetPathToGUID(meshPath),
                DocumentGuid = AssetDatabase.AssetPathToGUID(documentPath),
                Source = owner ? Identify(owner.sharedMesh) : null,
                SourceFingerprint = owner ? MeshNativeBridge.Fingerprint(owner.sharedMesh) : null,
                SourceSerialized = owner ? EditorJsonUtility.ToJson(owner.sharedMesh) : null,
                SourceDocument = sourceDocument == null ? null : JsonUtility.ToJson(sourceDocument),
                IntendedNative = intendedNative,
                References = owner ? new Component[] { owner }.Concat(colliders ?? Array.Empty<MeshCollider>()).Select(Identify).ToArray() : null,
                Images = new[] { meshPath, meshPath + ".meta", documentPath, documentPath + ".meta" }
                    .Select(path =>
                    {
                        var bytes = File.Exists(path) ? File.ReadAllBytes(path) : null;
                        return new Image { Exists = bytes != null, Path = path, Bytes = bytes == null ? null : Convert.ToBase64String(bytes),
                            Hash = bytes == null ? null : Hash(bytes) };
                    }).ToArray()
            };
            Directory.CreateDirectory(DirectoryPath);
            var path = JournalPath(meshPath);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                MeshEditTransaction.PersistenceFault?.Invoke("before-journal-write");
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(journal));
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                File.Move(temporary, path);
                MeshEditTransaction.PersistenceFault?.Invoke("after-journal-write");
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        internal static void Complete(string meshPath)
        {
            MeshEditTransaction.PersistenceFault?.Invoke("before-journal-delete");
            File.Delete(JournalPath(meshPath));
        }

        internal static void RecordCreatedIdentity(string meshPath)
        {
            var path = JournalPath(meshPath);
            var journal = JsonUtility.FromJson<Journal>(File.ReadAllText(path));
            journal.MeshGuid = AssetDatabase.AssetPathToGUID(meshPath);
            if (string.IsNullOrEmpty(journal.MeshGuid)) throw new IOException("Native mesh creation did not publish a GUID.");
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(journal));
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                File.Replace(temporary, path, null);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        internal static void Recover(string meshPath)
        {
            var path = JournalPath(meshPath);
            if (!File.Exists(path) && Directory.Exists(DirectoryPath))
            {
                var guid = AssetDatabase.AssetPathToGUID(meshPath);
                var matches = Directory.GetFiles(DirectoryPath, "*.recovery").Where(p =>
                    !string.IsNullOrEmpty(guid) && JsonUtility.FromJson<Journal>(File.ReadAllText(p))?.MeshGuid == guid).ToArray();
                if (matches.Length == 1) path = matches[0];
            }
            if (!File.Exists(path)) throw new IOException("No durable before-image exists; clearing a pending marker is not recovery.");
            var journal = JsonUtility.FromJson<Journal>(File.ReadAllText(path));
            if (journal == null || journal.Version != 1 || journal.Images?.Length != 4)
                throw new IOException("Unsupported mesh recovery journal.");
            if (journal.Images[0].Path != journal.MeshPath || journal.Images[1].Path != journal.MeshPath + ".meta" ||
                journal.Images[3].Path != journal.Images[2].Path + ".meta" ||
                !journal.Images[2].Path.Split('/').Contains("Editor") ||
                journal.Images.Select(i => i.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 4 ||
                (journal.Images[0].Exists && journal.Images.Any(i => !i.Exists)))
                throw new IOException("Inconsistent mesh recovery manifest.");
            var project = Path.GetFullPath("Assets") + Path.DirectorySeparatorChar;
            foreach (var image in journal.Images)
            {
                if (!Path.GetFullPath(image.Path).StartsWith(project, StringComparison.OrdinalIgnoreCase) ||
                    (image.Exists && Hash(Convert.FromBase64String(image.Bytes)) != image.Hash))
                    throw new IOException("Invalid mesh before-image path or checksum.");
            }
            var currentPath = string.IsNullOrEmpty(journal.MeshGuid) ? "" : AssetDatabase.GUIDToAssetPath(journal.MeshGuid);
            if (!string.IsNullOrEmpty(currentPath) && currentPath != journal.MeshPath)
            {
                journal.Images[0].Path = currentPath; journal.Images[1].Path = currentPath + ".meta";
            }
            meshPath = journal.Images[0].Path;
            if (!string.IsNullOrEmpty(journal.MeshGuid) && (File.Exists(meshPath) || File.Exists(meshPath + ".meta")) &&
                AssetDatabase.AssetPathToGUID(meshPath) != journal.MeshGuid)
                throw new IOException("Mesh identity changed; recovery refuses to replace another asset.");
            var documentPath = string.IsNullOrEmpty(journal.DocumentGuid) ? "" : AssetDatabase.GUIDToAssetPath(journal.DocumentGuid);
            if (!string.IsNullOrEmpty(documentPath))
            {
                if (!documentPath.Split('/').Contains("Editor")) throw new IOException("Recovered companion must remain in Editor.");
                journal.Images[2].Path = documentPath; journal.Images[3].Path = documentPath + ".meta";
            }
            if (!journal.Images[0].Exists)
            {
                var created = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                if (created && string.IsNullOrEmpty(journal.MeshGuid) &&
                    (string.IsNullOrEmpty(journal.IntendedNative) || EditorJsonUtility.ToJson(created) != journal.IntendedNative))
                    throw new IOException("Unpublished first-commit identity does not match its full intended native baseline; before-image retained.");
                if (journal.References != null)
                {
                    var source = Resolve(journal.Source) as Mesh;
                    var references = journal.References.Select(Resolve).ToArray();
                    if (!source || MeshNativeBridge.Fingerprint(source) != journal.SourceFingerprint ||
                        EditorJsonUtility.ToJson(source) != journal.SourceSerialized ||
                        references.Any(r => !(r is MeshFilter) && !(r is MeshCollider)))
                        throw new IOException("Exact first-commit source/owner is unavailable or changed; before-image retained. Load the recorded owner scene and retry.");
                    var sourceDocument = MeshDocumentAsset.Find(source)?.Data ?? MeshNativeBridge.Import(source);
                    if (JsonUtility.ToJson(sourceDocument) != journal.SourceDocument)
                        throw new IOException("First-commit source document changed; before-image retained.");
                    // Resolve and validate the entire set before changing any reference. Never infer an owner.
                    foreach (var reference in references)
                    {
                        var current = reference is MeshFilter filter ? filter.sharedMesh : ((MeshCollider)reference).sharedMesh;
                        if (current != source && (!created || current != created))
                            throw new IOException("Recorded owner reference changed externally; before-image retained.");
                    }
                    foreach (var reference in references)
                    {
                        var current = reference is MeshFilter currentFilter ? currentFilter.sharedMesh : ((MeshCollider)reference).sharedMesh;
                        if (current == source) continue;
                        if (reference is MeshFilter filter) filter.sharedMesh = source;
                        else MeshEditTransaction.RebindCollider((MeshCollider)reference, source);
                        if (PrefabUtility.IsPartOfPrefabInstance(reference)) PrefabUtility.RecordPrefabInstancePropertyModifications(reference);
                        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(((Component)reference).gameObject.scene);
                    }
                    MeshEditTransaction.PersistenceFault?.Invoke("after-recovery-references");
                }
                if (created && (Resources.FindObjectsOfTypeAll<MeshFilter>().Any(f => f.sharedMesh == created) ||
                    Resources.FindObjectsOfTypeAll<MeshCollider>().Any(c => c.sharedMesh == created) ||
                    AssetDatabase.FindAssets("t:Scene t:Prefab").Select(AssetDatabase.GUIDToAssetPath)
                        .Any(p => AssetDatabase.GetDependencies(p, true).Contains(meshPath))))
                    throw new IOException("Failed first-commit asset is still referenced on disk or by an unrecorded object. Save the restored owner explicitly and retry; before-image retained.");
                if (created) MeshOwnership.ClearPending(created);
                // A failed first commit owns only these new files. Never touch the imported source.
                foreach (var assetPath in new[] { journal.Images[2].Path, meshPath })
                    if (File.Exists(assetPath) && !AssetDatabase.DeleteAsset(assetPath))
                        throw new IOException("Could not remove failed first-commit asset " + assetPath);
            }
            else
            {
                // Acquire all four locks before writing anything. A partial OS write still retains the journal.
                AssetDatabase.ReleaseCachedFileHandles();
                var streams = new FileStream[4];
                try
                {
                    foreach (var image in journal.Images) Directory.CreateDirectory(Path.GetDirectoryName(image.Path));
                    for (var i = 0; i < streams.Length; i++)
                        streams[i] = new FileStream(journal.Images[i].Path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                    for (var i = 0; i < streams.Length; i++)
                    {
                        var bytes = Convert.FromBase64String(journal.Images[i].Bytes);
                        streams[i].SetLength(bytes.Length);
                        streams[i].Write(bytes, 0, bytes.Length);
                        streams[i].Flush(true);
                        MeshEditTransaction.PersistenceFault?.Invoke("after-recovery-file-" + i);
                    }
                }
                finally { foreach (var stream in streams) stream?.Dispose(); }
                foreach (var assetPath in new[] { meshPath, journal.Images[2].Path })
                    AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                foreach (var image in journal.Images)
                    if (Hash(File.ReadAllBytes(image.Path)) != image.Hash)
                        throw new IOException("Restored mesh file verification failed; before-image retained.");
            }
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (mesh)
            {
                var document = AssetDatabase.LoadAssetAtPath<MeshDocumentAsset>(journal.Images[2].Path);
                if (!document || document.MeshGuid != journal.MeshGuid || document.Schema != 1 ||
                    document.Fingerprint != MeshNativeBridge.Fingerprint(mesh) ||
                    document.DocumentFingerprint != MeshDocumentAsset.HashDocument(document.Data))
                    throw new IOException("Recovered mesh/document validation failed; before-image retained.");
                document.Data.Validate();
                // Undo can leave a dirty object when reimport reuses an identical baseline artifact.
                // Its validated contents are already durable; do not save or adopt unrelated changes.
                EditorUtility.ClearDirty(mesh);
                EditorUtility.ClearDirty(document);
            }
            if (mesh)
            {
                MeshDocumentAsset.Find(mesh, true);
                foreach (var collider in Resources.FindObjectsOfTypeAll<MeshCollider>().Where(c =>
                    !EditorUtility.IsPersistent(c) && c.gameObject.scene.IsValid() && c.sharedMesh == mesh && !c.convex))
                    MeshEditTransaction.RebindCollider(collider, mesh);
                MeshOwnership.ClearPending(mesh);
            }
            MeshEditTransaction.PersistenceFault?.Invoke("before-journal-delete");
            File.Delete(path);
        }
    }
}
