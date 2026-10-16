using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using TextureLab.Runtime;

namespace TextureLab.Editor
{
    internal sealed class VariationsWindow : EditorWindow
    {
        private Texture2D source;
        private List<TextureEffectData> baseEffects;
        private List<List<TextureEffectData>> variations;
        private readonly List<RenderTexture> thumbnails = new();
        private TextureProcessor processor;
        private VisualElement grid;
        private Button applyButton;
        private Action<List<TextureEffectData>> apply;
        private int seed;
        private int selectedIndex = -1;

        internal static void Open(Texture2D source, IReadOnlyList<TextureEffectData> effects, Action<List<TextureEffectData>> apply)
        {
            var window = GetWindow<VariationsWindow>();
            window.titleContent = new GUIContent("Texture Lab Variations");
            window.minSize = new Vector2(540f, 520f);
            window.source = source;
            window.baseEffects = CopyEffects(effects);
            window.apply = apply;
            window.seed = Environment.TickCount;
            window.Generate();
            window.Rebuild();
            window.Show();
        }

        private void OnEnable()
        {
            processor ??= new TextureProcessor();
        }

        private void OnDisable()
        {
            ReleaseThumbnails();
            processor?.Dispose();
            processor = null;
        }

        public void CreateGUI()
        {
            rootVisualElement.style.flexGrow = 1f;
            Rebuild();
        }

        private void Rebuild()
        {
            if (rootVisualElement == null)
                return;

            rootVisualElement.Clear();
            var toolbar = new VisualElement();
            toolbar.style.flexDirection = FlexDirection.Row;
            toolbar.style.paddingLeft = 8f;
            toolbar.style.paddingRight = 8f;
            toolbar.style.paddingTop = 6f;
            toolbar.style.paddingBottom = 6f;

            var seedField = new IntegerField("Seed") { value = seed };
            seedField.style.flexGrow = 1f;
            seedField.RegisterValueChangedCallback(evt => seed = evt.newValue);
            toolbar.Add(seedField);
            toolbar.Add(new Button(() =>
            {
                seed = Environment.TickCount;
                Generate();
                Rebuild();
            }) { text = "New Seed" });
            toolbar.Add(new Button(() =>
            {
                Generate();
                Rebuild();
            }) { text = "Generate" });
            rootVisualElement.Add(toolbar);

            grid = new VisualElement();
            grid.style.flexDirection = FlexDirection.Row;
            grid.style.flexWrap = Wrap.Wrap;
            grid.style.flexGrow = 1f;
            grid.style.paddingLeft = 8f;
            grid.style.paddingRight = 8f;
            grid.style.paddingBottom = 8f;
            rootVisualElement.Add(grid);
            BuildGrid();

            var footer = new VisualElement();
            footer.style.flexDirection = FlexDirection.Row;
            footer.style.paddingLeft = 8f;
            footer.style.paddingRight = 8f;
            footer.style.paddingBottom = 8f;
            applyButton = new Button(ApplySelected) { text = "Apply Variation" };
            applyButton.style.flexGrow = 1f;
            applyButton.SetEnabled(selectedIndex >= 0);
            footer.Add(applyButton);
            footer.Add(new Button(Close) { text = "Close" });
            rootVisualElement.Add(footer);
        }

        private void BuildGrid()
        {
            if (grid == null || variations == null)
                return;

            for (int i = 0; i < variations.Count; i++)
            {
                int index = i;
                var card = new VisualElement();
                card.style.width = Length.Percent(33.333f);
                card.style.paddingLeft = 3f;
                card.style.paddingRight = 3f;
                card.style.paddingTop = 3f;
                card.style.paddingBottom = 3f;
                card.style.borderLeftWidth = selectedIndex == index ? 2f : 0f;
                card.style.borderRightWidth = selectedIndex == index ? 2f : 0f;
                card.style.borderTopWidth = selectedIndex == index ? 2f : 0f;
                card.style.borderBottomWidth = selectedIndex == index ? 2f : 0f;
                card.style.borderLeftColor = new Color(0.22f, 0.55f, 0.85f);
                card.style.borderRightColor = new Color(0.22f, 0.55f, 0.85f);
                card.style.borderTopColor = new Color(0.22f, 0.55f, 0.85f);
                card.style.borderBottomColor = new Color(0.22f, 0.55f, 0.85f);
                card.RegisterCallback<ClickEvent>(_ =>
                {
                    selectedIndex = index;
                    Rebuild();
                });

                card.Add(new Label($"Variation {index + 1}"));
                var image = new Image
                {
                    image = thumbnails[index],
                    scaleMode = ScaleMode.ScaleToFit
                };
                image.style.width = Length.Percent(100f);
                image.style.height = 130f;
                card.Add(image);
                grid.Add(card);
            }
        }

        private void Generate()
        {
            if (source == null || baseEffects == null)
                return;

            processor ??= new TextureProcessor();
            ReleaseThumbnails();
            variations = VariationGenerator.Generate(baseEffects, seed);
            foreach (List<TextureEffectData> variation in variations)
            {
                processor.Process(source, variation, 256);
                RenderTexture result = processor.Result;
                var thumbnail = new RenderTexture(result.width, result.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                thumbnail.Create();
                Graphics.Blit(result, thumbnail);
                thumbnails.Add(thumbnail);
            }

            selectedIndex = -1;
        }

        private void ApplySelected()
        {
            if (selectedIndex < 0 || selectedIndex >= variations.Count)
                return;

            apply?.Invoke(variations[selectedIndex]);
            Close();
        }

        private void ReleaseThumbnails()
        {
            foreach (RenderTexture thumbnail in thumbnails)
            {
                if (thumbnail == null)
                    continue;

                thumbnail.Release();
                DestroyImmediate(thumbnail);
            }

            thumbnails.Clear();
        }

        private static List<TextureEffectData> CopyEffects(IReadOnlyList<TextureEffectData> source)
        {
            var copies = new List<TextureEffectData>(source.Count);
            foreach (TextureEffectData effect in source)
                copies.Add(effect.Duplicate());
            return copies;
        }
    }
}
