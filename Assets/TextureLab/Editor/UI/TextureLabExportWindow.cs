using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using TextureLab.Runtime;
using UnityEngine.UIElements;

namespace TextureLab.Editor
{
    internal sealed class TextureLabExportWindow : EditorWindow
    {
        private Texture2D source;
        private List<TextureEffectData> effects;
        private Action exported;
        private TextureExportFormat format = TextureExportFormat.Png;
        private int jpgQuality = 90;
        private TextureExportImportSettings importSettings;
        private FilterMode filterMode = FilterMode.Bilinear;
        private Button nextToSourceButton;
        private Label sourceHint;

        internal static void Open(Texture2D source, IReadOnlyList<TextureEffectData> effects, Action exported)
        {
            var window = GetWindow<TextureLabExportWindow>();
            window.titleContent = new GUIContent("Texture Lab Export");
            window.minSize = new Vector2(420f, 260f);
            window.source = source;
            window.effects = CopyEffects(effects);
            window.exported = exported;
            window.Rebuild();
            window.Show();
        }

        public void CreateGUI() => Rebuild();

        private void Rebuild()
        {
            rootVisualElement.Clear();
            rootVisualElement.style.paddingLeft = 10f;
            rootVisualElement.style.paddingRight = 10f;
            rootVisualElement.style.paddingTop = 10f;
            rootVisualElement.style.paddingBottom = 10f;

            var formatField = new EnumField("Format", format);
            formatField.RegisterValueChangedCallback(evt =>
            {
                format = (TextureExportFormat)evt.newValue;
                Rebuild();
            });
            rootVisualElement.Add(formatField);

            if (format == TextureExportFormat.Jpg)
            {
                var quality = new SliderInt("JPG Quality", 1, 100) { value = jpgQuality, showInputField = true };
                quality.RegisterValueChangedCallback(evt => jpgQuality = evt.newValue);
                rootVisualElement.Add(quality);
            }

            var settings = new EnumField("Import Settings", importSettings);
            settings.RegisterValueChangedCallback(evt => importSettings = (TextureExportImportSettings)evt.newValue);
            rootVisualElement.Add(settings);

            var filters = new PopupField<FilterMode>("Filter", new List<FilterMode> { FilterMode.Point, FilterMode.Bilinear },
                filterMode == FilterMode.Point ? 0 : 1);
            filters.RegisterValueChangedCallback(evt => filterMode = evt.newValue);
            rootVisualElement.Add(filters);

            sourceHint = new Label();
            sourceHint.style.whiteSpace = WhiteSpace.Normal;
            sourceHint.style.marginTop = 8f;
            rootVisualElement.Add(sourceHint);

            nextToSourceButton = new Button(SaveNextToSource) { text = "Save Next to Source" };
            rootVisualElement.Add(nextToSourceButton);
            rootVisualElement.Add(new Button(SaveInAssets) { text = "Save in Assets…" });
            rootVisualElement.Add(new Button(SaveAnywhere) { text = "Save Anywhere…" });
            UpdateSourceControls();
        }

        private void UpdateSourceControls()
        {
            bool canSaveNextToSource = source != null && AssetDatabase.GetAssetPath(source).StartsWith("Assets/", StringComparison.OrdinalIgnoreCase);
            nextToSourceButton.SetEnabled(canSaveNextToSource);
            sourceHint.text = canSaveNextToSource
                ? "Exports always process at the source texture's full resolution."
                : "Save Next to Source is unavailable for textures outside Assets.";
        }

        private void SaveNextToSource()
        {
            string sourcePath = AssetDatabase.GetAssetPath(source);
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string sourceFullPath = Path.GetFullPath(Path.Combine(projectRoot, sourcePath));
            string destination = Path.Combine(
                Path.GetDirectoryName(sourceFullPath),
                Path.GetFileNameWithoutExtension(sourceFullPath) + "_processed." + Extension);
            Export(destination);
        }

        private void SaveInAssets()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Export Texture Lab Result",
                source.name + "_processed",
                Extension,
                "Choose an Assets location for the processed texture.",
                "Assets");
            if (!string.IsNullOrEmpty(path))
                Export(ToFullPath(path));
        }

        private void SaveAnywhere()
        {
            string path = EditorUtility.SaveFilePanel(
                "Export Texture Lab Result",
                Application.dataPath,
                source.name + "_processed",
                Extension);
            if (!string.IsNullOrEmpty(path))
                Export(path);
        }

        private void Export(string destination)
        {
            destination = Path.ChangeExtension(destination, Extension);
            if (format == TextureExportFormat.Jpg && !EditorUtility.DisplayDialog(
                    "JPG Drops Alpha",
                    "JPG export permanently drops alpha. Continue?",
                    "Export JPG",
                    "Cancel"))
                return;

            if (File.Exists(destination) && !EditorUtility.DisplayDialog(
                    "Overwrite Texture",
                    $"Replace '{Path.GetFileName(destination)}'?",
                    "Overwrite",
                    "Cancel"))
                return;

            try
            {
                TextureExporter.Export(source, effects, destination, format, jpgQuality, importSettings, filterMode);
                exported?.Invoke();
                ShowNotification(new GUIContent("Export complete."));
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                ShowNotification(new GUIContent("Export failed. See Console."));
            }
        }

        private string Extension => format == TextureExportFormat.Png ? "png" : "jpg";

        private static string ToFullPath(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
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
