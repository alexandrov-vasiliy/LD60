using System;
using Febucci.TextAnimatorForUnity;
using MoreMountains.Feedbacks;
using TMPro;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

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
    public NpcDialog Speaker { get; private set; }
    private Action completed;
    private CinemachineCamera cameraRig;
    private string[] lines;
    private int index, inputFrame;

    public bool TryOpen(NpcDialog npc, string speaker, string[] text, CinemachineCamera npcCamera,
        Transform playerPosition, Action onCompleted = null)
    {
        if (!sequencer.CanReceiveItems || (IsOpen && Speaker != npc) || text == null || text.Length == 0)
            return false;
        if (npcCamera == null || playerPosition == null)
        {
            Debug.LogError("NPC dialogue needs a camera and player position.", npc);
            return false;
        }
        if (!IsOpen)
        {
            controls.SetGameplayEnabled(false);
            controls.Teleport(playerPosition);
            controls.SetDialogueInventoryEnabled(true);
            controls.SetBodyVisible(true);
            IsOpen = true;
            Speaker = npc;
            cameraRig = npcCamera;
            cameraRig.gameObject.SetActive(true);
            panel.SetActive(true);
            speakerLabel.text = speaker;
            openedFeedback?.PlayFeedbacks();
        }
        lines = text;
        index = 0;
        completed = onCompleted;
        inputFrame = Time.frameCount;
        typewriter.ShowText(lines[index]);
        return true;
    }

    private void Update()
    {
        if (!IsOpen || Time.frameCount <= inputFrame) return;
        Keyboard keyboard = Keyboard.current;
        bool advance = (keyboard != null && (keyboard.eKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame))
            || (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame);
        if (!advance) return;
        inputFrame = Time.frameCount;
        if (typewriter.IsShowingText) typewriter.SkipTypewriter();
        else if (++index < lines.Length) typewriter.ShowText(lines[index]);
        else
        {
            Action next = completed;
            completed = null;
            if (next != null) next();
            else Close();
        }
    }

    private void Close()
    {
        IsOpen = false;
        Speaker = null;
        completed = null;
        panel.SetActive(false);
        controls.SetBodyVisible(false);
        cameraRig.gameObject.SetActive(false);
        closedFeedback?.PlayFeedbacks();
        controls.SetDialogueInventoryEnabled(false);
        controls.SetGameplayEnabled(true);
    }
}
