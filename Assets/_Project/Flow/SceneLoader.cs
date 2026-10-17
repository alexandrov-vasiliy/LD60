using System.Collections;
using DG.Tweening;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class SceneLoader : MonoBehaviour
{
    [SerializeField] private CanvasGroup fade;
    [SerializeField, Min(0f)] private float fadeDuration = 0.4f;
    private bool loading;

    private IEnumerator Start() => Fade(false);

    public IEnumerator Fade(bool toBlack)
    {
        fade.blocksRaycasts = true;
        yield return fade.DOFade(toBlack ? 1f : 0f, fadeDuration).SetUpdate(true).WaitForCompletion();
        fade.blocksRaycasts = toBlack;
    }

    public void LoadGame()
    {
        if (loading) return;
        loading = true;
        StartCoroutine(Load());
    }

    private IEnumerator Load()
    {
        Time.timeScale = 1f;
        yield return Fade(true);
        yield return SceneManager.LoadSceneAsync("GameScene");
    }

    private void OnDestroy() => fade.DOKill();
}
