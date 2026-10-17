using System.Collections;
using Febucci.TextAnimatorForUnity;
using UnityEngine;

public sealed class WorldText : MonoBehaviour
{
    [SerializeField] private GameObject label;
    [SerializeField] private TypewriterComponent typewriter;
    [SerializeField, Min(0.1f)] private float lineDuration = 2.5f;
    private Coroutine reaction;

    public void Show(string[] lines)
    {
        Hide();
        reaction = StartCoroutine(Present(lines));
    }

    private IEnumerator Present(string[] lines)
    {
        label.SetActive(true);
        foreach (string line in lines)
        {
            typewriter.ShowText(line);
            while (typewriter.IsShowingText) yield return null;
            yield return new WaitForSeconds(lineDuration);
        }
        label.SetActive(false);
        reaction = null;
    }

    private void LateUpdate()
    {
        if (label.activeSelf && Camera.main != null) label.transform.rotation = Camera.main.transform.rotation;
    }

    public void Hide()
    {
        if (reaction != null) StopCoroutine(reaction);
        reaction = null;
        label.SetActive(false);
    }

    private void OnDisable() => Hide();
}
