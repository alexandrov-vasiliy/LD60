using System.Collections;
using DG.Tweening;
using Febucci.TextAnimatorForUnity;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class IntroComic : MonoBehaviour
{
    [SerializeField] private GameObject panel;
    [SerializeField] private CanvasGroup[] frames;
    [SerializeField] private TypewriterComponent typewriter;
    [SerializeField] private SceneLoader loader;
    [SerializeField] private string[] captionKeys = { "intro.frame1", "intro.frame2", "intro.frame3" };
    [SerializeField, Min(0f)] private float revealDuration = 0.4f;
    private int index;
    private int shownFrame;
    private bool running, transitioning;

    private void OnEnable() => Localization.LanguageChanged += RefreshCaption;
    private void OnDisable() => Localization.LanguageChanged -= RefreshCaption;

    private void RefreshCaption()
    {
        if (!running || index >= frames.Length) return;
        string key = captionKeys[index];
        typewriter.transform.parent.gameObject.SetActive(!string.IsNullOrEmpty(key));
        if (!string.IsNullOrEmpty(key)) typewriter.ShowText(Localization.Get(key));
    }

    public void Begin()
    {
        if (running) return;
        running = true;
        index = 0;
        panel.SetActive(true);
        StartCoroutine(Reveal());
    }

    public IEnumerator Play()
    {
        Begin();
        while (running) yield return null;
    }

    private IEnumerator Reveal()
    {
        transitioning = true;
        for (int i = 0; i < frames.Length; i++) frames[i].gameObject.SetActive(i <= index);
        frames[index].alpha = 0f;
        RefreshCaption();
        yield return frames[index].DOFade(1f, revealDuration).SetUpdate(true).WaitForCompletion();
        shownFrame = Time.frameCount;
        transitioning = false;
    }

    private void Update()
    {
        if (!running || transitioning || Time.frameCount <= shownFrame) return;
        Keyboard keyboard = Keyboard.current;
        bool advance = (keyboard != null && (keyboard.eKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame))
            || (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame);
        if (!advance) return;
        if (typewriter.gameObject.activeInHierarchy && typewriter.IsShowingText)
        {
            typewriter.SkipTypewriter();
            return;
        }
        if (++index < frames.Length) StartCoroutine(Reveal());
        else
        {
            transitioning = true;
            running = false;
            if (loader != null) loader.LoadGame();
        }
    }

    private void OnDestroy()
    {
        foreach (CanvasGroup frame in frames) if (frame != null) frame.DOKill();
    }
}
