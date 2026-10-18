using UnityEngine;

/// <summary>
/// Передаёт попадание активного предмета в триггер NPC компоненту NpcDialog; возможность реакции и принятие определяет диалоговый сценарий.
/// </summary>
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
