using UnityEngine;
using UnityEngine.InputSystem;

public sealed class PlayerInteractor : MonoBehaviour
{
    [SerializeField] private Camera interactionCamera;
    [SerializeField] private PlayerInventory inventory;
    [SerializeField] private InputActionReference interactAction;
    [SerializeField] private InteractionPrompt prompt;
    [SerializeField] private LayerMask targetLayers;
    [SerializeField, Min(0.01f)] private float distance = 2.5f;
    [SerializeField, Min(0.001f)] private float radius = 0.08f;
    private bool enabledInput;

    private void Awake()
    {
        if (interactionCamera == null || inventory == null || interactAction == null || prompt == null)
        {
            Debug.LogError("PlayerInteractor needs a camera, inventory, Interact action and prompt.", this);
            enabled = false;
        }
    }

    private void OnEnable()
    {
        enabledInput = !interactAction.action.enabled;
        if (enabledInput) interactAction.action.Enable();
    }

    private void OnDisable()
    {
        if (enabledInput) interactAction.action.Disable();
        if (prompt != null) prompt.SetVisible(false);
    }

    public IInteractable FindTarget()
    {
        Transform view = interactionCamera.transform;
        if (!Physics.SphereCast(view.position, radius, view.forward, out RaycastHit hit, distance,
            targetLayers, QueryTriggerInteraction.Ignore)) return null;
        IInteractable target = hit.collider.GetComponentInParent<IInteractable>();
        return target != null && target.CanInteract(inventory) ? target : null;
    }

    private void Update()
    {
        IInteractable target = FindTarget();
        prompt.SetVisible(target != null);
        if (target != null && interactAction.action.WasPressedThisFrame())
        {
            target.Interact(inventory);
            prompt.SetVisible(false);
        }
    }
}
