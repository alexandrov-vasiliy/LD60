using System;
using DialogueSystem;
using MoreMountains.Feedbacks;
using Unity.Cinemachine;
using UnityEngine;

[RequireComponent(typeof(NpcWantItem), typeof(TAnimSoundWriter))]
public abstract class NpcDialog : MonoBehaviour, IInteractable
{
    [Serializable]
    public sealed class FeedbackBinding
    {
        public string id;
        public MMF_Player player;
    }

    [Serializable]
    public sealed class CameraBinding
    {
        public string id;
        public CinemachineCamera camera;
    }

    [SerializeField] private string speaker;
    [SerializeField] private CinemachineCamera dialogueCamera;
    [SerializeField] private WorldText worldText;
    [SerializeField] private Transform playerPosition;
    [SerializeField] private FeedbackBinding[] feedbacks = Array.Empty<FeedbackBinding>();
    [SerializeField] private CameraBinding[] cameras = Array.Empty<CameraBinding>();
    private NpcScenario scenario;
    private ScenarioRunner runner;

    public string CharacterId => Scenario.Id;
    public string SpeakerName => speaker;
    public Transform PlayerPosition => playerPosition;
    public bool IsIntroducing { get; private set; }
    public TAnimSoundWriter SoundWriter { get; private set; }
    public DialoguePresenter Presenter { get; private set; }
    public GameSequencer Sequencer { get; private set; }
    private NpcScenario Scenario => scenario ??= CreateScenario();
    protected abstract NpcScenario CreateScenario();

    protected virtual void Awake()
    {
        SoundWriter = GetComponent<TAnimSoundWriter>();
        Presenter = FindAnyObjectByType<DialoguePresenter>();
        Sequencer = FindAnyObjectByType<GameSequencer>();
        runner = new ScenarioRunner(this);
    }

    public bool CanInteract(PlayerInventory player) => player != null && CanReact() && !Presenter.IsOpen;
    private bool CanReact() => isActiveAndEnabled && Sequencer != null && Presenter != null &&
        Sequencer.CanReceiveItems && !(runner?.ItemsLocked ?? false) && !Presenter.ItemsLocked && (!Presenter.IsOpen || Presenter.Owner == this);

    public void Interact(PlayerInventory player)
    {
        if (CanInteract(player)) Run(ScenarioEvent.Interact, null);
    }

    public void ReactToItem(Item item)
    {
        if (CanReact() && item != null && item.Owner == null && item.WasDropped)
            Run(ScenarioEvent.ItemHit, item);
    }

    private void Run(ScenarioEvent trigger, Item item)
    {
        try
        {
            var context = new ScenarioContext(this, item);
            ScenarioRule rule = Scenario.Select(trigger, context);
            if (rule != null) runner.Start(rule, context);
        }
        catch (Exception exception)
        {
            Debug.LogError($"Scenario '{CharacterId}': {exception}", this);
        }
    }

    public void SetIntroduction(bool value) => IsIntroducing = value;
    public void ShowWorld(string[] lines, Action completed) => worldText.Show(lines, completed);
    public void HideWorld() => worldText.Hide();

    public CinemachineCamera GetCamera(string id = null)
    {
        if (string.IsNullOrEmpty(id)) return dialogueCamera;
        foreach (CameraBinding binding in cameras)
            if (binding.id == id && binding.camera != null) return binding.camera;
        throw new InvalidOperationException($"NPC '{CharacterId}' needs camera '{id}'.");
    }

    public MMF_Player GetFeedback(string id)
    {
        foreach (FeedbackBinding binding in feedbacks)
            if (binding.id == id && binding.player != null) return binding.player;
        throw new InvalidOperationException($"NPC '{CharacterId}' needs feedback '{id}'.");
    }

    public void ValidateSpeech(bool world, string cameraId)
    {
        if (world)
        {
            if (worldText == null || !worldText.IsConfigured)
                throw new InvalidOperationException($"NPC '{CharacterId}' needs configured WorldText.");
        }
        else if (GetCamera(cameraId) == null || playerPosition == null || string.IsNullOrWhiteSpace(speaker))
            throw new InvalidOperationException($"NPC '{CharacterId}' needs a name, camera and player position.");
    }

    public string GetEndingText() => Scenario.GetEnding(new ScenarioContext(this));
    public void CancelScenario() => runner?.Cancel(true);
    protected virtual void OnDisable() => CancelScenario();
}
