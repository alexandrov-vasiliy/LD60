using UnityEngine;

public sealed class Polaroid : MonoBehaviour
{
    [SerializeField] private Transform photoOrigin;
    public Transform PhotoOrigin => photoOrigin;
}
