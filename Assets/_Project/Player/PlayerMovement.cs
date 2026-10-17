using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public sealed class PlayerMovement : MonoBehaviour
{
    [SerializeField] private InputActionReference moveAction;
    [SerializeField] private Transform movementCamera;
    [SerializeField, Min(0f)] private float speed = 4f;
    [SerializeField, Min(0f)] private float gravity = 20f;

    public float GroundedDistanceThisFrame { get; private set; }

    private CharacterController controller;
    private float verticalSpeed;
    private bool enabledInput;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (moveAction == null || moveAction.action == null || movementCamera == null)
        {
            Debug.LogError("PlayerMovement needs a Move action and movement camera.", this);
            enabled = false;
        }
    }

    private void OnEnable()
    {
        if (moveAction == null || moveAction.action == null)
            return;

        enabledInput = !moveAction.action.enabled;
        if (enabledInput)
            moveAction.action.Enable();
    }

    private void OnDisable()
    {
        if (enabledInput && moveAction != null && moveAction.action != null)
            moveAction.action.Disable();

        enabledInput = false;
        verticalSpeed = 0f;
        GroundedDistanceThisFrame = 0f;
    }

    private void Update()
    {
        GroundedDistanceThisFrame = 0f;
        if (!controller.enabled)
            return;

        Vector2 input = Vector2.ClampMagnitude(moveAction.action.ReadValue<Vector2>(), 1f);
        // Camera right stays horizontal even when looking straight up or down.
        Vector3 right = Vector3.ProjectOnPlane(movementCamera.right, Vector3.up).normalized;
        Vector3 forward = Vector3.Cross(right, Vector3.up);
        Vector3 velocity = (forward * input.y + right * input.x) * speed;

        bool wasGrounded = controller.isGrounded;
        if (wasGrounded && verticalSpeed < 0f)
            verticalSpeed = -2f;

        verticalSpeed -= gravity * Time.deltaTime;
        velocity.y = verticalSpeed;
        Vector3 previousPosition = transform.position;
        controller.Move(velocity * Time.deltaTime);

        if (wasGrounded && controller.isGrounded && input.sqrMagnitude > 0f)
            GroundedDistanceThisFrame = Vector3.ProjectOnPlane(transform.position - previousPosition, Vector3.up).magnitude;
    }
}
