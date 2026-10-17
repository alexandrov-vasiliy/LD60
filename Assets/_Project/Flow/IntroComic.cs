using System.Collections;
using DG.Tweening;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class IntroComic : MonoBehaviour
{
    [SerializeField] private GameObject panel;
    [SerializeField] private CanvasGroup[] frames;
    [SerializeField] private SceneLoader loader;
    [SerializeField, Min(0f)] private float revealDuration = 0.4f;
    private int index;
    private int shownFrame;
    private bool running, transitioning;

    public void Begin()
    {
        if (running) return;
        running = true;
        index = 0;
        panel.SetActive(true);
        StartCoroutine(Reveal());
    }

    private IEnumerator Reveal()
    {
        transitioning = true;
        for (int i = 0; i < frames.Length; i++) frames[i].gameObject.SetActive(i == index);
        frames[index].alpha = 0f;
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
        if (++index < frames.Length) StartCoroutine(Reveal());
        else { transitioning = true; loader.LoadGame(); }
    }

    private void OnDestroy()
    {
        foreach (CanvasGroup frame in frames) if (frame != null) frame.DOKill();
    }
}
