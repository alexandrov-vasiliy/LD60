using Unity.Cinemachine;
using UnityEngine;

[DefaultExecutionOrder(-100)]
public sealed class PlayerControls : MonoBehaviour
{
    [SerializeField] private PlayerMovement movement;
    [SerializeField] private PlayerInventory inventory;
    [SerializeField] private PlayerInteractor interactor;
    [SerializeField] private CinemachineInputAxisController look;
    [SerializeField] private CinemachineCamera gameplayCamera;
    [SerializeField] private Camera heldItemCamera;
    [SerializeField] private GameObject gameplayHud;
    [SerializeField] private GameObject inventoryHud;
    [SerializeField] private GameObject body;
    private bool requested;
    private int resumeFrame;

    public void SetBodyVisible(bool value) => body.SetActive(value);

    public void SetDialogueInventoryEnabled(bool value)
    {
        inventory.UsePlayerDropOrigin = value;
        inventory.enabled = value;
        inventoryHud.SetActive(value);
    }

    public void SetGameplayEnabled(bool value)
    {
        requested = value;
        resumeFrame = Time.frameCount + 1;
        if (!value) Apply(false);
        Cursor.lockState = value ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !value;
    }

    private void LateUpdate()
    {
        if (requested && Time.frameCount >= resumeFrame && !movement.enabled) Apply(true);
    }

    private void Apply(bool value)
    {
        movement.enabled = value;
        inventory.enabled = value;
        interactor.enabled = value;
        look.enabled = value;
        heldItemCamera.enabled = value;
        gameplayHud.SetActive(value);
        inventoryHud.SetActive(value);
    }

    public void Teleport(Transform destination)
    {
        CharacterController controller = GetComponent<CharacterController>();
        controller.enabled = false;
        Vector3 delta = destination.position - transform.position;
        transform.SetPositionAndRotation(destination.position, destination.rotation);
        gameplayCamera.OnTargetObjectWarped(transform, delta);
        CinemachinePanTilt panTilt = gameplayCamera.GetComponent<CinemachinePanTilt>();
        panTilt.PanAxis.Value = 0f;
        panTilt.TiltAxis.Value = 0f;
        gameplayCamera.PreviousStateIsValid = false;
        controller.enabled = true;
    }
}
