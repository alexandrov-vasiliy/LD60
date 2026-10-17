using MoreMountains.Feedbacks;
using UnityEngine;

public sealed class PlayerFootsteps : MonoBehaviour
{
    [SerializeField] private PlayerMovement movement;
    [SerializeField] private MMF_Player feedback;
    [SerializeField, Min(0.01f)] private float stepDistance = 1.5f;

    private float distance;

    private void Awake()
    {
        if (movement == null || feedback == null)
        {
            Debug.LogError("PlayerFootsteps needs movement and a Feel feedback player.", this);
            enabled = false;
        }
    }

    private void OnDisable()
    {
        distance = 0f;
    }

    private void LateUpdate()
    {
        float travelled = movement.isActiveAndEnabled ? movement.GroundedDistanceThisFrame : 0f;
        if (travelled <= 0.0001f)
        {
            distance = 0f;
            return;
        }

        distance += travelled;
        float interval = Mathf.Max(0.01f, stepDistance);
        while (distance >= interval)
        {
            distance -= interval;
            feedback.PlayFeedbacks();
        }
    }
}
