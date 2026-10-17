public interface IInteractable
{
    bool CanInteract(PlayerInventory player);
    void Interact(PlayerInventory player);
}
