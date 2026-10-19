using System;
using Febucci.TextAnimatorCore.Text;
using Febucci.TextAnimatorForUnity;
using MoreMountains.Feedbacks;
using TMPro;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Управляет общим окном разговора: говорящим, камерой, печатью реплик, вводом и блокировкой инвентаря. Инициатор владеет сессией и точкой игрока, а смена говорящего внутри неё не переносит игрока повторно; окончание реплик продолжает сценарий.
/// </summary>
[DefaultExecutionOrder(-40)]
public sealed class DialoguePresenter : MonoBehaviour
{
    [SerializeField] private GameSequencer sequencer;
    [SerializeField] private PlayerControls controls;
    [SerializeField] private GameObject panel;
    [SerializeField] private TMP_Text speakerLabel;
    [SerializeField] private TypewriterComponent typewriter;
    [SerializeField] private MMF_Player openedFeedback;
    [SerializeField] private MMF_Player closedFeedback;
    public bool IsOpen { get; private set; }
    public bool ItemsLocked { get; private set; }
    public NpcDialog Owner { get; private set; }
    public NpcDialog Speaker { get; private set; }
    private Action completed;
    private CinemachineCamera cameraRig;
    private string[] lines;
    private int index, inputFrame;
    private bool muteTypewriter, waitForAction;

    private void OnEnable()
    {
        typewriter.onCharacterVisible.AddListener(PlayCharacterSound);
        Localization.LanguageChanged += RefreshLanguage;
    }

    private void OnDisable()
    {
        Localization.LanguageChanged -= RefreshLanguage;
        typewriter.onCharacterVisible.RemoveListener(PlayCharacterSound);
        NpcDialog owner = Owner;
        owner?.CancelScenario();
        Close(owner);
    }

    private void PlayCharacterSound(CharacterData character)
    {
        if (IsOpen && !muteTypewriter) Speaker?.SoundWriter?.PlayCharacter(character);
    }

    private void ShowLine()
    {
        Speaker?.SoundWriter?.Stop();
        muteTypewriter = false;
        typewriter.ShowText(Localization.Get(lines[index]));
    }

    private void RefreshLanguage()
    {
        if (!IsOpen || Speaker == null || lines == null || index >= lines.Length) return;
        speakerLabel.text = Speaker.SpeakerName;
        muteTypewriter = true;
        Speaker.SoundWriter?.Stop();
        typewriter.StopShowingText();
        typewriter.TextAnimator.SetText(Localization.Get(lines[index]));
        typewriter.TextAnimator.SetVisibilityEntireText(true, false);
    }

    public bool TryShow(NpcDialog owner, NpcDialog actor, string[] text, CinemachineCamera camera,
        Action onCompleted, bool itemsLocked, bool waitForAction = false)
    {
        if (!sequencer.CanReceiveItems || (IsOpen && Owner != owner) || owner == null || actor == null ||
            camera == null || owner.PlayerPosition == null || text == null || text.Length == 0)
            return false;
        bool opening = !IsOpen;
        if (opening)
        {
            controls.SetGameplayEnabled(false);
            controls.Teleport(owner.PlayerPosition);
            controls.SetDialogueMode(true);
            controls.SetBodyVisible(true);
            IsOpen = true;
            Owner = owner;
            panel.SetActive(true);
        }
        Speaker?.SoundWriter?.Stop();
        Speaker = actor;
        actor.HideWorld();
        if (cameraRig != camera)
        {
            if (cameraRig != null) cameraRig.gameObject.SetActive(false);
            cameraRig = camera;
            cameraRig.gameObject.SetActive(true);
        }
        speakerLabel.text = actor.SpeakerName;
        SetItemLock(owner, itemsLocked);
        lines = text;
        index = 0;
        completed = onCompleted;
        this.waitForAction = waitForAction;
        inputFrame = Time.frameCount;
        if (opening) openedFeedback?.PlayFeedbacks();
        ShowLine();
        return true;
    }

    public void SetItemLock(NpcDialog owner, bool value)
    {
        if (!IsOpen || Owner != owner) return;
        ItemsLocked = value;
        controls.SetDialogueInventoryEnabled(!value);
    }

    public void ClearContinuation(NpcDialog owner)
    {
        if (Owner != owner) return;
        completed = null;
        Speaker?.SoundWriter?.Stop();
    }

    private void Update()
    {
        if (IsOpen && (Owner == null || !Owner.isActiveAndEnabled || Speaker == null || !Speaker.isActiveAndEnabled))
        {
            NpcDialog owner = Owner;
            if (owner != null) owner.CancelScenario();
            Close(owner);
            return;
        }
        if (!IsOpen || Time.frameCount <= inputFrame) return;
        Keyboard keyboard = Keyboard.current;
        bool advance = (keyboard != null && (keyboard.eKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame))
            || (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame);
        if (!advance) return;
        inputFrame = Time.frameCount;
        if (typewriter.IsShowingText)
        {
            muteTypewriter = true;
            Speaker?.SoundWriter?.Stop();
            typewriter.SkipTypewriter();
        }
        else if (waitForAction)
        {
            Action action = completed;
            completed = null;
            action?.Invoke();
        }
        else if (++index < lines.Length) ShowLine();
        else
        {
            Action next = completed;
            completed = null;
            if (next != null) next();
            else Close(Owner);
        }
    }

    public void Close(NpcDialog owner)
    {
        if (!IsOpen || Owner != owner) return;
        Speaker?.SoundWriter?.Stop();
        IsOpen = ItemsLocked = false;
        Owner = Speaker = null;
        completed = null;
        panel.SetActive(false);
        controls.SetBodyVisible(false);
        if (cameraRig != null) cameraRig.gameObject.SetActive(false);
        cameraRig = null;
        closedFeedback?.PlayFeedbacks();
        controls.SetDialogueInventoryEnabled(false);
        controls.SetDialogueMode(false);
        controls.SetGameplayEnabled(sequencer.CanReceiveItems && !sequencer.IsPaused);
    }
}
