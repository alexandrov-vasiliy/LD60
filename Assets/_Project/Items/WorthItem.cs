using UnityEngine;

public sealed class WorthItem : MonoBehaviour
{
    [SerializeField, Min(0)] private int value = 100;
    public int Value => value;
}
