using System;
using System.Collections.Generic;
using MoreMountains.Feedbacks;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class PlayerInventory : MonoBehaviour
{
    [SerializeField, Min(1)] private int capacity = 3;
    [SerializeField] private HeldItemPresenter presenter;
    [SerializeField] private Transform dropOrigin;
    [SerializeField] private Transform playerDropOrigin;
    [SerializeField, Min(0f)] private float dropDistance = 0.8f;
    [SerializeField, Min(0f)] private float dropImpulse = 2f;
    [SerializeField] private InputActionReference dropAction;
    [SerializeField] private InputActionReference scrollAction;
    [SerializeField] private MMF_Player pickupFeedback;
    [SerializeField] private MMF_Player dropFeedback;

    public IReadOnlyList<Item> Slots { get; private set; }
    public event Action Changed;
    public int ActiveIndex { get; private set; }
    public Item ActiveItem => items[ActiveIndex];
    public bool UsePlayerDropOrigin { get; set; }
    private Transform DropOrigin => UsePlayerDropOrigin ? playerDropOrigin : dropOrigin;
    private Item[] items;
    private bool enabledDrop, enabledScroll;

    private void Awake()
    {
        items = new Item[Mathf.Max(1, capacity)];
        Slots = Array.AsReadOnly(items);
        if (presenter == null || dropOrigin == null || playerDropOrigin == null || dropAction == null || scrollAction == null)
        {
            Debug.LogError("PlayerInventory needs a presenter, camera and player drop origins, Drop and SelectSlot actions.", this);
            enabled = false;
        }
    }

    private void OnEnable()
    {
        enabledDrop = dropAction != null && !dropAction.action.enabled;
        enabledScroll = scrollAction != null && !scrollAction.action.enabled;
        if (enabledDrop) dropAction.action.Enable();
        if (enabledScroll) scrollAction.action.Enable();
    }

    private void OnDisable()
    {
        if (presenter != null) presenter.CompleteFlight();
        if (enabledDrop) dropAction.action.Disable();
        if (enabledScroll) scrollAction.action.Disable();
    }

    private void Update()
    {
        if (dropAction != null && dropAction.action.WasPressedThisFrame()) DropActive();
        float scroll = scrollAction != null ? scrollAction.action.ReadValue<float>() : 0f;
        if (scroll != 0f) SelectSlot((ActiveIndex + (scroll > 0f ? 1 : -1) + items.Length) % items.Length);
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;
        for (int index = 0; index < Mathf.Min(9, items.Length); index++)
        {
            if (!keyboard[(Key)((int)Key.Digit1 + index)].wasPressedThisFrame) continue;
            if (index != ActiveIndex) SelectSlot(index);
            break;
        }
    }

    public bool Pickup(Item item)
    {
        if (item == null || item.Owner != null) return false;
        presenter.CompleteFlight();
        int index = Array.FindIndex(items, slot => slot == null);
        if (index < 0) { index = ActiveIndex; DropActive(); }
        if (ActiveItem != null) ActiveItem.gameObject.SetActive(false);
        item.Store(this);
        items[index] = item;
        ActiveIndex = index;
        presenter.Show(item, true);
        pickupFeedback?.PlayFeedbacks();
        Changed?.Invoke();
        return true;
    }

    public void SelectSlot(int index)
    {
        if (index < 0 || index >= items.Length) throw new ArgumentOutOfRangeException(nameof(index));
        presenter.CompleteFlight();
        if (ActiveItem != null) ActiveItem.gameObject.SetActive(false);
        ActiveIndex = index;
        presenter.Show(ActiveItem, false);
        Changed?.Invoke();
    }

    public bool Contains(ItemId id) => Array.Exists(items, item => item != null && item.Id == id);

    public bool TryExtract(Item item)
    {
        if (item == null) return false;
        int index = Array.IndexOf(items, item);
        if (index < 0) return false;
        presenter.CompleteFlight();
        items[index] = null;
        item.Release(DropOrigin.position, DropOrigin.rotation, Vector3.zero);
        Changed?.Invoke();
        return true;
    }

    public void DropActive()
    {
        presenter.CompleteFlight();
        Item item = ActiveItem;
        if (item == null) return;
        items[ActiveIndex] = null;
        Transform origin = DropOrigin;
        // shortcut: drops can overlap walls; add clearance checks only if playtesting needs them.
        item.Release(origin.position + origin.forward * dropDistance, origin.rotation, origin.forward * dropImpulse, playImpactFeedback: true);
        dropFeedback?.PlayFeedbacks();
        Changed?.Invoke();
    }

    private void OnDestroy()
    {
        if (items == null) return;
        foreach (Item item in items)
            if (item != null) item.Release(transform.position, transform.rotation, Vector3.zero);
    }
}
