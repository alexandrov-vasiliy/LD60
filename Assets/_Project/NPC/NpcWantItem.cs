using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public sealed class NpcWantItem : MonoBehaviour
{
    [SerializeField] private ItemId wantedItem;
    public bool HasReceivedItem { get; private set; }
    private NpcDialog dialog;
    private GameSequencer sequencer;

    private void Awake()
    {
        dialog = GetComponent<NpcDialog>();
        sequencer = FindAnyObjectByType<GameSequencer>();
    }

    private void OnTriggerEnter(Collider other)
    {
        Item item = other.GetComponentInParent<Item>();
        if (HasReceivedItem || !sequencer.CanReceiveItems || item == null || item.Owner != null || !item.WasDropped)
            return;
        bool accepted = dialog.HasMet && item.Id == wantedItem;
        if (accepted)
        {
            HasReceivedItem = true;
            item.gameObject.SetActive(false);
            Destroy(item.gameObject);
        }
        dialog.ReactToItem(accepted);
    }
}
