using MoreMountains.Feedbacks;
using UnityEngine;

public enum ItemId { Vase = 0, Typewriter = 1, Stethoscope = 2, Pills = 3, Alcohol = 4, Calculator = 5, Polaroid = 6, Pencil = 7, Abacus = 8, Glasses = 9, Teapot = 10, Plate = 11, Candle = 12, GoldIngot = 13, AudioBox = 14, Telescope = 15 }

[RequireComponent(typeof(Rigidbody))]
public sealed class Item : MonoBehaviour
{
    [SerializeField] private ItemId id;
    [SerializeField] private Transform visual;
    [SerializeField] private Vector3 previewRotation = new Vector3(15f, -30f, 0f);
    [SerializeField] private Vector3 heldPosition;
    [SerializeField] private Vector3 heldRotation;
    [SerializeField] private Vector3 heldScale = Vector3.one;
    [SerializeField] private MMF_Player impactFeedback;
    [SerializeField, Min(0f)] private float minimumImpactSpeed = 1f;

    public ItemId Id => id;
    public Transform Visual => visual;
    public Quaternion PreviewRotation => Quaternion.Euler(previewRotation);
    public bool WasDropped { get; private set; }
    public Vector3 HeldPosition => heldPosition;
    public Quaternion HeldRotation => Quaternion.Euler(heldRotation);
    public Vector3 HeldScale => Vector3.Scale(worldScale, heldScale);
    public PlayerInventory Owner { get; internal set; }

    private Rigidbody body;
    private Collider[] colliders;
    private bool[] colliderStates;
    private Transform[] parts;
    private int[] layers;
    private Vector3 worldScale;
    private bool impactPending;
    private static double nextImpactTime;
    private const double ImpactInterval = 0.1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetImpactCooldown() => nextImpactTime = 0;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        colliders = GetComponentsInChildren<Collider>(true);
        colliderStates = new bool[colliders.Length];
        parts = GetComponentsInChildren<Transform>(true);
        layers = new int[parts.Length];
    }

    internal void Store(PlayerInventory owner)
    {
        Owner = owner;
        WasDropped = false;
        impactPending = false;
        worldScale = transform.lossyScale;
        transform.SetParent(null, true);
        for (int i = 0; i < parts.Length; i++) layers[i] = parts[i].gameObject.layer;
        for (int i = 0; i < colliders.Length; i++)
        {
            colliderStates[i] = colliders[i].enabled;
            colliders[i].enabled = false;
        }
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.isKinematic = true;
    }

    internal void SetHeldLayer(int layer)
    {
        foreach (Transform part in parts) part.gameObject.layer = layer;
    }

    internal void RestoreWorldLayers()
    {
        for (int i = 0; i < parts.Length; i++) parts[i].gameObject.layer = layers[i];
    }

    internal void Release(Vector3 position, Quaternion rotation, Vector3 impulse, bool playImpactFeedback = false)
    {
        Owner = null;
        WasDropped = true;
        impactPending = playImpactFeedback;
        transform.SetParent(null, true);
        transform.SetPositionAndRotation(position, rotation);
        transform.localScale = worldScale;
        RestoreWorldLayers();
        for (int i = 0; i < colliders.Length; i++) colliders[i].enabled = colliderStates[i];
        gameObject.SetActive(true);
        body.isKinematic = false;
        body.useGravity = true;
        body.AddForce(impulse, ForceMode.Impulse);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!impactPending || Owner != null || impactFeedback == null) return;
        if (collision.collider.GetComponentInParent<Item>() != null ||
            collision.collider.GetComponentInParent<PlayerInventory>() != null) return;

        // Consume even a quiet or throttled landing so later nudges cannot trigger it.
        impactPending = false;
        float speed = 0f;
        for (int i = 0; i < collision.contactCount; i++)
            speed = Mathf.Max(speed, Mathf.Abs(Vector3.Dot(collision.relativeVelocity, collision.GetContact(i).normal)));
        if (speed < minimumImpactSpeed || Time.unscaledTimeAsDouble < nextImpactTime) return;

        nextImpactTime = Time.unscaledTimeAsDouble + ImpactInterval;
        impactFeedback.PlayFeedbacks();
    }
}
