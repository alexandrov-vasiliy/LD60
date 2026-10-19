using System.Collections;
using System;
using Febucci.TextAnimatorForUnity;
using UnityEngine;

/// <summary>
/// Последовательно показывает реплики над NPC с печатью и выдержкой времени, затем сообщает исполнителю о завершении. При скрытии отменяет текущий показ без продолжения сценария.
/// </summary>
public sealed class WorldText : MonoBehaviour
{
    [SerializeField] private GameObject label;
    [SerializeField] private TypewriterComponent typewriter;
    [SerializeField, Min(0.1f)] private float lineDuration = 2.5f;
    private Coroutine reaction;
    private string currentLine;
    public bool IsConfigured => label != null && typewriter != null;

    private void OnEnable() => Localization.LanguageChanged += RefreshLanguage;

    private void RefreshLanguage()
    {
        if (currentLine == null || label == null || !label.activeSelf) return;
        typewriter.StopShowingText();
        typewriter.TextAnimator.SetText(Localization.Get(currentLine));
        typewriter.TextAnimator.SetVisibilityEntireText(true, false);
    }

    public void Show(string[] lines, Action completed = null)
    {
        Hide();
        reaction = StartCoroutine(Present(lines, completed));
    }

    private IEnumerator Present(string[] lines, Action completed)
    {
        label.SetActive(true);
        foreach (string line in lines)
        {
            currentLine = line;
            typewriter.ShowText(Localization.Get(line));
            while (typewriter.IsShowingText) yield return null;
            yield return new WaitForSeconds(lineDuration);
        }
        label.SetActive(false);
        currentLine = null;
        reaction = null;
        completed?.Invoke();
    }

    private void LateUpdate()
    {
        if (label != null && label.activeSelf && Camera.main != null) label.transform.rotation = Camera.main.transform.rotation;
    }

    public void Hide()
    {
        if (reaction != null) StopCoroutine(reaction);
        reaction = null;
        currentLine = null;
        if (label != null) label.SetActive(false);
    }

    private void OnDisable()
    {
        Localization.LanguageChanged -= RefreshLanguage;
        Hide();
    }
}
