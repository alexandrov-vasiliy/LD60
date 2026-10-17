using UnityEngine;

public sealed class InteractionPrompt : MonoBehaviour
{
    [SerializeField] private GameObject label;
    public void SetVisible(bool visible) => label.SetActive(visible);
    private void Awake() => SetVisible(false);
}
