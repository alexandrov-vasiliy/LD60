using DG.Tweening;
using UnityEngine;

public sealed class HeldItemPresenter : MonoBehaviour
{
    [SerializeField] private Transform mount;
    [SerializeField] private Camera worldCamera;
    [SerializeField] private Camera itemCamera;
    [SerializeField, Range(0, 31)] private int heldLayer;
    [SerializeField, Min(0f)] private float pickupDuration = 0.2f;

    private Tween flight;
    private Item flyingItem;

    private void Awake()
    {
        if (mount == null || worldCamera == null || itemCamera == null)
        {
            Debug.LogError("HeldItemPresenter needs a mount, world camera and item camera.", this);
            enabled = false;
        }
    }

    internal void CompleteFlight()
    {
        flight?.Kill();
        flight = null;
        if (flyingItem != null) Attach(flyingItem);
        flyingItem = null;
    }

    internal void Show(Item item, bool animate)
    {
        if (item == null) return;
        item.gameObject.SetActive(true);
        if (!animate || pickupDuration <= 0f) { Attach(item); return; }
        flyingItem = item;
        Vector3 position = item.transform.position;
        Quaternion rotation = item.transform.rotation;
        Vector3 scale = item.transform.lossyScale;
        // Keep the flight in world space so moving the camera does not drag its start point.
        flight = DOVirtual.Float(0f, 1f, pickupDuration, t =>
        {
            if (item == null) return;
            item.transform.position = Vector3.Lerp(position, mount.TransformPoint(item.HeldPosition), t);
            item.transform.rotation = Quaternion.Slerp(rotation, mount.rotation * item.HeldRotation, t);
            item.transform.localScale = Vector3.Lerp(scale, Vector3.Scale(mount.lossyScale, item.HeldScale), t);
        }).SetEase(Ease.OutQuad).OnComplete(CompleteFlight);
    }

    private void Attach(Item item)
    {
        item.transform.SetParent(mount, false);
        item.transform.localPosition = item.HeldPosition;
        item.transform.localRotation = item.HeldRotation;
        item.transform.localScale = item.HeldScale;
        item.SetHeldLayer(heldLayer);
    }

    private void LateUpdate()
    {
        itemCamera.fieldOfView = worldCamera.fieldOfView;
    }

    private void OnDisable() => CompleteFlight();
}
