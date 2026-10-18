using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public sealed class NpcWantItem : MonoBehaviour
{
    private NpcDialog dialog;
    private void Awake() => dialog = GetComponent<NpcDialog>();

    private void OnTriggerEnter(Collider other)
    {
        Item item = other.GetComponentInParent<Item>();
        if (dialog != null && item != null && item.gameObject.activeInHierarchy)
            dialog.ReactToItem(item);
    }
}
