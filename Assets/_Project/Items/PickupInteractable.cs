using UnityEngine;

[RequireComponent(typeof(Item))]
public sealed class PickupInteractable : MonoBehaviour, IInteractable
{
    private Item item;
    private void Awake() => item = GetComponent<Item>();
    public bool CanInteract(PlayerInventory player) => isActiveAndEnabled && player != null && item.Owner == null;
    public void Interact(PlayerInventory player)
    {
        if (CanInteract(player)) player.Pickup(item);
    }
}
