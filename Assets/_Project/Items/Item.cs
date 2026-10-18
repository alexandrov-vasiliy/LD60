using UnityEngine;

public enum ItemId { Vase = 0, Typewriter = 1, Stethoscope = 2, Pills = 3, Alcohol = 4, Calculator = 5 }

[RequireComponent(typeof(Rigidbody))]
public sealed class Item : MonoBehaviour
{
    [SerializeField] private ItemId id;
    [SerializeField] private Transform visual;
    [SerializeField] private Vector3 previewRotation = new Vector3(15f, -30f, 0f);
    [SerializeField] private Vector3 heldPosition;
    [SerializeField] private Vector3 heldRotation;
    [SerializeField] private Vector3 heldScale = Vector3.one;

    public ItemId Id => id;
    public Transform Visual => visual;
    public Quaternion PreviewRotation => Quaternion.Euler(previewRotation);
    public bool WasDropped { get; private set; }
    public Vector3 HeldPosition => heldPosition;
    public Quaternion HeldRotation => Quaternion.Euler(heldRotation);
    public Vector3 HeldScale => heldScale;
    public PlayerInventory Owner { get; internal set; }

    private Rigidbody body;
    private Collider[] colliders;
    private bool[] colliderStates;
    private Transform[] parts;
    private int[] layers;
    private Vector3 worldScale;

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

    internal void Release(Vector3 position, Quaternion rotation, Vector3 impulse)
    {
        Owner = null;
        WasDropped = true;
        transform.SetParent(null, true);
        transform.SetPositionAndRotation(position, rotation);
        transform.localScale = worldScale;
        for (int i = 0; i < parts.Length; i++) parts[i].gameObject.layer = layers[i];
        for (int i = 0; i < colliders.Length; i++) colliders[i].enabled = colliderStates[i];
        gameObject.SetActive(true);
        body.isKinematic = false;
        body.useGravity = true;
        body.AddForce(impulse, ForceMode.Impulse);
    }
}
