using Febucci.TextAnimatorCore.Text;
using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public sealed class TAnimSoundWriter : MonoBehaviour
{
    [SerializeField] private AudioSource source;
    [SerializeField] private AudioClip[] sounds = new AudioClip[0];
    [SerializeField, Range(0.1f, 3f)] private float minPitch = 0.9f;
    [SerializeField, Range(0.1f, 3f)] private float maxPitch = 1.1f;
    [SerializeField, Min(0f)] private float minSoundDelay = 0.03f;
    private float lastSoundTime = float.NegativeInfinity;

    private void Awake()
    {
        if (source == null) source = GetComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
    }

    public void PlayCharacter(CharacterData character)
    {
        if (!isActiveAndEnabled || !char.IsLetterOrDigit(character.info.character)
            || sounds == null || sounds.Length == 0 || Time.unscaledTime - lastSoundTime < minSoundDelay)
            return;
        AudioClip clip = sounds[Random.Range(0, sounds.Length)];
        if (clip == null) return;
        source.pitch = Random.Range(Mathf.Min(minPitch, maxPitch), Mathf.Max(minPitch, maxPitch));
        source.clip = clip;
        source.Play();
        lastSoundTime = Time.unscaledTime;
    }

    public void Stop()
    {
        if (source != null) source.Stop();
        lastSoundTime = float.NegativeInfinity;
    }

    private void OnDisable() => Stop();
}
