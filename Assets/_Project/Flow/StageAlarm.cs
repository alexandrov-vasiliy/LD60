using DG.Tweening;
using UnityEngine;

public sealed class StageAlarm : MonoBehaviour
{
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private Transform beacons;
    [SerializeField] private float rotationSpeed = 120f;

    private void Update()
    {
        foreach (Transform beacon in beacons)
            beacon.Rotate(0f, rotationSpeed * Time.deltaTime, 0f, Space.Self);
    }

    public void FadeOut(float duration)
    {
        audioSource.DOKill();
        audioSource.DOFade(0f, duration).SetUpdate(true);
    }

    public void StopAlarm() => gameObject.SetActive(false);

    private void OnDisable()
    {
        audioSource.DOKill();
        audioSource.volume = 0f;
        audioSource.Stop();
    }
}
