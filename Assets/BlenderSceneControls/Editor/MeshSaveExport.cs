using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BlenderSceneControls
{
    // Public seam allows the optional assembly to reference core, never the reverse.
    public static class MeshFbxBackend
    {
        public const string Package = "com.unity.formats.fbx@5.1.6";
        public static Func<string, Mesh, Material[], string> Export { get; set; }
        public static bool Available => Export != null;
    }

    internal static class MeshSaveExport
    {
        internal static string Unavailable(MeshFilter target)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || PrefabStageUtility.GetCurrentPrefabStage() != null)
                return "Mesh save/export requires Edit Mode outside Prefab Stage.";
            if (!target || !target.sharedMesh || EditorUtility.IsPersistent(target) || !target.gameObject.scene.IsValid() ||
                target.GetComponent<SkinnedMeshRenderer>() || target.GetComponent<MeshRenderer>()?.additionalVertexStreams)
                return "Select one supported scene static MeshFilter.";
            return null;
        }

        internal static bool Save(MeshFilter target, MeshEditMode mode, string path, bool saveAs)
        {
            ValidateIdle(target, mode);
            var metadata = MeshDocumentAsset.Find(target.sharedMesh);
            if (!saveAs && metadata && MeshOwnership.IsOwner(target, target.sharedMesh) && !MeshOwnership.IsShared(target, target.sharedMesh))
            {
                // Geometry Confirm already persisted the pair. Never flush external edits or the scene.
                if (EditorUtility.IsDirty(target.sharedMesh) || EditorUtility.IsDirty(metadata))
                    throw new InvalidOperationException("The pair has external unsaved changes; resolve them before Save Mesh.");
                return false;
            }
            if (string.IsNullOrEmpty(path)) return false;
            var working = mode?.Transaction.Working.Clone();
            try
            {
                using (var transaction = new MeshEditTransaction(target, true))
                    transaction.Commit(path, copyUnchanged: true);
            }
            finally
            {
                if (mode != null)
                {
                    // Also restore prepared mode after failed-save Undo callbacks.
                    mode.Transaction.ReloadTarget(); mode.RefreshSource(); mode.RestoreDocument(working, true);
                }
            }
            return true;
        }

        private static void ValidateIdle(MeshFilter target, MeshEditMode mode)
        {
            var reason = Unavailable(target);
            if (reason != null) throw new InvalidOperationException(reason);
            if (SceneControls.Active || SceneControls.NavigationActive || mode != null &&
                (!mode.Valid || mode.Target != target || mode.Preparing || mode.SelectingBox))
                throw new InvalidOperationException("Finish or cancel the current preview before save/export.");
        }

        internal static string ValidateExportPath(MeshFilter target, string path, bool overwrite)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Choose an FBX file.");
            path = Path.GetFullPath(path);
            if (!path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase) || !Directory.Exists(Path.GetDirectoryName(path)))
                throw new InvalidOperationException("Choose an .fbx file in an existing directory.");
            var source = AssetDatabase.GetAssetPath(target.sharedMesh);
            if (!string.IsNullOrEmpty(source) && string.Equals(path, Path.GetFullPath(source), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Export cannot replace the current source asset.");
            var assets = Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar;
            if (File.Exists(path) && path.StartsWith(assets, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Choose a new FBX in Assets. Existing project models are protected; explicit overwrite is available outside Assets.");
            if (File.Exists(path) && !overwrite) throw new IOException("Explicit overwrite confirmation is required.");
            return path;
        }

        internal static string Export(MeshFilter target, MeshEditMode mode, string path, bool overwrite = false)
        {
            ValidateIdle(target, mode);
            if (!MeshFbxBackend.Available) throw new InvalidOperationException("Install the optional FBX Exporter explicitly first.");
            path = ValidateExportPath(target, path, overwrite);
            // Read confirmed native data, never detached Working (including automatic Recover/Quads).
            MeshDocumentAsset.Find(target.sharedMesh);
            using (var transaction = new MeshEditTransaction(target))
            {
                var mesh = MeshNativeBridge.Evaluate(transaction.Working);
                try
                {
                    var slots = target.GetComponent<MeshRenderer>()?.sharedMaterials ?? Array.Empty<Material>();
                    if (slots.Length != mesh.subMeshCount || slots.Any(m => !m))
                        throw new InvalidOperationException("Assign one non-null material per mesh submesh before FBX export.");
                    return MeshFbxBackend.Export(path, mesh, slots);
                }
                finally { UnityEngine.Object.DestroyImmediate(mesh); }
            }
        }

        internal static bool Execute(SceneCommand command, MeshFilter target, MeshEditMode mode, SceneCommandContext context)
        {
            if (command == SceneCommand.ExportFbx && !MeshFbxBackend.Available)
            {
                if (EditorUtility.DisplayDialog("Optional FBX Exporter", "Mesh editing and Save work without FBX Exporter. Open Package Manager, then use + > Install package by name: " + MeshFbxBackend.Package + ". No package is installed automatically.", "Open Package Manager", "Cancel"))
                    UnityEditor.PackageManager.UI.Window.Open("com.unity.formats.fbx");
                return false;
            }
            var source = target.sharedMesh;
            var fingerprint = MeshNativeBridge.Fingerprint(source);
            var saveAs = command == SceneCommand.SaveMeshAs;
            if (command != SceneCommand.ExportFbx && !saveAs)
            {
                var metadata = MeshDocumentAsset.Find(source);
                if (metadata && MeshOwnership.IsOwner(target, source) && !MeshOwnership.IsShared(target, source))
                { Save(target, mode, null, false); return true; }
            }
            var path = command == SceneCommand.ExportFbx ?
                EditorUtility.SaveFilePanel("Export confirmed mesh (local geometry, identity transform, metres)", "", target.name, "fbx") :
                EditorUtility.SaveFilePanelInProject("Save independent Mesh and companion", "Mesh " + target.name, "asset", "Choose a new runtime Assets path. Existing assets are protected.");
            if (string.IsNullOrEmpty(path)) return false;
            var overwrite = false;
            if (command == SceneCommand.ExportFbx && File.Exists(path))
            {
                ValidateExportPath(target, path, true);
                overwrite = EditorUtility.DisplayDialog("Overwrite FBX", "Replace this file?\n" + path, "Overwrite", "Cancel");
                if (!overwrite) return false;
            }
            if (!context.Current || target.sharedMesh != source || MeshNativeBridge.Fingerprint(source) != fingerprint)
                throw new InvalidOperationException("Target changed while choosing a path. Run the command again.");
            if (command == SceneCommand.ExportFbx) Export(target, mode, path, overwrite);
            else Save(target, mode, path, saveAs);
            return true;
        }
    }
}
