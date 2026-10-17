using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class PlayerInventory : MonoBehaviour
{
    [SerializeField, Min(1)] private int capacity = 3;
    [SerializeField] private HeldItemPresenter presenter;
    [SerializeField] private Transform dropOrigin;
    [SerializeField, Min(0f)] private float dropDistance = 0.8f;
    [SerializeField, Min(0f)] private float dropImpulse = 2f;
    [SerializeField] private InputActionReference dropAction;
    [SerializeField] private InputActionReference scrollAction;

    public IReadOnlyList<Item> Slots { get; private set; }
    public int ActiveIndex { get; private set; }
    public Item ActiveItem => items[ActiveIndex];
    private Item[] items;
    private bool enabledDrop, enabledScroll;

    private void Awake()
    {
        items = new Item[Mathf.Max(1, capacity)];
        Slots = Array.AsReadOnly(items);
        if (presenter == null || dropOrigin == null || dropAction == null || scrollAction == null)
        {
            Debug.LogError("PlayerInventory needs a presenter, drop origin, Drop and SelectSlot actions.", this);
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
        return true;
    }

    public void SelectSlot(int index)
    {
        if (index < 0 || index >= items.Length) throw new ArgumentOutOfRangeException(nameof(index));
        presenter.CompleteFlight();
        if (ActiveItem != null) ActiveItem.gameObject.SetActive(false);
        ActiveIndex = index;
        presenter.Show(ActiveItem, false);
    }

    public bool Contains(ItemId id) => Array.Exists(items, item => item != null && item.Id == id);

    public bool TryExtract(Item item)
    {
        if (item == null) return false;
        int index = Array.IndexOf(items, item);
        if (index < 0) return false;
        presenter.CompleteFlight();
        items[index] = null;
        item.Release(dropOrigin.position, dropOrigin.rotation, Vector3.zero);
        return true;
    }

    public void DropActive()
    {
        presenter.CompleteFlight();
        Item item = ActiveItem;
        if (item == null) return;
        items[ActiveIndex] = null;
        // shortcut: drops can overlap walls; add clearance checks only if playtesting needs them.
        item.Release(dropOrigin.position + dropOrigin.forward * dropDistance,
            dropOrigin.rotation, dropOrigin.forward * dropImpulse);
    }

    private void OnDestroy()
    {
        if (items == null) return;
        foreach (Item item in items)
            if (item != null) item.Release(transform.position, transform.rotation, Vector3.zero);
    }
}
