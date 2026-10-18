using System.Collections;
using System.Text;
using System;
using DialogueSystem;
using MoreMountains.Feedbacks;
using TMPro;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Playables;

/// <summary>
/// Управляет этапами попытки и хранит общее состояние истории и список NPC для совместных сцен. Перед итогами отменяет незавершённые реакции и собирает концовки по текущим фактам; следующая попытка перезагружает сцену.
/// </summary>
[DefaultExecutionOrder(-50)]
public sealed class GameSequencer : MonoBehaviour
{
    public enum GamePhase { Collecting, Cutscene, SecondStage, Results }
    [SerializeField, Min(0f)] private float startDelay = 5f;
    [SerializeField, Min(0.1f)] private float collectingDuration = 60f;
    [SerializeField, Min(0.1f)] private float secondStageDuration = 90f;
    [SerializeField] private PlayerControls controls;
    [SerializeField] private PlayerInventory inventory;
    [SerializeField] private DialoguePresenter dialogue;
    [SerializeField] private SceneLoader loader;
    [SerializeField] private StageAlarm stageAlarm;
    [SerializeField] private Transform collectingSpawn;
    [SerializeField] private Transform secondStageSpawn;
    [SerializeField] private PlayableDirector cutscene;
    [SerializeField] private CinemachineCamera cutsceneCamera;
    [SerializeField] private CinemachineBrain cameraBrain;
    [SerializeField] private TMP_Text timerLabel;
    [SerializeField] private MMF_Player timerTickFeedback;
    [SerializeField] private GameObject pausePanel;
    [SerializeField] private GameObject resultsPanel;
    [SerializeField] private TMP_Text resultsText;
    [SerializeField] private NpcDialog[] npcs;
    public StoryState Story { get; } = new StoryState();
    private float delayRemaining;
    private bool timerStarted;

    public NpcDialog GetNpc(string id)
    {
        NpcDialog found = null;
        foreach (NpcDialog npc in npcs)
        {
            if (npc.CharacterId != id) continue;
            if (found != null) throw new InvalidOperationException($"Duplicate NPC ID '{id}'.");
            found = npc;
        }
        return found != null ? found : throw new InvalidOperationException($"Missing NPC '{id}'.");
    }

    public GamePhase Phase { get; private set; }
    public float Remaining { get; private set; }
    public bool IsPaused { get; private set; }
    public bool IsFreeGameplay => (Phase == GamePhase.Collecting || Phase == GamePhase.SecondStage) && !IsPaused && !dialogue.IsOpen;
    public bool CanReceiveItems => Phase == GamePhase.SecondStage && !IsPaused && Remaining > 0f;

    private void Start()
    {
        Time.timeScale = 1f;
        pausePanel.SetActive(false);
        resultsPanel.SetActive(false);
        cutsceneCamera.gameObject.SetActive(false);
        controls.Teleport(collectingSpawn);
        Phase = GamePhase.Collecting;
        Remaining = collectingDuration;
        delayRemaining = startDelay;
        timerLabel.enabled = false;
        controls.SetGameplayEnabled(true);
    }

    private void Update()
    {
        if ((IsFreeGameplay || IsPaused) && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            SetPaused(!IsPaused);
#if UNITY_EDITOR
        if (Keyboard.current != null && Keyboard.current.xKey.wasPressedThisFrame)
        {
            if (Phase == GamePhase.Cutscene) cutscene.Stop();
            else if (IsFreeGameplay) Remaining = 0f;
        }
#endif
        if (!IsFreeGameplay) return;
        if (!timerStarted)
        {
            delayRemaining -= Time.deltaTime;
            if (delayRemaining > 0f) return;
            timerStarted = true;
            timerLabel.enabled = true;
            stageAlarm?.StartAlarm();
        }
        int previousSeconds = Mathf.CeilToInt(Remaining);
        Remaining = Mathf.Max(0f, Remaining - Time.deltaTime);
        if (Mathf.CeilToInt(Remaining) < previousSeconds) timerTickFeedback?.PlayFeedbacks();
        timerLabel.text = $"{(Phase == GamePhase.Collecting ? "Сбор предметов" : "Помоги людям")} — {Mathf.CeilToInt(Remaining)} с";
        if (Remaining > 0f) return;
        if (Phase == GamePhase.Collecting) StartCoroutine(ChangeStage());
        else Finish();
    }

    private IEnumerator ChangeStage()
    {
        Phase = GamePhase.Cutscene;
        controls.SetGameplayEnabled(false);
        controls.SetBodyVisible(true);
        timerLabel.text = "Время вышло…";
        cutsceneCamera.gameObject.SetActive(true);
        cutscene.time = 0;
        cutscene.Play();
        while (cutscene.state == PlayState.Playing) yield return null;
        stageAlarm?.FadeOut(loader.FadeDuration);
        yield return loader.Fade(true);
        stageAlarm?.StopAlarm();
        controls.Teleport(secondStageSpawn);
        controls.SetBodyVisible(false);
        cutsceneCamera.gameObject.SetActive(false);
        cameraBrain.ResetState();
        yield return null;
        yield return loader.Fade(false);
        Remaining = secondStageDuration;
        Phase = GamePhase.SecondStage;
        controls.SetGameplayEnabled(true);
    }

    public void Resume() => SetPaused(false);

    private void SetPaused(bool value)
    {
        IsPaused = value;
        Time.timeScale = value ? 0f : 1f;
        pausePanel.SetActive(value);
        controls.SetGameplayEnabled(!value);
    }

    private void Finish()
    {
        foreach (NpcDialog npc in npcs) npc.CancelScenario();
        Phase = GamePhase.Results;
        controls.SetGameplayEnabled(false);
        int worth = 0;
        foreach (Item item in inventory.Slots)
            if (item != null && item.TryGetComponent(out WorthItem value)) worth += value.Value;
        StringBuilder text = new StringBuilder();
        if(worth > 0)  
            text.Append($"Я смог заработать: {worth}$");

        foreach (NpcDialog npc in npcs)
        {
            string ending = npc.GetEndingText();
            if (!string.IsNullOrWhiteSpace(ending)) text.AppendLine(ending).AppendLine();
        }
        resultsText.text = text.ToString();
        timerLabel.text = "Итоги";
        resultsPanel.SetActive(true);
    }

    public void NextTry() => loader.LoadGame();
    private void OnDestroy() => Time.timeScale = 1f;
}
