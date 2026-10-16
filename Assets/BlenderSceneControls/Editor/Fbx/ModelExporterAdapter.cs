using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Formats.Fbx.Exporter;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BlenderSceneControls
{
    [InitializeOnLoad]
    internal static class ModelExporterAdapter
    {
        static ModelExporterAdapter() { MeshFbxBackend.Export = Export; }
        private static string Export(string path, Mesh mesh, Material[] materials)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            GameObject root = null;
            // Stage outside Assets: exporter failure cannot partially overwrite the destination.
            var temp = Path.Combine(Path.GetTempPath(), "BSC-" + Guid.NewGuid().ToString("N") + ".fbx");
            var staged = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                root = new GameObject("Mesh", typeof(MeshFilter), typeof(MeshRenderer));
                SceneManager.MoveGameObjectToScene(root, scene);
                root.GetComponent<MeshFilter>().sharedMesh = mesh;
                root.GetComponent<MeshRenderer>().sharedMaterials = materials;
                var result = ModelExporter.ExportObject(temp, root, new ExportModelOptions {
                    ModelAnimIncludeOption = Include.Model, ObjectPosition = ObjectPosition.WorldAbsolute,
                    ExportFormat = ExportFormat.Binary, EmbedTextures = false, UseMayaCompatibleNames = false
                });
                if (string.IsNullOrEmpty(result) || !File.Exists(temp) || new FileInfo(temp).Length == 0)
                    throw new IOException("FBX Exporter did not produce a model.");
                // Replacement must be on the destination volume, which can differ from OS temp.
                File.Copy(temp, staged, false);
                if (File.Exists(path)) File.Replace(staged, path, null);
                else File.Move(staged, path);
                return path;
            }
            finally
            {
                if (root) UnityEngine.Object.DestroyImmediate(root);
                EditorSceneManager.ClosePreviewScene(scene);
                if (File.Exists(temp)) File.Delete(temp);
                if (File.Exists(staged)) File.Delete(staged);
            }
        }
    }
}
