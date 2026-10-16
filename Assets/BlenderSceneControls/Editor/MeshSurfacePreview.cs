using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace BlenderSceneControls
{
    // A camera-scoped public rendering override. Never assign sharedMesh, enabled or scene visibility.
    internal sealed class MeshSurfacePreview : IDisposable
    {
        private readonly MeshEditMode _mode;
        private readonly Func<Mesh> _mesh;
        private readonly Func<bool> _valid;
        private Renderer _suppressed;
        private bool _previous;
        private bool _disposed;
        internal bool Suppressed => _suppressed;

        internal MeshSurfacePreview(MeshEditMode mode, Func<Mesh> mesh, Func<bool> valid)
        {
            _mode = mode; _mesh = mesh; _valid = valid;
            Camera.onPreCull += Begin;
            Camera.onPostRender += End;
            RenderPipelineManager.beginCameraRendering += BeginSRP;
            RenderPipelineManager.endCameraRendering += EndSRP;
            EditorApplication.update += Restore;
            AssemblyReloadEvents.beforeAssemblyReload += Dispose;
            EditorApplication.quitting += Dispose;
        }

        private void BeginSRP(ScriptableRenderContext context, Camera camera) => Begin(camera);
        private void EndSRP(ScriptableRenderContext context, Camera camera) => End(camera);
        internal void Begin(Camera camera)
        {
            Restore();
            if (_disposed || !_valid() || !_mode.Source || camera != _mode.Source.camera) return;
            var renderer = _mode.Target.GetComponent<MeshRenderer>();
            var mesh = _mesh();
            if (!renderer || !renderer.enabled || renderer.forceRenderingOff || !mesh) return;
            var materials = renderer.sharedMaterials;
            if (materials.Length == 0 || Array.Exists(materials, m => !m)) return;
            _previous = renderer.forceRenderingOff;
            _suppressed = renderer;
            try
            {
                renderer.forceRenderingOff = true;
                var block = new MaterialPropertyBlock();
                // Unity repeats the last submesh if there are extra material slots.
                for (var i = 0; i < Math.Max(mesh.subMeshCount, materials.Length); i++)
                {
                    block.Clear(); renderer.GetPropertyBlock(block, Math.Min(i, materials.Length - 1));
                    if (block.isEmpty) renderer.GetPropertyBlock(block);
                    Graphics.DrawMesh(mesh, _mode.Target.transform.localToWorldMatrix,
                        materials[Math.Min(i, materials.Length - 1)], _mode.Target.gameObject.layer, camera,
                        Math.Min(i, mesh.subMeshCount - 1), block, renderer.shadowCastingMode,
                        renderer.receiveShadows, renderer.probeAnchor, renderer.lightProbeUsage,
                        renderer.lightProbeProxyVolumeOverride ? renderer.lightProbeProxyVolumeOverride.GetComponent<LightProbeProxyVolume>() : null);
                }
            }
            catch { Restore(); throw; }
        }
        internal void End(Camera camera)
        {
            if (_mode.Source && camera == _mode.Source.camera) Restore();
        }
        private void Restore()
        {
            if (_suppressed) _suppressed.forceRenderingOff = _previous;
            _suppressed = null;
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Restore();
            Camera.onPreCull -= Begin; Camera.onPostRender -= End;
            RenderPipelineManager.beginCameraRendering -= BeginSRP;
            RenderPipelineManager.endCameraRendering -= EndSRP;
            EditorApplication.update -= Restore;
            AssemblyReloadEvents.beforeAssemblyReload -= Dispose;
            EditorApplication.quitting -= Dispose;
        }
    }
}
