using System;
using System.Collections;
using DG.Tweening;
using DialogueSystem;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Подключает историю Егора и выполняет съёмку: выбирает полароид, ждёт Q, запускает эффект и передаёт карточку. Регистрирует очистку корутины, анимации, карточки и эффекта при завершении или отмене сценария.
/// </summary>
public sealed class EgorNpcDialog : NpcDialog
{
    [SerializeField] private GameObject photoPrefab;
    [SerializeField] private Transform photoTarget;
    public PlayerInventory Inventory { get; private set; }
    protected override NpcScenario CreateScenario() => new EgorScenario();

    protected override void Awake()
    {
        base.Awake();
        Inventory = FindAnyObjectByType<PlayerInventory>();
    }

    public void ValidatePhoto()
    {
        ValidateSpeech(false, null);
        GetFeedback("PhotoTaken");
        if (Inventory == null || photoPrefab == null || photoTarget == null)
            throw new InvalidOperationException("Egor needs the inventory, photo prefab and photo target.");
    }

    public void PreparePolaroid(Item incoming)
    {
        if (incoming != null && incoming.Id == ItemId.Polaroid && !Inventory.Pickup(incoming))
            throw new InvalidOperationException("Cannot return the polaroid to the player.");
        for (int i = 0; i < Inventory.Slots.Count; i++)
        {
            if (Inventory.Slots[i] == null || Inventory.Slots[i].Id != ItemId.Polaroid) continue;
            Inventory.SelectSlot(i);
            return;
        }
        throw new InvalidOperationException("The player no longer has the polaroid.");
    }

    public void WaitForPhoto(ScenarioRunner runner)
    {
        Action resume = runner.Await();
        if (!Presenter.TryShow(this, this, new[] { "Не двигаться? Хорошо" }, GetCamera(), null, true, true))
            throw new InvalidOperationException("Cannot open Egor's photo dialogue.");
        Item item = Inventory.ActiveItem;
        if (item == null || item.Id != ItemId.Polaroid ||
            !item.TryGetComponent(out Polaroid polaroid) || polaroid.PhotoOrigin == null)
            throw new InvalidOperationException("The selected polaroid needs a photo origin.");
        GameObject card = null;
        Tween flight = null;
        Coroutine routine = null;
        var feedback = GetFeedback("PhotoTaken");
        runner.OnEnd(() =>
        {
            if (routine != null) StopCoroutine(routine);
            flight?.Kill();
            if (card != null) Destroy(card);
            feedback.StopFeedbacks();
            feedback.RestoreInitialValues();
        });
        routine = StartCoroutine(Capture());

        IEnumerator Capture()
        {
            // The Q that threw the camera must not also take the picture.
            yield return null;
            while (Keyboard.current == null || !Keyboard.current.qKey.wasPressedThisFrame) yield return null;
            feedback.PlayFeedbacks();
            yield return new WaitForSeconds(1f);
            card = Instantiate(photoPrefab, polaroid.PhotoOrigin.position, photoTarget.rotation);
            flight = DOTween.Sequence()
                .Append(card.transform.DOMove(photoTarget.position, 0.5f).SetEase(Ease.OutQuad))
                .Append(card.transform.DOScale(Vector3.zero, 0.2f));
            yield return flight.WaitForCompletion();
            Destroy(card);
            card = null;
            routine = null;
            resume();
        }
    }
}
