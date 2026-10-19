using DG.Tweening;
using UnityEngine;

public sealed class StageAlarm : MonoBehaviour
{
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private Transform beacons;
    [SerializeField] private Material beaconMaterial;
    [SerializeField] private float rotationSpeed = 120f;
    private Light[] lights;
    private Material emissionMaterial;
    private bool alarmActive;
    private float volume;

    private void Awake()
    {
        volume = audioSource.volume;
        audioSource.playOnAwake = false;
        lights = beacons.GetComponentsInChildren<Light>(true);
        emissionMaterial = new Material(beaconMaterial);
        foreach (Renderer renderer in beacons.GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
                if (materials[i] == beaconMaterial) materials[i] = emissionMaterial;
            renderer.sharedMaterials = materials;
        }
        StopAlarm();
    }

    public void StartAlarm()
    {
        SetAlarmActive(true);
        audioSource.DOKill();
        audioSource.volume = volume;
        audioSource.Play();
    }

    private void Update()
    {
        if (!alarmActive) return;
        foreach (Transform beacon in beacons)
            beacon.Rotate(0f, rotationSpeed * Time.deltaTime, 0f, Space.Self);
    }

    public void FadeOut(float duration)
    {
        audioSource.DOKill();
        audioSource.DOFade(0f, duration).SetUpdate(true);
    }

    public void StopAlarm()
    {
        SetAlarmActive(false);
        audioSource.DOKill();
        audioSource.volume = 0f;
        audioSource.Stop();
    }

    private void SetAlarmActive(bool active)
    {
        alarmActive = active;
        foreach (Light light in lights) light.enabled = active;
        emissionMaterial.SetColor("_EmissionColor", active ? beaconMaterial.GetColor("_EmissionColor") : Color.black);
    }

    private void OnDisable() => StopAlarm();
    private void OnDestroy() => Destroy(emissionMaterial);
}
