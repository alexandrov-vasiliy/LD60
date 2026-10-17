using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public sealed class ItemPreviewRenderer : MonoBehaviour
{
    [SerializeField] private Camera previewCamera;
    [SerializeField, Min(32)] private int resolution = 256;

    public RenderTexture Render(Item item)
    {
        if (previewCamera == null || item.Visual == null)
        {
            Debug.LogError("Item preview needs a camera and an Item Visual reference.", item);
            return null;
        }

        var root = new GameObject("PreviewVisual");
        RenderTexture texture = null;
        try
        {
            previewCamera.transform.rotation = Quaternion.identity;
            root.transform.SetPositionAndRotation(previewCamera.transform.position + previewCamera.transform.forward * 5f, item.PreviewRotation);
            CopyVisual(item.Visual, root.transform);
            var renderers = root.GetComponentsInChildren<MeshRenderer>();
            if (renderers.Length == 0)
            {
                Debug.LogError("Item Visual needs a MeshRenderer for its preview.", item);
                return null;
            }
            Bounds bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            previewCamera.transform.position = bounds.center - Vector3.forward * (bounds.extents.z + 5f);
            previewCamera.orthographicSize = Mathf.Max(0.01f, Mathf.Max(bounds.extents.x, bounds.extents.y) * 1.15f);
            previewCamera.farClipPlane = bounds.size.z + 10f;
            texture = new RenderTexture(resolution, resolution, 24, RenderTextureFormat.ARGB32) { name = item.name + " Preview" };
            texture.Create();
            RenderPipeline.SubmitRenderRequest(previewCamera, new UniversalRenderPipeline.SingleCameraRequest { destination = texture });
            return texture;
        }
        catch
        {
            if (texture != null) { texture.Release(); Destroy(texture); }
            throw;
        }
        finally
        {
            root.SetActive(false);
            Destroy(root);
        }
    }

    private void CopyVisual(Transform source, Transform parent)
    {
        if (!source.gameObject.activeSelf) return;
        var copy = new GameObject(source.name);
        copy.layer = previewCamera.gameObject.layer;
        copy.transform.SetParent(parent, false);
        copy.transform.localPosition = source.localPosition;
        copy.transform.localRotation = source.localRotation;
        copy.transform.localScale = source.localScale;
        var mesh = source.GetComponent<MeshFilter>();
        var renderer = source.GetComponent<MeshRenderer>();
        // shortcut: previews support static meshes; bake skinned meshes if animated items are added.
        if (mesh != null && renderer != null && renderer.enabled)
        {
            copy.AddComponent<MeshFilter>().sharedMesh = mesh.sharedMesh;
            var preview = copy.AddComponent<MeshRenderer>();
            preview.sharedMaterials = renderer.sharedMaterials;
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            preview.SetPropertyBlock(properties);
            preview.shadowCastingMode = ShadowCastingMode.Off;
            preview.receiveShadows = false;
            preview.lightProbeUsage = LightProbeUsage.Off;
            preview.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }
        foreach (Transform child in source) CopyVisual(child, copy.transform);
    }
}
