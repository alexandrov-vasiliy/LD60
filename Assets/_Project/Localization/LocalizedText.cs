using TMPro;
using UnityEngine;

[DisallowMultipleComponent, RequireComponent(typeof(TMP_Text))]
public sealed class LocalizedText : MonoBehaviour
{
    [SerializeField] private string key;
    private TMP_Text label;

    private void OnEnable()
    {
        label = GetComponent<TMP_Text>();
        Localization.LanguageChanged += Refresh;
        Refresh();
    }

    private void OnDisable() => Localization.LanguageChanged -= Refresh;
    private void Refresh() => label.text = Localization.Get(key);
}
