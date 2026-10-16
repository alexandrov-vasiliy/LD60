using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using TextureLab.Runtime;

namespace TextureLab.Editor
{
    internal sealed class TextureLabWindow : EditorWindow
    {
        private static readonly List<int> BlockSizes = new() { 1, 2, 4, 8, 16, 32, 64 };
        private static readonly List<int> PaletteSizes = new() { 4, 8, 16, 32, 64 };
        private static readonly List<string> PreviewTileLabels = new() { "1×1", "2×2", "3×3", "4×4", "5×5", "6×6", "7×7", "8×8" };
        private static readonly List<int> PreviewTileCounts = new() { 1, 2, 3, 4, 5, 6, 7, 8 };
        private static readonly List<string> PreviewZoomLabels = new() { "Fit", "25%", "50%", "100%", "200%" };
        private static readonly List<int> PreviewZoomValues = new() { 0, 25, 50, 100, 200 };
        private static readonly List<int> PreviewQualities = new() { 512, 1024, 2048 };
        private static readonly string[] MixerChannelNames = { "Red", "Green", "Blue" };
        private const string EditorStyleSheetGuid = "6b7a23fa744e31e43bb377b355e86e86";
        private readonly Dictionary<string, int> selectedMixerOutputs = new();
        private readonly List<Image> previewImages = new();
        private TextureLabSession session;
        private TextureProcessor processor;
        private ObjectField sourceField;
        private ObjectField presetField;
        private TextField presetNameField;
        private Button originalButton;
        private Button applyPresetButton;
        private Button overwritePresetButton;
        private Button duplicatePresetButton;
        private Button renamePresetButton;
        private Label presetHint;
        private ScrollView previewScroll;
        private VisualElement previewGrid;
        private Label previewMessage;
        private ListView effectList;
        private Texture previewTexture;
        private TextureLabPreset selectedPreset;
        private bool showOriginal;
        private ExposureBrushEffectData activeBrush;
        private Image activeBrushImage;
        private int activeBrushPointerId = -1;
        private VisualElement brushCursor;
        private VisualElement brushHardnessCursor;
        private Image brushCursorImage;
        private Vector2 brushCursorPosition;

        [MenuItem("Tools/Texture Lab/Open Window")]
        private static void Open()
        {
            var window = GetWindow<TextureLabWindow>();
            window.titleContent = new GUIContent("Texture Lab");
            window.minSize = new Vector2(760f, 480f);
        }

        private void OnEnable()
        {
            session = TextureLabSession.instance;
            Undo.undoRedoPerformed -= OnUndoRedo;
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            EndBrushStroke();
            showOriginal = false;
            processor?.Dispose();
            processor = null;
        }

        public void CreateGUI()
        {
            session ??= TextureLabSession.instance;
            processor ??= new TextureProcessor();
            session.ResolvePaletteReferences(LoadAvailablePalettes());
            rootVisualElement.Clear();
            rootVisualElement.AddToClassList("texture-lab-root");

            string styleSheetPath = AssetDatabase.GUIDToAssetPath(EditorStyleSheetGuid);
            StyleSheet styles = AssetDatabase.LoadAssetAtPath<StyleSheet>(styleSheetPath);
            if (styles != null)
                rootVisualElement.styleSheets.Add(styles);

            rootVisualElement.Add(BuildSourceBar());
            rootVisualElement.Add(BuildPresetBar());

            var workspace = new VisualElement();
            workspace.AddToClassList("workspace");
            workspace.Add(BuildPreview());
            workspace.Add(BuildStack());
            rootVisualElement.Add(workspace);

            sourceField.SetValueWithoutNotify(session.SourceTexture);
            ProcessPreview();
        }

        [Shortcut("Texture Lab/Toggle Original", typeof(TextureLabWindow), KeyCode.Space)]
        private static void ToggleOriginalShortcut(ShortcutArguments arguments)
        {
            if (arguments.context is TextureLabWindow window)
                window.ToggleOriginal();
        }

        private VisualElement BuildSourceBar()
        {
            var bar = new VisualElement();
            bar.AddToClassList("source-bar");

            var label = new Label("SOURCE");
            label.AddToClassList("section-label");
            bar.Add(label);

            sourceField = new ObjectField
            {
                objectType = typeof(Texture2D),
                allowSceneObjects = false
            };
            sourceField.AddToClassList("source-field");
            sourceField.RegisterValueChangedCallback(evt => SetSource(evt.newValue as Texture2D));
            bar.Add(sourceField);
            bar.Add(new Button(OpenVariations) { text = "Variations" });
            bar.Add(new Button(OpenExport) { text = "Export" });
            return bar;
        }

        private VisualElement BuildPresetBar()
        {
            var bar = new VisualElement();
            bar.AddToClassList("preset-bar");

            var label = new Label("PRESET");
            label.AddToClassList("section-label");
            bar.Add(label);

            presetField = new ObjectField
            {
                objectType = typeof(TextureLabPreset),
                allowSceneObjects = false,
                value = selectedPreset
            };
            presetField.AddToClassList("preset-field");
            presetField.RegisterValueChangedCallback(evt =>
            {
                selectedPreset = evt.newValue as TextureLabPreset;
                if (presetNameField != null)
                    presetNameField.SetValueWithoutNotify(selectedPreset != null ? selectedPreset.name : string.Empty);
                UpdatePresetControls();
            });
            bar.Add(presetField);

            bar.Add(new Button(SavePresetAs) { text = "Save As…" });

            applyPresetButton = new Button(ApplyPreset) { text = "Apply" };
            bar.Add(applyPresetButton);

            overwritePresetButton = new Button(OverwritePreset) { text = "Overwrite" };
            bar.Add(overwritePresetButton);

            duplicatePresetButton = new Button(DuplicatePreset) { text = "Duplicate" };
            bar.Add(duplicatePresetButton);

            presetNameField = new TextField { value = selectedPreset != null ? selectedPreset.name : string.Empty };
            presetNameField.AddToClassList("preset-name-field");
            presetNameField.tooltip = "New asset name";
            bar.Add(presetNameField);

            renamePresetButton = new Button(RenamePreset) { text = "Rename" };
            bar.Add(renamePresetButton);
            bar.Add(new Button(ResetStack) { text = "Reset Stack" });

            presetHint = new Label();
            presetHint.AddToClassList("preset-hint");
            bar.Add(presetHint);
            UpdatePresetControls();
            return bar;
        }

        private void UpdatePresetControls()
        {
            bool hasPreset = selectedPreset != null;
            bool editable = IsEditablePreset(selectedPreset);
            applyPresetButton?.SetEnabled(hasPreset);
            overwritePresetButton?.SetEnabled(editable);
            duplicatePresetButton?.SetEnabled(hasPreset);
            renamePresetButton?.SetEnabled(editable);
            presetNameField?.SetEnabled(editable);
            if (presetHint != null)
                presetHint.text = hasPreset && !editable ? "Built-in preset: duplicate it to edit." : string.Empty;
        }

        private static bool IsEditablePreset(TextureLabPreset preset)
        {
            if (preset == null)
                return false;

            return AssetDatabase.GetAssetPath(preset).StartsWith("Assets/", StringComparison.OrdinalIgnoreCase);
        }

        private VisualElement BuildPreview()
        {
            var pane = new VisualElement();
            pane.AddToClassList("preview-pane");
            pane.RegisterCallback<DragUpdatedEvent>(_ => UpdateTextureDrag());
            pane.RegisterCallback<DragPerformEvent>(_ => PerformTextureDrag());

            var toolbar = new VisualElement();
            toolbar.AddToClassList("preview-toolbar");

            originalButton = new Button(ToggleOriginal) { text = "Original", tooltip = "Toggle original preview (Space)" };
            originalButton.AddToClassList("preview-original-button");
            toolbar.Add(originalButton);

            int tileIndex = Mathf.Max(0, PreviewTileCounts.IndexOf(session.PreviewTiles));
            var tiles = new PopupField<string>(PreviewTileLabels, tileIndex) { tooltip = "Tile preview" };
            tiles.RegisterValueChangedCallback(evt =>
            {
                int index = Mathf.Max(0, PreviewTileLabels.IndexOf(evt.newValue));
                ChangePreviewPreference(() => session.PreviewTiles = PreviewTileCounts[index]);
                RebuildPreviewTiles();
                ApplyPreviewLayout();
            });
            toolbar.Add(tiles);

            int zoomIndex = Mathf.Max(0, PreviewZoomValues.IndexOf(session.PreviewZoom));
            var zoom = new PopupField<string>(PreviewZoomLabels, zoomIndex) { tooltip = "Preview zoom" };
            zoom.RegisterValueChangedCallback(evt =>
            {
                int index = Mathf.Max(0, PreviewZoomLabels.IndexOf(evt.newValue));
                ChangePreviewPreference(() => session.PreviewZoom = PreviewZoomValues[index]);
                ApplyPreviewLayout();
            });
            toolbar.Add(zoom);

            var channel = new EnumField(session.PreviewChannel) { tooltip = "Preview channel" };
            channel.RegisterValueChangedCallback(evt =>
            {
                ChangePreviewPreference(() => session.PreviewChannel = (PreviewChannel)evt.newValue);
                UpdatePreviewDisplay();
            });
            toolbar.Add(channel);

            int qualityIndex = Mathf.Max(0, PreviewQualities.IndexOf(session.PreviewMaxDimension));
            var quality = new PopupField<int>(PreviewQualities, qualityIndex) { tooltip = "Preview maximum dimension" };
            quality.RegisterValueChangedCallback(evt =>
            {
                ChangePreviewPreference(() => session.PreviewMaxDimension = evt.newValue);
                ProcessPreview();
            });
            toolbar.Add(quality);
            pane.Add(toolbar);

            previewScroll = new ScrollView { mode = ScrollViewMode.VerticalAndHorizontal };
            previewScroll.AddToClassList("preview-scroll");
            previewScroll.contentViewport.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (session.PreviewZoom == 0)
                    ApplyPreviewLayout();
            });

            previewGrid = new VisualElement();
            previewGrid.AddToClassList("preview-grid");
            previewScroll.Add(previewGrid);
            pane.Add(previewScroll);
            RebuildPreviewTiles();

            previewMessage = new Label("Drop a Texture2D here\nor choose one above");
            previewMessage.AddToClassList("preview-message");
            pane.Add(previewMessage);
            return pane;
        }

        private void RebuildPreviewTiles()
        {
            previewImages.Clear();
            previewGrid.Clear();
            int imageCount = session.PreviewTiles * session.PreviewTiles;
            Debug.Log($"RebuildPreviewTiles {session.PreviewTiles}");
            for (int i = 0; i < imageCount; i++)
            {
                var image = new Image
                {
                    image = previewTexture,
                    scaleMode = ScaleMode.StretchToFill
                };
                image.AddToClassList("preview-image");
                image.RegisterCallback<PointerDownEvent>(evt => BeginBrushStroke(image, evt));
                image.RegisterCallback<PointerMoveEvent>(evt => ContinueBrushStroke(image, evt));
                image.RegisterCallback<PointerUpEvent>(evt => EndBrushStroke(image, evt));
                image.RegisterCallback<PointerCaptureOutEvent>(_ => EndBrushStroke());
                image.RegisterCallback<PointerLeaveEvent>(_ => HideBrushCursor());
                previewImages.Add(image);
                previewGrid.Add(image);
            }

            brushCursor = new VisualElement { pickingMode = PickingMode.Ignore };
            brushCursor.AddToClassList("brush-cursor");
            brushHardnessCursor = new VisualElement { pickingMode = PickingMode.Ignore };
            brushHardnessCursor.AddToClassList("brush-cursor-hardness");
            brushCursor.Add(brushHardnessCursor);
            previewGrid.Add(brushCursor);
        }

        private void SetActiveBrush(ExposureBrushEffectData brush)
        {
            EndBrushStroke();
            activeBrush = brush;
            if (brush == null)
                HideBrushCursor();
            effectList?.Rebuild();
            if (brush != null)
                ShowNotification(new GUIContent("Paint on the processed preview. Original view is read-only."));
        }

        private void BeginBrushStroke(Image image, PointerDownEvent evt)
        {
            if (evt.button != 0 || !CanPaint())
                return;

            Undo.RecordObject(session, "Paint Texture Lab Exposure");
            BrushStroke stroke = activeBrush.CreateStroke();
            activeBrush.Strokes.Add(stroke);
            activeBrushImage = image;
            activeBrushPointerId = evt.pointerId;
            UpdateBrushCursor(image, evt.position);
            AddBrushPoint(stroke, GetBrushUv(image, evt.position), true);
            EditorUtility.SetDirty(session);
            image.CapturePointer(evt.pointerId);
            evt.StopPropagation();
            ProcessPreview();
        }

        private void ContinueBrushStroke(Image image, PointerMoveEvent evt)
        {
            if (CanPaint())
                UpdateBrushCursor(image, evt.position);
            if (image != activeBrushImage || evt.pointerId != activeBrushPointerId || activeBrush == null)
                return;

            List<BrushStroke> strokes = activeBrush.Strokes;
            if (strokes.Count == 0)
                return;

            AddBrushPoint(strokes[^1], GetBrushUv(image, evt.position), false);
            ProcessPreview();
            evt.StopPropagation();
        }

        private void EndBrushStroke(Image image, PointerUpEvent evt)
        {
            if (image != activeBrushImage || evt.pointerId != activeBrushPointerId)
                return;

            if (activeBrush != null && activeBrush.Strokes.Count > 0)
            {
                UpdateBrushCursor(image, evt.position);
                AddBrushPoint(activeBrush.Strokes[^1], GetBrushUv(image, evt.position), true);
            }
            EndBrushStroke();
            evt.StopPropagation();
        }

        private void EndBrushStroke()
        {
            if (activeBrushPointerId < 0)
                return;

            if (activeBrushImage != null && activeBrushImage.HasPointerCapture(activeBrushPointerId))
                activeBrushImage.ReleasePointer(activeBrushPointerId);
            activeBrushImage = null;
            activeBrushPointerId = -1;
            EditorUtility.SetDirty(session);
            session.Persist();
            ProcessPreview();
        }

        private bool CanPaint() => activeBrush != null
            && activeBrush.Enabled
            && session.SourceTexture != null
            && !showOriginal
            && session.Effects.Contains(activeBrush);

        private void UpdateBrushCursor(Image image, Vector2 pointerPosition)
        {
            if (brushCursor == null || !CanPaint())
            {
                HideBrushCursor();
                return;
            }

            brushCursorImage = image;
            brushCursorPosition = pointerPosition;
            Rect imageBounds = image.worldBound;
            Rect gridBounds = previewGrid.worldBound;
            float centerX = Mathf.Clamp(pointerPosition.x, imageBounds.xMin, imageBounds.xMax) - gridBounds.xMin;
            float centerY = Mathf.Clamp(pointerPosition.y, imageBounds.yMin, imageBounds.yMax) - gridBounds.yMin;
            float diameterX = activeBrush.BrushSize / Mathf.Max(1f, session.SourceTexture.width) * imageBounds.width;
            float diameterY = activeBrush.BrushSize / Mathf.Max(1f, session.SourceTexture.height) * imageBounds.height;
            brushCursor.style.left = centerX - diameterX * 0.5f;
            brushCursor.style.top = centerY - diameterY * 0.5f;
            brushCursor.style.width = diameterX;
            brushCursor.style.height = diameterY;
            brushCursor.style.display = DisplayStyle.Flex;

            float hardness = activeBrush.BrushHardness;
            brushHardnessCursor.style.width = diameterX * hardness;
            brushHardnessCursor.style.height = diameterY * hardness;
            brushHardnessCursor.style.left = diameterX * (1f - hardness) * 0.5f;
            brushHardnessCursor.style.top = diameterY * (1f - hardness) * 0.5f;
            brushHardnessCursor.style.display = hardness > 0f ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void RefreshBrushCursor()
        {
            if (brushCursorImage != null)
                UpdateBrushCursor(brushCursorImage, brushCursorPosition);
        }

        private void HideBrushCursor()
        {
            if (brushCursor != null)
                brushCursor.style.display = DisplayStyle.None;
            brushCursorImage = null;
        }

        private void AddBrushPoint(BrushStroke stroke, Vector2 point, bool force)
        {
            List<Vector2> points = stroke.Points;
            if (points.Count == 0)
            {
                points.Add(point);
                return;
            }

            Vector2 last = points[^1];
            float distance = BrushDistanceInPixels(last, point);
            float spacing = Mathf.Max(1f, stroke.Size * 0.2f);
            if (distance < spacing)
            {
                if (force && distance > 0.01f)
                    points.Add(point);
                return;
            }

            int count = Mathf.FloorToInt(distance / spacing);
            for (int i = 1; i <= count; i++)
                points.Add(Vector2.Lerp(last, point, i * spacing / distance));
            if (force && BrushDistanceInPixels(points[^1], point) > 0.01f)
                points.Add(point);
        }

        private float BrushDistanceInPixels(Vector2 from, Vector2 to)
        {
            return Vector2.Scale(to - from, new Vector2(session.SourceTexture.width, session.SourceTexture.height)).magnitude;
        }

        private static Vector2 GetBrushUv(Image image, Vector2 pointerPosition)
        {
            Rect bounds = image.worldBound;
            float x = Mathf.Clamp01((pointerPosition.x - bounds.xMin) / Mathf.Max(1f, bounds.width));
            float y = Mathf.Clamp01((pointerPosition.y - bounds.yMin) / Mathf.Max(1f, bounds.height));
            return new Vector2(x, y);
        }

        private void ApplyPreviewLayout()
        {
            if (previewGrid == null || previewScroll == null || processor?.Result == null)
                return;

            int tiles = session.PreviewTiles;
            float cellWidth;
            float cellHeight;
            if (session.PreviewZoom == 0)
            {
                float availableWidth = previewScroll.contentViewport.resolvedStyle.width;
                float availableHeight = previewScroll.contentViewport.resolvedStyle.height;
                if (float.IsNaN(availableWidth) || float.IsNaN(availableHeight) || availableWidth <= 0f || availableHeight <= 0f)
                    return;

                float aspect = processor.Result.width / (float)processor.Result.height;
                float gridWidth = Mathf.Min(availableWidth, availableHeight * aspect);
                float gridHeight = gridWidth / aspect;
                cellWidth = gridWidth / tiles;
                cellHeight = gridHeight / tiles;
            }
            else
            {
                float scale = session.PreviewZoom / 100f;
                cellWidth = processor.Result.width * scale;
                cellHeight = processor.Result.height * scale;
            }

            previewGrid.style.width = cellWidth * tiles;
            previewGrid.style.height = cellHeight * tiles;
            previewGrid.style.alignSelf = session.PreviewZoom == 0 ? Align.Center : Align.FlexStart;
            previewScroll.contentContainer.style.justifyContent = session.PreviewZoom == 0 ? Justify.Center : Justify.FlexStart;
            foreach (Image image in previewImages)
            {
                image.style.width = cellWidth;
                image.style.height = cellHeight;
            }
        }

        private void UpdatePreviewDisplay()
        {
            if (processor?.Result == null || session.SourceTexture == null)
            {
                previewTexture = null;
                foreach (Image image in previewImages)
                    image.image = null;
                return;
            }

            Texture source = showOriginal ? session.SourceTexture : processor.Result;
            previewTexture = processor.RenderDisplay(
                source,
                session.PreviewChannel,
                processor.Result.width,
                processor.Result.height);
            foreach (Image image in previewImages)
            {
                image.image = previewTexture;
                image.MarkDirtyRepaint();
            }

            previewMessage.style.display = DisplayStyle.None;
        }

        private void ToggleOriginal()
        {
            showOriginal = !showOriginal;
            if (originalButton != null)
            {
                if (showOriginal)
                    originalButton.AddToClassList("selected");
                else
                    originalButton.RemoveFromClassList("selected");
            }

            UpdatePreviewDisplay();
        }

        private void ChangePreviewPreference(Action change)
        {
            change();
            EditorUtility.SetDirty(session);
            session.Persist();
        }

        private VisualElement BuildStack()
        {
            var pane = new VisualElement();
            pane.AddToClassList("stack-pane");

            var title = new Label("EFFECT STACK");
            title.AddToClassList("stack-title");
            pane.Add(title);

            effectList = new ListView
            {
                itemsSource = session.Effects,
                makeItem = () => new VisualElement(),
                bindItem = BindEffect,
                reorderable = true,
                reorderMode = ListViewReorderMode.Animated,
                selectionType = SelectionType.None,
                virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight,
                showBorder = false
            };
            effectList.AddToClassList("effect-list");
            effectList.canStartDrag += _ =>
            {
                Undo.RecordObject(session, "Reorder Texture Lab Effect");
                return true;
            };
            effectList.itemIndexChanged += (_, _) => CommitListChange();
            pane.Add(effectList);

            var addButton = new Button(ShowAddMenu) { text = "+ Add Effect" };
            addButton.AddToClassList("add-effect-button");
            pane.Add(addButton);
            return pane;
        }

        private void BindEffect(VisualElement container, int index)
        {
            container.Clear();
            if (index >= 0 && index < session.Effects.Count)
                container.Add(BuildEffectCard(session.Effects[index]));
        }

        private VisualElement BuildEffectCard(TextureEffectData effect)
        {
            var card = new VisualElement();
            card.AddToClassList("effect-card");

            var header = new VisualElement();
            header.AddToClassList("effect-header");

            var expand = new Button(() => ToggleExpanded(effect)) { text = effect.Expanded ? "▾" : "▸" };
            expand.AddToClassList("icon-button");
            header.Add(expand);

            var enabled = new Toggle { value = effect.Enabled };
            enabled.AddToClassList("enabled-toggle");
            enabled.RegisterValueChangedCallback(evt => EditEffect(effect, "Toggle Texture Lab Effect", () => effect.Enabled = evt.newValue));
            header.Add(enabled);

            var randomize = new Button(() =>
            {
                EditEffect(effect, "Toggle Texture Lab Effect Randomization", () => effect.AllowRandomize = !effect.AllowRandomize);
                effectList.Rebuild();
            }) { text = "R", tooltip = "Allow Randomize" };
            randomize.AddToClassList("icon-button");
            if (effect.AllowRandomize)
                randomize.AddToClassList("selected");
            header.Add(randomize);

            var title = new Label(effect.DisplayName);
            title.AddToClassList("effect-title");
            header.Add(title);

            var duplicate = new Button(() => DuplicateEffect(effect)) { text = "D", tooltip = "Duplicate" };
            duplicate.AddToClassList("icon-button");
            header.Add(duplicate);

            var remove = new Button(() => RemoveEffect(effect)) { text = "×", tooltip = "Remove" };
            remove.AddToClassList("icon-button");
            header.Add(remove);
            card.Add(header);

            if (effect.Expanded)
            {
                var parameters = new VisualElement();
                parameters.AddToClassList("effect-parameters");
                AddEffectFields(parameters, effect);
                card.Add(parameters);
            }

            return card;
        }

        private void AddEffectFields(VisualElement root, TextureEffectData effect)
        {
            switch (effect)
            {
                case PixelateEffectData pixelate:
                    AddPixelateFields(root, pixelate);
                    break;
                case PosterizeEffectData posterize:
                    root.Add(IntSlider("Red Bits", posterize.RedBits, 1, 8, value => posterize.RedBits = value, effect));
                    root.Add(IntSlider("Green Bits", posterize.GreenBits, 1, 8, value => posterize.GreenBits = value, effect));
                    root.Add(IntSlider("Blue Bits", posterize.BlueBits, 1, 8, value => posterize.BlueBits = value, effect));
                    break;
                case LevelsEffectData levels:
                    root.Add(FloatSlider("Black Point", levels.BlackPoint, 0f, 1f, value => levels.BlackPoint = Mathf.Min(value, levels.WhitePoint - 0.001f), effect));
                    root.Add(FloatSlider("White Point", levels.WhitePoint, 0f, 1f, value => levels.WhitePoint = Mathf.Max(value, levels.BlackPoint + 0.001f), effect));
                    root.Add(FloatSlider("Gamma", levels.Gamma, 0.1f, 4f, value => levels.Gamma = value, effect));
                    root.Add(FloatSlider("Output Black", levels.OutputBlack, 0f, 1f, value => levels.OutputBlack = Mathf.Min(value, levels.OutputWhite), effect));
                    root.Add(FloatSlider("Output White", levels.OutputWhite, 0f, 1f, value => levels.OutputWhite = Mathf.Max(value, levels.OutputBlack), effect));
                    break;
                case ColorAdjustmentsEffectData adjustments:
                    root.Add(FloatSlider("Brightness", adjustments.Brightness, -1f, 1f, value => adjustments.Brightness = value, effect));
                    root.Add(FloatSlider("Contrast", adjustments.Contrast, -1f, 1f, value => adjustments.Contrast = value, effect));
                    root.Add(FloatSlider("Gamma", adjustments.Gamma, 0.1f, 4f, value => adjustments.Gamma = value, effect));
                    break;
                case ColorReplaceEffectData replace:
                    AddColorReplaceFields(root, replace);
                    break;
                case PaletteQuantizeEffectData paletteQuantize:
                    AddPaletteFields(root, paletteQuantize);
                    break;
                case DitherEffectData dither:
                    AddDitherFields(root, dither);
                    break;
                case NoiseEffectData noise:
                    AddNoiseFields(root, noise);
                    break;
                case GaussianBlurEffectData blur:
                    root.Add(FloatSlider("Radius", blur.Radius, 0f, 32f, value => blur.Radius = value, effect));
                    root.Add(IntSlider("Iterations", blur.Iterations, 1, 4, value => blur.Iterations = value, effect));
                    break;
                case OffsetEffectData offset:
                    AddOffsetFields(root, offset);
                    break;
                case SeamBlendEffectData seamBlend:
                    AddSeamBlendFields(root, seamBlend);
                    break;
                case ChannelRemapEffectData remap:
                    AddChannelMixerFields(root, remap);
                    break;
                case ExposureBrushEffectData brush:
                    AddExposureBrushFields(root, brush);
                    break;
            }
        }

        private void AddExposureBrushFields(VisualElement root, ExposureBrushEffectData effect)
        {
            var mode = new EnumField("Mode", effect.BrushMode);
            mode.RegisterValueChangedCallback(evt =>
                EditEffect(effect, "Change Brush Mode", () => effect.BrushMode = (ExposureBrushMode)evt.newValue));
            root.Add(mode);
            root.Add(FloatSlider("Size", effect.BrushSize, 1f, 512f, value => effect.BrushSize = value, effect));
            root.Add(FloatSlider("Hardness", effect.BrushHardness, 0f, 1f, value => effect.BrushHardness = value, effect));
            root.Add(FloatSlider("Exposure", effect.BrushExposure, 0.01f, 2f, value => effect.BrushExposure = value, effect));

            var wrap = new EnumField("Edges", effect.Wrap);
            wrap.RegisterValueChangedCallback(evt =>
                EditEffect(effect, "Change Brush Edge Mode", () => effect.Wrap = (OffsetWrapMode)evt.newValue));
            root.Add(wrap);

            var paint = new Button(() => SetActiveBrush(activeBrush == effect ? null : effect)) { text = "Paint" };
            if (activeBrush == effect)
                paint.AddToClassList("selected");
            root.Add(paint);

            var clear = new Button(() =>
            {
                if (effect.Strokes.Count > 0)
                    EditEffect(effect, "Clear Texture Lab Brush Strokes", effect.Strokes.Clear);
            }) { text = "Clear Strokes" };
            clear.SetEnabled(effect.Strokes.Count > 0);
            root.Add(clear);
        }

        private void AddOffsetFields(VisualElement root, OffsetEffectData effect)
        {
            root.Add(FloatSlider("Offset X", effect.OffsetX, 0f, 1f, value => effect.OffsetX = value, effect));
            root.Add(FloatSlider("Offset Y", effect.OffsetY, 0f, 1f, value => effect.OffsetY = value, effect));

            var wrap = new EnumField("Wrap", effect.Wrap);
            wrap.RegisterValueChangedCallback(evt =>
                EditEffect(effect, "Change Offset Wrap", () => effect.Wrap = (OffsetWrapMode)evt.newValue));
            root.Add(wrap);

            root.Add(new Button(() =>
            {
                EditEffect(effect, "Center Offset Seams", () =>
                {
                    effect.OffsetX = 0.5f;
                    effect.OffsetY = 0.5f;
                });
                effectList.Rebuild();
            }) { text = "Center Seams" });
        }

        private void AddSeamBlendFields(VisualElement root, SeamBlendEffectData effect)
        {
            root.Add(FloatSlider("Blend Width", effect.BlendWidth, 0f, 0.5f, value => effect.BlendWidth = value, effect));
            root.Add(FloatSlider("Blend Strength", effect.BlendStrength, 0f, 1f, value => effect.BlendStrength = value, effect));

            var horizontal = new Toggle("Horizontal (left/right)") { value = effect.Horizontal };
            horizontal.RegisterValueChangedCallback(evt =>
                EditEffect(effect, "Toggle Horizontal Seam Blend", () => effect.Horizontal = evt.newValue));
            root.Add(horizontal);

            var vertical = new Toggle("Vertical (top/bottom)") { value = effect.Vertical };
            vertical.RegisterValueChangedCallback(evt =>
                EditEffect(effect, "Toggle Vertical Seam Blend", () => effect.Vertical = evt.newValue));
            root.Add(vertical);

            var blendAlpha = new Toggle("Blend Alpha") { value = effect.BlendAlpha };
            blendAlpha.RegisterValueChangedCallback(evt =>
                EditEffect(effect, "Toggle Seam Blend Alpha", () => effect.BlendAlpha = evt.newValue));
            root.Add(blendAlpha);
        }

        private void AddPixelateFields(VisualElement root, PixelateEffectData effect)
        {
            var mode = new EnumField("Mode", effect.Mode);
            mode.RegisterValueChangedCallback(evt =>
            {
                EditEffect(effect, "Change Pixelate Mode", () => effect.Mode = (PixelateMode)evt.newValue);
                effectList.Rebuild();
            });
            root.Add(mode);

            int selectedBlockSize = Mathf.Max(0, BlockSizes.IndexOf(effect.BlockSize));
            var blockSize = new PopupField<int>("Block Size", BlockSizes, selectedBlockSize);
            blockSize.SetEnabled(effect.Mode == PixelateMode.BlockSize);
            blockSize.RegisterValueChangedCallback(evt => EditEffect(effect, "Change Pixelate Block Size", () => effect.BlockSize = evt.newValue));
            root.Add(blockSize);

            var targetResolution = new IntegerField("Target Resolution") { value = effect.TargetResolution };
            targetResolution.SetEnabled(effect.Mode == PixelateMode.TargetResolution);
            targetResolution.RegisterValueChangedCallback(evt => EditEffect(effect, "Change Pixelate Resolution", () => effect.TargetResolution = Mathf.Clamp(evt.newValue, 16, 2048)));
            root.Add(targetResolution);

            var sampling = new EnumField("Sampling", effect.Sampling);
            sampling.RegisterValueChangedCallback(evt => EditEffect(effect, "Change Pixelate Sampling", () => effect.Sampling = (PixelSampling)evt.newValue));
            root.Add(sampling);
        }

        private void AddPaletteFields(VisualElement root, PaletteQuantizeEffectData effect)
        {
            root.Add(BuildPalettePicker(effect, out VisualElement palettePreview));

            var colorLimit = new IntegerField("Color Limit (0 = All)") { value = effect.ColorLimit };
            colorLimit.RegisterValueChangedCallback(evt =>
                EditEffect(effect, "Change Palette Color Limit", () => effect.ColorLimit = evt.newValue));
            root.Add(colorLimit);

            int selectedSize = Mathf.Max(0, PaletteSizes.IndexOf(effect.ExtractionColorCount));
            var extractionSize = new PopupField<int>("Extract Colors", PaletteSizes, selectedSize);
            extractionSize.RegisterValueChangedCallback(evt =>
                ChangeSession("Change Palette Extraction Size", () => effect.ExtractionColorCount = evt.newValue, false));
            root.Add(extractionSize);

            var actions = new VisualElement();
            actions.AddToClassList("palette-actions");
            actions.Add(new Button(() => CreateAndAssignPalette(effect)) { text = "New Palette" });
            actions.Add(new Button(() => ExtractPalette(effect)) { text = "Extract from Source" });
            root.Add(actions);

            TextureLabPalette palette = effect.Palette;
            if (palette == null)
            {
                var hint = new Label("Assign or create a palette to edit its colors.");
                hint.AddToClassList("palette-hint");
                root.Add(hint);
                return;
            }

            var colorCount = new Label($"{palette.Colors.Count} / 64 colors");
            colorCount.AddToClassList("palette-count");
            root.Add(colorCount);

            for (int i = 0; i < palette.Colors.Count; i++)
                root.Add(BuildPaletteColorRow(effect, palette, i, palettePreview));

            var addColor = new Button(() => AddPaletteColor(effect, palette)) { text = "+ Color" };
            addColor.SetEnabled(palette.Colors.Count < 64);
            root.Add(addColor);
        }

        private VisualElement BuildPalettePicker(PaletteQuantizeEffectData effect, out VisualElement preview)
        {
            var row = new VisualElement();
            row.AddToClassList("palette-picker");

            var fieldLabel = new Label("Palette");
            fieldLabel.AddToClassList("palette-picker-label");
            row.Add(fieldLabel);

            var button = new Button();
            button.AddToClassList("palette-picker-button");

            preview = new VisualElement();
            preview.AddToClassList("palette-picker-preview");
            RefreshPalettePreview(preview, effect.Palette);
            button.Add(preview);

            var arrow = new Label("▾");
            arrow.AddToClassList("palette-picker-arrow");
            button.Add(arrow);

            button.clicked += () => ShowPalettePicker(button, effect);
            row.Add(button);
            return row;
        }

        private void ShowPalettePicker(VisualElement activator, PaletteQuantizeEffectData effect)
        {
            List<TextureLabPalette> palettes = LoadAvailablePalettes();

            palettes.Sort((left, right) => string.Compare(left.name, right.name, StringComparison.OrdinalIgnoreCase));

            Rect activatorRect = activator.worldBound;
            activatorRect.position += position.position;
            UnityEditor.PopupWindow.Show(activatorRect, new PalettePickerPopup(palettes, effect.Palette, palette =>
            {
                if (effect.Palette != null && string.Equals(effect.Palette.Id, palette?.Id, StringComparison.Ordinal))
                    return;

                EditEffect(effect, "Change Texture Lab Palette", () => effect.Palette = palette);
                session.ResolvePaletteReferences(LoadAvailablePalettes());
                ProcessPreview();
                effectList.Rebuild();
            }));
        }

        private static List<TextureLabPalette> LoadAvailablePalettes()
        {
            var palettes = new List<TextureLabPalette>();

            AddPalettesFromSearch(palettes, "Assets");

            // The explicit catalog remains the source of truth for built-in palettes.
            // Keeping this independent from the asset path allows Texture Lab to live
            // either under Assets or in a package without changing the picker.
            TextureLabPaletteCatalog catalog = TextureLabPaletteCatalog.LoadBuiltIn();
            if (catalog != null)
            {
                foreach (TextureLabPalette palette in catalog.Palettes)
                    AddPalette(palettes, palette);
            }

            return palettes;
        }

        private static void AddPalettesFromSearch(List<TextureLabPalette> palettes, string folder)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:TextureLabPalette", new[] { folder }))
                AddPalette(palettes, AssetDatabase.LoadAssetAtPath<TextureLabPalette>(AssetDatabase.GUIDToAssetPath(guid)));
        }

        private static void AddPalette(List<TextureLabPalette> palettes, TextureLabPalette palette)
        {
            if (palette != null && !palettes.Contains(palette))
                palettes.Add(palette);
        }

        private static void RefreshPalettePreview(VisualElement preview, TextureLabPalette palette)
        {
            preview.Clear();
            if (palette == null)
            {
                preview.AddToClassList("empty");
                return;
            }

            preview.RemoveFromClassList("empty");
            foreach (Color color in palette.Colors)
            {
                var swatch = new VisualElement();
                swatch.AddToClassList("palette-picker-swatch");
                swatch.style.backgroundColor = color;
                preview.Add(swatch);
            }
        }

        private void AddColorReplaceFields(VisualElement root, ColorReplaceEffectData effect)
        {
            var source = new ColorField("Source Color")
            {
                value = effect.SourceColor,
                showAlpha = false,
                hdr = false
            };
            source.RegisterValueChangedCallback(evt =>
                EditEffect(effect, "Change Replace Source Color", () => effect.SourceColor = evt.newValue));
            root.Add(source);

            var replacement = new ColorField("Replacement Color")
            {
                value = effect.ReplacementColor,
                showAlpha = false,
                hdr = false
            };
            replacement.RegisterValueChangedCallback(evt =>
                EditEffect(effect, "Change Replacement Color", () => effect.ReplacementColor = evt.newValue));
            root.Add(replacement);

            root.Add(FloatSlider("Tolerance", effect.Tolerance, 0f, Mathf.Sqrt(3f), value => effect.Tolerance = value, effect));
            root.Add(FloatSlider("Softness", effect.Softness, 0f, Mathf.Sqrt(3f), value => effect.Softness = value, effect));

            var previewMask = new Toggle("Preview Mask") { value = effect.PreviewMask };
            previewMask.RegisterValueChangedCallback(evt =>
                EditEffect(effect, "Toggle Color Replace Mask", () => effect.PreviewMask = evt.newValue));
            root.Add(previewMask);
        }

        private void AddDitherFields(VisualElement root, DitherEffectData effect)
        {
            var pattern = new EnumField("Pattern", effect.Pattern);
            pattern.RegisterValueChangedCallback(evt =>
                EditEffect(effect, "Change Dither Pattern", () => effect.Pattern = (DitherPattern)evt.newValue));
            root.Add(pattern);

            root.Add(FloatSlider("Strength", effect.Strength, 0f, 1f, value => effect.Strength = value, effect));
            root.Add(IntSlider("Scale", effect.Scale, 1, 8, value => effect.Scale = value, effect));

            var seed = new IntegerField("Offset / Seed") { value = effect.Seed };
            seed.RegisterValueChangedCallback(evt =>
                EditEffect(effect, "Change Dither Seed", () => effect.Seed = evt.newValue));
            root.Add(seed);

            var channels = new EnumField("Channels", effect.Channels);
            channels.RegisterValueChangedCallback(evt =>
                EditEffect(effect, "Change Dither Channels", () => effect.Channels = (DitherChannels)evt.newValue));
            root.Add(channels);
        }

        private void AddNoiseFields(VisualElement root, NoiseEffectData effect)
        {
            var type = new EnumField("Type", effect.Type);
            type.RegisterValueChangedCallback(evt =>
                EditEffect(effect, "Change Noise Type", () => effect.Type = (NoiseType)evt.newValue));
            root.Add(type);

            root.Add(FloatSlider("Amount", effect.Amount, 0f, 1f, value => effect.Amount = value, effect));
            root.Add(IntSlider("Scale", effect.Scale, 1, 64, value => effect.Scale = value, effect));

            var seed = new IntegerField("Seed") { value = effect.Seed };
            seed.RegisterValueChangedCallback(evt =>
                EditEffect(effect, "Change Noise Seed", () => effect.Seed = evt.newValue));
            root.Add(seed);

            var channels = new EnumField("Channels", effect.Channels);
            channels.RegisterValueChangedCallback(evt =>
                EditEffect(effect, "Change Noise Channels", () => effect.Channels = (DitherChannels)evt.newValue));
            root.Add(channels);
        }

        private void AddChannelMixerFields(VisualElement root, ChannelRemapEffectData effect)
        {
            int outputIndex = Mathf.Clamp(selectedMixerOutputs.GetValueOrDefault(effect.Id), 0, 2);
            var selector = new VisualElement();
            selector.AddToClassList("mixer-selector");
            for (int i = 0; i < MixerChannelNames.Length; i++)
            {
                int index = i;
                var button = new Button(() =>
                {
                    selectedMixerOutputs[effect.Id] = index;
                    effectList.Rebuild();
                }) { text = MixerChannelNames[i] };
                button.AddToClassList("mixer-selector-button");
                if (i == outputIndex)
                    button.AddToClassList("selected");
                selector.Add(button);
            }

            var outputRow = new VisualElement();
            outputRow.AddToClassList("mixer-output-row");
            outputRow.Add(new Label("Output"));
            outputRow.Add(selector);
            outputRow.style.display = effect.Monochrome ? DisplayStyle.None : DisplayStyle.Flex;
            root.Add(outputRow);

            Label total = new();
            total.AddToClassList("mixer-total");
            Action updateTotal = null;

            if (effect.Monochrome)
            {
                root.Add(ChannelPercentSlider("Red Source", effect.MonochromeMix.x, -2f, 2f,
                    value => effect.MonochromeMix = SetComponent(effect.MonochromeMix, 0, value), effect, "channel-red", UpdateTotal));
                root.Add(ChannelPercentSlider("Green Source", effect.MonochromeMix.y, -2f, 2f,
                    value => effect.MonochromeMix = SetComponent(effect.MonochromeMix, 1, value), effect, "channel-green", UpdateTotal));
                root.Add(ChannelPercentSlider("Blue Source", effect.MonochromeMix.z, -2f, 2f,
                    value => effect.MonochromeMix = SetComponent(effect.MonochromeMix, 2, value), effect, "channel-blue", UpdateTotal));
                root.Add(ChannelPercentSlider("Constant", effect.MonochromeConstant, -1f, 1f,
                    value => effect.MonochromeConstant = value, effect, null, UpdateTotal));
                updateTotal = () => total.text = $"Total  {effect.MonochromeMix.x + effect.MonochromeMix.y + effect.MonochromeMix.z:P0}";
            }
            else
            {
                Vector3 current = effect.GetOutput(outputIndex);
                root.Add(ChannelPercentSlider("Red Source", current.x, -2f, 2f,
                    value => SetOutputComponent(effect, outputIndex, 0, value), effect, "channel-red", UpdateTotal));
                root.Add(ChannelPercentSlider("Green Source", current.y, -2f, 2f,
                    value => SetOutputComponent(effect, outputIndex, 1, value), effect, "channel-green", UpdateTotal));
                root.Add(ChannelPercentSlider("Blue Source", current.z, -2f, 2f,
                    value => SetOutputComponent(effect, outputIndex, 2, value), effect, "channel-blue", UpdateTotal));
                root.Add(ChannelPercentSlider("Constant", effect.Constants[outputIndex], -1f, 1f,
                    value => effect.Constants = SetComponent(effect.Constants, outputIndex, value), effect, null, UpdateTotal));
                updateTotal = () =>
                {
                    Vector3 value = effect.GetOutput(outputIndex);
                    total.text = $"Total  {value.x + value.y + value.z:P0}";
                };
            }

            void UpdateTotal() => updateTotal?.Invoke();
            UpdateTotal();
            root.Add(total);

            var channelActions = new VisualElement();
            channelActions.AddToClassList("mixer-actions");
            channelActions.Add(new Button(() => NormalizeMixerChannel(effect, outputIndex)) { text = "Normalize" });
            channelActions.Add(new Button(() => ResetMixerChannel(effect, outputIndex))
            {
                text = effect.Monochrome ? "Reset Mix" : "Reset Channel"
            });
            root.Add(channelActions);

            root.Add(ChannelPercentSlider("Strength", effect.Strength, 0f, 1f,
                value => effect.Strength = value, effect));

            var monochrome = new Toggle("Monochrome") { value = effect.Monochrome };
            monochrome.RegisterValueChangedCallback(evt =>
            {
                EditEffect(effect, "Toggle Channel Mixer Monochrome", () => effect.Monochrome = evt.newValue);
                effectList.Rebuild();
            });
            root.Add(monochrome);

            AddMixerRecipes(root, effect);
            AddMixerAlpha(root, effect);

            var resetAll = new Button(() =>
            {
                EditEffect(effect, "Reset Channel Mixer", effect.ResetAll);
                effectList.Rebuild();
            }) { text = "Reset All" };
            resetAll.AddToClassList("mixer-reset-all");
            root.Add(resetAll);
        }

        private void NormalizeMixerChannel(ChannelRemapEffectData effect, int outputIndex)
        {
            Vector3 value = effect.Monochrome ? effect.MonochromeMix : effect.GetOutput(outputIndex);
            float total = value.x + value.y + value.z;
            if (Mathf.Abs(total) < 0.0001f)
                return;

            EditEffect(effect, "Normalize Channel Mixer Output", () =>
            {
                if (effect.Monochrome)
                    effect.MonochromeMix = value / total;
                else
                    effect.SetOutput(outputIndex, value / total);
            });
            effectList.Rebuild();
        }

        private void ResetMixerChannel(ChannelRemapEffectData effect, int outputIndex)
        {
            EditEffect(effect, "Reset Channel Mixer Output", () =>
            {
                if (effect.Monochrome)
                {
                    effect.MonochromeMix = new Vector3(0.2126f, 0.7152f, 0.0722f);
                    effect.MonochromeConstant = 0f;
                    return;
                }

                effect.SetOutput(outputIndex, outputIndex switch
                {
                    0 => Vector3.right,
                    1 => Vector3.up,
                    _ => Vector3.forward
                });
                effect.Constants = SetComponent(effect.Constants, outputIndex, 0f);
            });
            effectList.Rebuild();
        }

        private void AddMixerRecipes(VisualElement root, ChannelRemapEffectData effect)
        {
            var recipes = new Foldout { text = "Recipes", value = false };
            recipes.AddToClassList("mixer-foldout");
            AddMixerRecipe(recipes, effect, "Identity", () => SetMixerRecipe(
                effect, Vector3.right, Vector3.up, Vector3.forward));
            AddMixerRecipe(recipes, effect, "Swap R/G", () => SetMixerRecipe(
                effect, Vector3.up, Vector3.right, Vector3.forward));
            AddMixerRecipe(recipes, effect, "Swap R/B", () => SetMixerRecipe(
                effect, Vector3.forward, Vector3.up, Vector3.right));
            AddMixerRecipe(recipes, effect, "Swap G/B", () => SetMixerRecipe(
                effect, Vector3.right, Vector3.forward, Vector3.up));
            AddMixerRecipe(recipes, effect, "Warm", () => SetMixerRecipe(
                effect,
                new Vector3(1f, 0.05f, 0.1f),
                Vector3.up,
                new Vector3(0f, 0.05f, 0.9f)));
            AddMixerRecipe(recipes, effect, "Cool", () => SetMixerRecipe(
                effect,
                new Vector3(0.9f, 0.05f, 0f),
                Vector3.up,
                new Vector3(0.1f, 0.05f, 1f)));
            AddMixerRecipe(recipes, effect, "Sepia", () => SetMixerRecipe(
                effect,
                new Vector3(0.393f, 0.769f, 0.189f),
                new Vector3(0.349f, 0.686f, 0.168f),
                new Vector3(0.272f, 0.534f, 0.131f)));
            AddMixerRecipe(recipes, effect, "High Contrast Mono", () => SetMonochromeRecipe(
                effect, new Vector3(0.6f, 0.9f, -0.5f)));
            AddMixerRecipe(recipes, effect, "Luminance Mono", () => SetMonochromeRecipe(
                effect, new Vector3(0.2126f, 0.7152f, 0.0722f)));
            root.Add(recipes);
        }

        private void AddMixerRecipe(VisualElement root, ChannelRemapEffectData effect, string name, Action apply)
        {
            var button = new Button(() =>
            {
                EditEffect(effect, $"Apply {name} Channel Mixer Recipe", apply);
                effectList.Rebuild();
            }) { text = name };
            button.AddToClassList("mixer-recipe-button");
            root.Add(button);
        }

        private void AddMixerAlpha(VisualElement root, ChannelRemapEffectData effect)
        {
            var advanced = new Foldout { text = "Advanced", value = false };
            advanced.AddToClassList("mixer-foldout");

            var alphaMode = new EnumField("Alpha Mode", effect.AlphaMode);
            var alphaFields = new VisualElement();
            alphaFields.AddToClassList("mixer-alpha-fields");
            alphaFields.style.display = effect.AlphaMode == ChannelMixerAlphaMode.Mix
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            alphaMode.RegisterValueChangedCallback(evt =>
            {
                EditEffect(effect, "Change Channel Mixer Alpha Mode", () => effect.AlphaMode = (ChannelMixerAlphaMode)evt.newValue);
                alphaFields.style.display = effect.AlphaMode == ChannelMixerAlphaMode.Mix
                    ? DisplayStyle.Flex
                    : DisplayStyle.None;
            });
            advanced.Add(alphaMode);

            Label total = new();
            total.AddToClassList("mixer-total");
            void UpdateTotal() => total.text = $"Total  {effect.AlphaOutput.x + effect.AlphaOutput.y + effect.AlphaOutput.z + effect.AlphaOutput.w:P0}";

            alphaFields.Add(ChannelPercentSlider("Red Source", effect.AlphaOutput.x, -2f, 2f,
                value => effect.AlphaOutput = SetComponent(effect.AlphaOutput, 0, value), effect, "channel-red", UpdateTotal));
            alphaFields.Add(ChannelPercentSlider("Green Source", effect.AlphaOutput.y, -2f, 2f,
                value => effect.AlphaOutput = SetComponent(effect.AlphaOutput, 1, value), effect, "channel-green", UpdateTotal));
            alphaFields.Add(ChannelPercentSlider("Blue Source", effect.AlphaOutput.z, -2f, 2f,
                value => effect.AlphaOutput = SetComponent(effect.AlphaOutput, 2, value), effect, "channel-blue", UpdateTotal));
            alphaFields.Add(ChannelPercentSlider("Alpha Source", effect.AlphaOutput.w, -2f, 2f,
                value => effect.AlphaOutput = SetComponent(effect.AlphaOutput, 3, value), effect, "channel-alpha", UpdateTotal));
            alphaFields.Add(ChannelPercentSlider("Constant", effect.AlphaConstant, -1f, 1f,
                value => effect.AlphaConstant = value, effect, null, UpdateTotal));
            UpdateTotal();
            alphaFields.Add(total);

            var actions = new VisualElement();
            actions.AddToClassList("mixer-actions");
            actions.Add(new Button(() =>
            {
                float sum = effect.AlphaOutput.x + effect.AlphaOutput.y + effect.AlphaOutput.z + effect.AlphaOutput.w;
                if (Mathf.Abs(sum) < 0.0001f)
                    return;
                EditEffect(effect, "Normalize Channel Mixer Alpha", () => effect.AlphaOutput /= sum);
                effectList.Rebuild();
            }) { text = "Normalize Alpha" });
            actions.Add(new Button(() =>
            {
                EditEffect(effect, "Reset Channel Mixer Alpha", () =>
                {
                    effect.AlphaOutput = new Vector4(0f, 0f, 0f, 1f);
                    effect.AlphaConstant = 0f;
                });
                effectList.Rebuild();
            }) { text = "Reset Alpha" });
            alphaFields.Add(actions);
            advanced.Add(alphaFields);
            root.Add(advanced);
        }

        private Slider ChannelPercentSlider(
            string label,
            float value,
            float min,
            float max,
            Action<float> setter,
            ChannelRemapEffectData effect,
            string className = null,
            Action onChanged = null)
        {
            var slider = new Slider(label, min * 100f, max * 100f) { value = value * 100f, showInputField = true };
            if (!string.IsNullOrEmpty(className))
                slider.AddToClassList(className);
            slider.RegisterValueChangedCallback(evt =>
            {
                EditEffect(effect, $"Change Channel Mixer {label}", () => setter(evt.newValue / 100f));
                onChanged?.Invoke();
            });
            return slider;
        }

        private static void SetOutputComponent(ChannelRemapEffectData effect, int output, int input, float value)
        {
            effect.SetOutput(output, SetComponent(effect.GetOutput(output), input, value));
        }

        private static Vector3 SetComponent(Vector3 vector, int index, float value)
        {
            vector[index] = value;
            return vector;
        }

        private static Vector4 SetComponent(Vector4 vector, int index, float value)
        {
            vector[index] = value;
            return vector;
        }

        private static void SetMixerRecipe(
            ChannelRemapEffectData effect,
            Vector3 red,
            Vector3 green,
            Vector3 blue)
        {
            effect.RedOutput = red;
            effect.GreenOutput = green;
            effect.BlueOutput = blue;
            effect.Constants = Vector3.zero;
            effect.Strength = 1f;
            effect.Monochrome = false;
        }

        private static void SetMonochromeRecipe(ChannelRemapEffectData effect, Vector3 mix)
        {
            effect.MonochromeMix = mix;
            effect.MonochromeConstant = 0f;
            effect.Strength = 1f;
            effect.Monochrome = true;
        }

        private VisualElement BuildPaletteColorRow(
            PaletteQuantizeEffectData effect,
            TextureLabPalette palette,
            int index,
            VisualElement palettePreview)
        {
            var row = new VisualElement();
            row.AddToClassList("palette-color-row");

            var color = new ColorField((index + 1).ToString("00"))
            {
                value = palette.Colors[index],
                showAlpha = false,
                hdr = false
            };
            color.AddToClassList("palette-color-field");
            color.RegisterValueChangedCallback(evt =>
            {
                TextureLabPalette edited = EditPalette(
                    effect,
                    palette,
                    "Change Palette Color",
                    target => target.Colors[index] = evt.newValue);
                RefreshPalettePreview(palettePreview, edited ?? effect.Palette);
                effectList.Rebuild();
            });
            row.Add(color);

            var up = new Button(() => MovePaletteColor(effect, palette, index, index - 1)) { text = "↑", tooltip = "Move up" };
            up.AddToClassList("palette-icon-button");
            up.SetEnabled(index > 0);
            row.Add(up);

            var down = new Button(() => MovePaletteColor(effect, palette, index, index + 1)) { text = "↓", tooltip = "Move down" };
            down.AddToClassList("palette-icon-button");
            down.SetEnabled(index < palette.Colors.Count - 1);
            row.Add(down);

            var remove = new Button(() => RemovePaletteColor(effect, palette, index)) { text = "×", tooltip = "Remove" };
            remove.AddToClassList("palette-icon-button");
            remove.SetEnabled(palette.Colors.Count > 1);
            row.Add(remove);
            return row;
        }

        private sealed class PalettePickerPopup : PopupWindowContent
        {
            private const float RowHeight = 28f;
            private readonly List<TextureLabPalette> palettes;
            private readonly TextureLabPalette selected;
            private readonly Action<TextureLabPalette> select;
            private readonly HashSet<TextureLabPalette> builtInPalettes = new();
            private Vector2 scroll;

            internal PalettePickerPopup(
                List<TextureLabPalette> palettes,
                TextureLabPalette selected,
                Action<TextureLabPalette> select)
            {
                this.palettes = palettes;
                this.selected = selected;
                this.select = select;

                TextureLabPaletteCatalog catalog = TextureLabPaletteCatalog.LoadBuiltIn();
                if (catalog == null)
                    return;

                foreach (TextureLabPalette palette in catalog.Palettes)
                {
                    if (palette != null)
                        builtInPalettes.Add(palette);
                }
            }

            public override Vector2 GetWindowSize()
            {
                float contentHeight = RowHeight * (palettes.Count + 1) + 52f;
                return new Vector2(360f, Mathf.Min(460f, contentHeight));
            }

            public override void OnGUI(Rect rect)
            {
                scroll = EditorGUILayout.BeginScrollView(scroll);
                DrawPaletteRow(null);
                DrawPaletteGroup("Project Palettes", false);
                DrawPaletteGroup("Built-in Palettes", true);
                EditorGUILayout.EndScrollView();
            }

            private void DrawPaletteGroup(string title, bool builtIn)
            {
                bool drewHeader = false;
                foreach (TextureLabPalette palette in palettes)
                {
                    bool isBuiltIn = builtInPalettes.Contains(palette);
                    if (isBuiltIn != builtIn)
                        continue;

                    if (!drewHeader)
                    {
                        GUILayout.Space(5f);
                        GUILayout.Label(title, EditorStyles.miniBoldLabel);
                        drewHeader = true;
                    }

                    DrawPaletteRow(palette);
                }
            }

            private void DrawPaletteRow(TextureLabPalette palette)
            {
                Rect row = GUILayoutUtility.GetRect(1f, RowHeight, GUILayout.ExpandWidth(true));
                bool isSelected = palette == selected
                    || (palette != null && selected != null
                        && string.Equals(palette.Id, selected.Id, StringComparison.Ordinal));
                bool isHovered = row.Contains(Event.current.mousePosition);
                if (Event.current.type == EventType.Repaint && (isSelected || isHovered))
                {
                    Color background = isSelected
                        ? new Color(0.22f, 0.43f, 0.68f, 0.8f)
                        : new Color(1f, 1f, 1f, 0.07f);
                    EditorGUI.DrawRect(row, background);
                }

                Rect checkRect = new Rect(row.x + 6f, row.y, 18f, row.height);
                Rect previewRect = new Rect(checkRect.xMax + 6f, row.y + 6f, row.xMax - checkRect.xMax - 14f, row.height - 12f);
                GUI.Label(checkRect, isSelected ? "✓" : string.Empty);
                DrawPaletteColors(previewRect, palette);

                if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && row.Contains(Event.current.mousePosition))
                {
                    select(palette);
                    editorWindow.Close();
                    Event.current.Use();
                }
            }

            private static void DrawPaletteColors(Rect rect, TextureLabPalette palette)
            {
                EditorGUI.DrawRect(rect, new Color(0.12f, 0.12f, 0.12f));
                if (palette == null || palette.Colors.Count == 0)
                    return;

                float swatchWidth = rect.width / palette.Colors.Count;
                for (int i = 0; i < palette.Colors.Count; i++)
                {
                    float xMin = Mathf.Round(rect.x + swatchWidth * i);
                    float xMax = Mathf.Round(rect.x + swatchWidth * (i + 1));
                    EditorGUI.DrawRect(new Rect(xMin, rect.y, Mathf.Max(1f, xMax - xMin), rect.height), palette.Colors[i]);
                }
            }
        }

        private SliderInt IntSlider(string label, int value, int min, int max, Action<int> setter, TextureEffectData effect)
        {
            var slider = new SliderInt(label, min, max) { value = value, showInputField = true };
            slider.RegisterValueChangedCallback(evt => EditEffect(effect, $"Change {label}", () => setter(evt.newValue)));
            return slider;
        }

        private Slider FloatSlider(string label, float value, float min, float max, Action<float> setter, TextureEffectData effect)
        {
            var slider = new Slider(label, min, max) { value = value, showInputField = true };
            slider.RegisterValueChangedCallback(evt => EditEffect(effect, $"Change {label}", () => setter(evt.newValue)));
            return slider;
        }

        private void ShowAddMenu()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Pixelate"), false, () => AddEffect(new PixelateEffectData()));
            menu.AddItem(new GUIContent("Posterize"), false, () => AddEffect(new PosterizeEffectData()));
            menu.AddItem(new GUIContent("Levels"), false, () => AddEffect(new LevelsEffectData()));
            menu.AddItem(new GUIContent("Color Adjustments"), false, () => AddEffect(new ColorAdjustmentsEffectData()));
            menu.AddItem(new GUIContent("Color Replace"), false, () => AddEffect(new ColorReplaceEffectData()));
            menu.AddItem(new GUIContent("Palette Quantization"), false, () => AddEffect(new PaletteQuantizeEffectData()));
            menu.AddItem(new GUIContent("Dither"), false, () => AddEffect(new DitherEffectData()));
            menu.AddItem(new GUIContent("Noise"), false, () => AddEffect(new NoiseEffectData()));
            menu.AddItem(new GUIContent("Gaussian Blur"), false, () => AddEffect(new GaussianBlurEffectData()));
            menu.AddItem(new GUIContent("Offset"), false, () => AddEffect(new OffsetEffectData()));
            menu.AddItem(new GUIContent("Seam Blend"), false, () => AddEffect(new SeamBlendEffectData()));
            menu.AddItem(new GUIContent("Channel Mixer"), false, () => AddEffect(new ChannelRemapEffectData()));
            menu.AddItem(new GUIContent("Dodge / Burn Brush"), false, () => AddEffect(new ExposureBrushEffectData()));
            menu.ShowAsContext();
        }

        private void CreateAndAssignPalette(PaletteQuantizeEffectData effect)
        {
            TextureLabPalette palette = CreatePaletteAsset();
            if (palette == null)
                return;

            EditEffect(effect, "Assign Texture Lab Palette", () => effect.Palette = palette);
            effectList.Rebuild();
        }

        private void ExtractPalette(PaletteQuantizeEffectData effect)
        {
            if (session.SourceTexture == null)
            {
                ShowNotification(new GUIContent("Choose a source texture first."));
                return;
            }

            TextureLabPalette palette = effect.Palette;
            if (palette == null)
            {
                palette = CreatePaletteAsset();
                if (palette == null)
                    return;

                TextureLabPalette createdPalette = palette;
                EditEffect(effect, "Assign Extracted Palette", () => effect.Palette = createdPalette);
            }

            List<Color> colors = PaletteExtractor.Extract(session.SourceTexture, effect.ExtractionColorCount);
            EditPalette(effect, palette, "Extract Texture Lab Palette", target =>
            {
                target.Colors.Clear();
                target.Colors.AddRange(colors);
            });
            effectList.Rebuild();
        }

        private static TextureLabPalette CreatePaletteAsset()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create Texture Lab Palette",
                "Texture Lab Palette",
                "asset",
                "Choose where to save the palette asset.",
                "Assets");
            if (string.IsNullOrEmpty(path))
                return null;

            path = AssetDatabase.GenerateUniqueAssetPath(path);
            var palette = CreateInstance<TextureLabPalette>();
            AssetDatabase.CreateAsset(palette, path);
            AssetDatabase.SaveAssetIfDirty(palette);
            Selection.activeObject = palette;
            return palette;
        }

        private void AddPaletteColor(PaletteQuantizeEffectData effect, TextureLabPalette palette)
        {
            if (palette.Colors.Count >= 64)
                return;

            Color color = palette.Colors.Count > 0 ? palette.Colors[^1] : Color.white;
            EditPalette(effect, palette, "Add Palette Color", target => target.Colors.Add(color));
            effectList.Rebuild();
        }

        private void RemovePaletteColor(PaletteQuantizeEffectData effect, TextureLabPalette palette, int index)
        {
            if (palette.Colors.Count <= 1 || index < 0 || index >= palette.Colors.Count)
                return;

            EditPalette(effect, palette, "Remove Palette Color", target => target.Colors.RemoveAt(index));
            effectList.Rebuild();
        }

        private void MovePaletteColor(PaletteQuantizeEffectData effect, TextureLabPalette palette, int from, int to)
        {
            if (from < 0 || from >= palette.Colors.Count || to < 0 || to >= palette.Colors.Count)
                return;

            EditPalette(effect, palette, "Reorder Palette Color", target =>
                (target.Colors[from], target.Colors[to]) = (target.Colors[to], target.Colors[from]));
            effectList.Rebuild();
        }

        private TextureLabPalette EditPalette(
            PaletteQuantizeEffectData effect,
            TextureLabPalette palette,
            string undoName,
            Action<TextureLabPalette> change)
        {
            if (session == null || effect == null || palette == null || change == null || !session.Effects.Contains(effect))
                return null;

            Undo.RecordObject(session, undoName);
            TextureLabPalette editable = session.EnsureEditablePalette(palette);
            if (editable == null)
                return null;

            effect.Palette = editable;
            change(editable);
            session.UpdatePaletteOverride(editable);
            EditorUtility.SetDirty(session);
            session.Persist();
            session.ResolvePaletteReferences(LoadAvailablePalettes());
            ProcessPreview();
            return editable;
        }

        private void AddEffect(TextureEffectData effect)
        {
            ChangeSession("Add Texture Lab Effect", () => session.Effects.Add(effect));
            effectList.Rebuild();
        }

        private void DuplicateEffect(TextureEffectData effect)
        {
            int index = session.Effects.IndexOf(effect);
            if (index < 0)
                return;

            ChangeSession("Duplicate Texture Lab Effect", () => session.Effects.Insert(index + 1, effect.Duplicate()));
            effectList.Rebuild();
        }

        private void RemoveEffect(TextureEffectData effect)
        {
            ChangeSession("Remove Texture Lab Effect", () => session.Effects.Remove(effect));
            effectList.Rebuild();
        }

        private void ToggleExpanded(TextureEffectData effect)
        {
            effect.Expanded = !effect.Expanded;
            session.Persist();
            effectList.Rebuild();
        }

        private void EditEffect(TextureEffectData effect, string undoName, Action change)
        {
            if (!session.Effects.Contains(effect))
                return;

            ChangeSession(undoName, change);
            if (effect == activeBrush)
                RefreshBrushCursor();
        }

        private void ChangeSession(string undoName, Action change, bool reprocess = true)
        {
            Undo.RecordObject(session, undoName);
            change();
            EditorUtility.SetDirty(session);
            session.Persist();
            if (reprocess)
                ProcessPreview();
        }

        private void CommitListChange()
        {
            EditorUtility.SetDirty(session);
            session.Persist();
            effectList.Rebuild();
            ProcessPreview();
        }

        private void SetSource(Texture2D texture)
        {
            if (texture != null && !IsSupportedTexture(texture, out string reason))
            {
                ShowNotification(new GUIContent(reason));
                sourceField.SetValueWithoutNotify(session.SourceTexture);
                return;
            }

            ChangeSession("Change Texture Lab Source", () => session.SourceTexture = texture);
        }

        private void OpenVariations()
        {
            if (session.SourceTexture == null)
            {
                ShowNotification(new GUIContent("Choose a source texture first."));
                return;
            }

            VariationsWindow.Open(session.SourceTexture, session.Effects, ApplyVariation);
        }

        private void OpenExport()
        {
            if (session.SourceTexture == null)
            {
                ShowNotification(new GUIContent("Choose a source texture first."));
                return;
            }

            TextureLabExportWindow.Open(session.SourceTexture, session.Effects, () => ShowNotification(new GUIContent("Texture exported.")));
        }

        private void ApplyVariation(List<TextureEffectData> variation)
        {
            ChangeSession("Apply Texture Lab Variation", () => session.ReplaceEffects(variation));
            effectList.Rebuild();
        }

        private void SavePresetAs()
        {
            TextureLabPreset preset = CreatePresetAsset("Texture Lab Preset", session.Effects);
            if (preset == null)
                return;

            SelectPreset(preset);
        }

        private void ApplyPreset()
        {
            if (selectedPreset == null)
                return;

            ChangeSession("Apply Texture Lab Preset", () => session.ReplaceEffects(selectedPreset.Effects));
            session.ResolvePaletteReferences(LoadAvailablePalettes());
            effectList.Rebuild();
        }

        private void OverwritePreset()
        {
            if (!IsEditablePreset(selectedPreset))
                return;

            if (!EditorUtility.DisplayDialog(
                    "Overwrite Texture Lab Preset",
                    "Replace the effect stack in '" + selectedPreset.name + "'?",
                    "Overwrite",
                    "Cancel"))
                return;

            Undo.RecordObject(selectedPreset, "Overwrite Texture Lab Preset");
            selectedPreset.SetEffects(session.Effects);
            EditorUtility.SetDirty(selectedPreset);
            AssetDatabase.SaveAssetIfDirty(selectedPreset);
        }

        private void DuplicatePreset()
        {
            if (selectedPreset == null)
                return;

            TextureLabPreset copy = CreatePresetAsset(selectedPreset.name + " Copy", selectedPreset.Effects);
            if (copy != null)
                SelectPreset(copy);
        }

        private void RenamePreset()
        {
            if (!IsEditablePreset(selectedPreset))
                return;

            string newName = presetNameField.value.Trim();
            if (string.IsNullOrEmpty(newName) || string.Equals(newName, selectedPreset.name, StringComparison.Ordinal))
                return;

            string error = AssetDatabase.RenameAsset(AssetDatabase.GetAssetPath(selectedPreset), newName);
            if (!string.IsNullOrEmpty(error))
            {
                ShowNotification(new GUIContent(error));
                return;
            }

            AssetDatabase.SaveAssets();
            presetNameField.SetValueWithoutNotify(selectedPreset.name);
        }

        private void ResetStack()
        {
            if (session.Effects.Count == 0)
                return;

            ChangeSession("Reset Texture Lab Stack", () => session.ReplaceEffects(Array.Empty<TextureEffectData>()));
            effectList.Rebuild();
        }

        private TextureLabPreset CreatePresetAsset(string defaultName, IEnumerable<TextureEffectData> effects)
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Save Texture Lab Preset",
                defaultName,
                "asset",
                "Choose a project location for the preset.",
                "Assets");
            if (string.IsNullOrEmpty(path))
                return null;

            path = AssetDatabase.GenerateUniqueAssetPath(path);
            var preset = CreateInstance<TextureLabPreset>();
            preset.SetEffects(effects);
            AssetDatabase.CreateAsset(preset, path);
            Undo.RegisterCreatedObjectUndo(preset, "Create Texture Lab Preset");
            AssetDatabase.SaveAssetIfDirty(preset);
            return preset;
        }

        private void SelectPreset(TextureLabPreset preset)
        {
            selectedPreset = preset;
            presetField.SetValueWithoutNotify(preset);
            presetNameField.SetValueWithoutNotify(preset != null ? preset.name : string.Empty);
            Selection.activeObject = preset;
            UpdatePresetControls();
        }

        private static bool IsSupportedTexture(Texture2D texture, out string reason)
        {
            string path = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(path) || AssetImporter.GetAtPath(path) is not TextureImporter importer)
            {
                reason = "Choose a Texture2D asset from the Project window.";
                return false;
            }

            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension is ".hdr" or ".exr")
            {
                reason = "Texture Lab currently supports LDR textures only.";
                return false;
            }

            if (importer.textureType is not TextureImporterType.Default and not TextureImporterType.Sprite)
            {
                reason = "Texture Lab currently supports Default and Sprite textures only.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private void UpdateTextureDrag()
        {
            DragAndDrop.visualMode = GetDraggedTexture() != null ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
        }

        private void PerformTextureDrag()
        {
            Texture2D texture = GetDraggedTexture();
            if (texture == null)
                return;

            DragAndDrop.AcceptDrag();
            sourceField.value = texture;
        }

        private static Texture2D GetDraggedTexture()
        {
            foreach (UnityEngine.Object draggedObject in DragAndDrop.objectReferences)
            {
                if (draggedObject is Texture2D texture)
                    return texture;
            }

            return null;
        }

        private void ProcessPreview()
        {
            if (previewGrid == null)
                return;

            if (session.SourceTexture == null)
            {
                previewTexture = null;
                foreach (Image image in previewImages)
                    image.image = null;
                previewMessage.text = "Drop a Texture2D here\nor choose one above";
                previewMessage.style.display = DisplayStyle.Flex;
                return;
            }

            try
            {
                processor.Process(session.SourceTexture, session.Effects, session.PreviewMaxDimension);
                UpdatePreviewDisplay();
                ApplyPreviewLayout();
            }
            catch (Exception exception)
            {
                previewTexture = null;
                foreach (Image image in previewImages)
                    image.image = null;
                previewMessage.text = "Preview failed. See Console.";
                previewMessage.style.display = DisplayStyle.Flex;
                Debug.LogException(exception);
            }
        }

        private void OnUndoRedo()
        {
            if (sourceField == null)
                return;

            sourceField.SetValueWithoutNotify(session.SourceTexture);
            session.ResolvePaletteReferences(LoadAvailablePalettes());
            effectList?.Rebuild();
            session.Persist();
            ProcessPreview();
        }
    }
}
