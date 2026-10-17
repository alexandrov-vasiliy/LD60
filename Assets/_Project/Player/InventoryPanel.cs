using UnityEngine;

public sealed class InventoryPanel : MonoBehaviour
{
    [SerializeField] private PlayerInventory inventory;
    [SerializeField] private ItemPreviewRenderer previews;
    [SerializeField] private RectTransform slotTemplate;
    [SerializeField] private Color normalBorder = new Color(0.5f, 0.5f, 0.5f, 1f);
    [SerializeField] private Color selectedBorder = Color.white;

    private UnityEngine.UI.RawImage[] images;
    private UnityEngine.UI.Outline[] borders;
    private Item[] shown;
    private bool dirty;

    private void OnEnable()
    {
        if (inventory == null || previews == null || slotTemplate == null)
        {
            Debug.LogError("InventoryPanel needs an inventory, preview renderer and slot template.", this);
            enabled = false;
            return;
        }
        inventory.Changed += MarkDirty;
        dirty = true;
    }

    private void Start()
    {
        int count = inventory.Slots.Count;
        images = new UnityEngine.UI.RawImage[count];
        borders = new UnityEngine.UI.Outline[count];
        shown = new Item[count];
        for (int i = 0; i < count; i++)
        {
            var slot = Instantiate(slotTemplate, slotTemplate.parent);
            slot.name = "Slot" + (i + 1);
            images[i] = slot.GetComponentInChildren<UnityEngine.UI.RawImage>(true);
            borders[i] = slot.GetComponent<UnityEngine.UI.Outline>();
            slot.gameObject.SetActive(true);
        }
    }

    private void MarkDirty() => dirty = true;

    private void LateUpdate()
    {
        if (!dirty) return;
        dirty = false;
        for (int i = 0; i < images.Length; i++)
        {
            Item item = inventory.Slots[i];
            if (shown[i] != item || (item == null && images[i].texture != null))
            {
                Release(images[i]);
                shown[i] = item;
                if (item != null) images[i].texture = previews.Render(item);
            }
            images[i].enabled = images[i].texture != null;
            bool selected = i == inventory.ActiveIndex;
            borders[i].effectColor = selected ? selectedBorder : normalBorder;
            borders[i].effectDistance = Vector2.one * (selected ? 3f : 1f);
        }
    }

    private static void Release(UnityEngine.UI.RawImage image)
    {
        if (image.texture is RenderTexture texture)
        {
            texture.Release();
            Destroy(texture);
        }
        image.texture = null;
    }

    private void OnDisable()
    {
        if (inventory != null) inventory.Changed -= MarkDirty;
    }

    private void OnDestroy()
    {
        if (images == null) return;
        foreach (var image in images)
            if (image != null) Release(image);
    }
}
