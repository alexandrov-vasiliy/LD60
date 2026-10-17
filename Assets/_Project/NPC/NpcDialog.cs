using System;
using MoreMountains.Feedbacks;
using Unity.Cinemachine;
using UnityEngine;

[RequireComponent(typeof(NpcWantItem))]
public abstract class NpcDialog : MonoBehaviour, IInteractable
{
    [System.Serializable]
    public sealed class Reaction
    {
        public MMF_Player feedback;
    }

    [SerializeField] private string speaker;
    [SerializeField] private CinemachineCamera dialogueCamera;
    [SerializeField] private WorldText worldText;
    [SerializeField] private Transform playerPosition;
    [SerializeField] private Reaction beforeItem = new Reaction();
    [SerializeField] private Reaction needItem = new Reaction();
    [SerializeField] private Reaction earlyWrongItem = new Reaction();
    [SerializeField] private Reaction earlyCorrectItem = new Reaction();
    [SerializeField] private Reaction beforeMeeting = new Reaction();
    [SerializeField] private Reaction itemReceived = new Reaction();
    [SerializeField] private Reaction afterItem = new Reaction();
    [SerializeField] private Reaction wrongItem = new Reaction();
    public bool HasMet { get; private set; }
    protected NpcWantItem Want { get; private set; }
    private DialoguePresenter presenter;
    private GameSequencer sequencer;
    private bool introducing;

    protected virtual void Awake()
    {
        Want = GetComponent<NpcWantItem>();
        presenter = FindAnyObjectByType<DialoguePresenter>();
        sequencer = FindAnyObjectByType<GameSequencer>();
    }

    public bool CanInteract(PlayerInventory player) => isActiveAndEnabled && player != null && sequencer.CanReceiveItems && !presenter.IsOpen;

    public void Interact(PlayerInventory player)
    {
        if (!CanInteract(player)) return;
        if (Want.HasReceivedItem) ShowWorld(afterItem, AfterItem());
        else if (HasMet) ShowNeed();
        else
        {
            introducing = true;
            if (ShowDialog(beforeItem, BeforeItem(), ShowNeed)) HasMet = true;
            else introducing = false;
        }
    }

    public void ReactToItem(bool accepted)
    {
        if (!HasMet)
        {
            ShowWorld(beforeMeeting, BeforeMeeting());
            return;
        }
        bool early = presenter.Speaker == this && introducing;
        introducing = false;
        if (accepted)
        {
            if (early) ShowDialog(earlyCorrectItem, EarlyCorrectItem(), ShowReceived);
            else ShowReceived();
        }
        else if (presenter.Speaker == this)
        {
            if (early) ShowDialog(earlyWrongItem, EarlyWrongItem(), ShowWrong);
            else ShowWrong();
        }
        else ShowWorld(wrongItem, WrongItem());
    }

    private void ShowNeed()
    {
        introducing = false;
        ShowDialog(needItem, NeedItem());
    }

    private void ShowReceived() => ShowDialog(itemReceived, ItemReceived());
    private void ShowWrong() => ShowDialog(wrongItem, WrongItem(), ShowNeed);

    private bool ShowDialog(Reaction reaction, string[] lines, Action completed = null)
    {
        if (!presenter.TryOpen(this, speaker, lines, dialogueCamera, playerPosition, completed)) return false;
        worldText.Hide();
        reaction.feedback?.PlayFeedbacks();
        return true;
    }

    private void ShowWorld(Reaction reaction, string[] lines)
    {
        worldText.Show(lines);
        reaction.feedback?.PlayFeedbacks();
    }

    protected abstract string[] BeforeItem();
    protected abstract string[] NeedItem();
    protected abstract string[] EarlyWrongItem();
    protected abstract string[] EarlyCorrectItem();
    protected abstract string[] BeforeMeeting();
    protected abstract string[] ItemReceived();
    protected abstract string[] AfterItem();
    protected abstract string[] WrongItem();
    public abstract string GetEndingText();
}
