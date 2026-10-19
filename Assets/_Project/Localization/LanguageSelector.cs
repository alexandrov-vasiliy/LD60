using TMPro;
using UnityEngine;

[DisallowMultipleComponent, RequireComponent(typeof(UnityEngine.UI.Button))]
public sealed class LanguageSelector : MonoBehaviour
{
    [SerializeField] private TMP_Text label;
    private UnityEngine.UI.Button button;

    private void OnEnable()
    {
        button = GetComponent<UnityEngine.UI.Button>();
        button.onClick.AddListener(SwitchLanguage);
        Localization.LanguageChanged += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        button.onClick.RemoveListener(SwitchLanguage);
        Localization.LanguageChanged -= Refresh;
    }

    private void SwitchLanguage() => Localization.SetLanguage(Localization.Language == "en" ? "ru" : "en");
    private void Refresh() => label.text = Localization.Get("ui.language");
}
