using System;
using System.Collections.Generic;
using UnityEngine;

namespace TextureLab.Runtime
{
    /// <summary>
    /// Applies an effect-stack/document edit while preserving source, preview
    /// preferences and other non-undoable session concerns.
    /// </summary>
    public sealed class TextureLabDocumentEditCommand : ITextureLabCommand, ITextureLabCommandMemoryEstimate
    {
        private readonly TextureLabDocument editedDocument;
        private TextureLabSessionSnapshot before;
        private bool hasBefore;

        public TextureLabDocumentEditCommand(string name, TextureLabDocument editedDocument)
        {
            if (editedDocument == null)
                throw new ArgumentNullException(nameof(editedDocument));

            Name = string.IsNullOrEmpty(name) ? "Edit Texture Lab Document" : name;
            this.editedDocument = TextureLabSessionState.CloneDocument(editedDocument);
        }

        public string Name { get; }
        public long EstimatedMemoryBytes => 1024
            + TextureLabSessionState.EstimateDocumentMemory(editedDocument)
            + (hasBefore && before != null ? before.EstimatedMemoryBytes : 0);

        public bool TryExecute(TextureLabApplicationController controller, out TextureLabValidationResult validation)
        {
            validation = new TextureLabValidationResult();
            if (controller == null)
            {
                validation.Add("controller", "The application controller is missing.");
                return false;
            }

            TextureLabSessionState session = controller.Session;
            TextureLabSessionSnapshot rollback = session.CaptureSnapshot();
            TextureLabDocument current = session.ActiveDocument;
            if (!HasSameNonEditableState(current, editedDocument))
            {
                validation.Add("document", "Undo commands may change only effects and palette dependencies.");
                return false;
            }

            validation.AddRange(TextureLabPortableSerializer.ValidateDocument(
                editedDocument,
                session.OriginalEncodedBytes));
            if (!validation.IsValid)
                return false;

            try
            {
                session.ApplyDocumentEdits(editedDocument);
                if (!hasBefore)
                {
                    before = rollback;
                    hasBefore = true;
                }

                return true;
            }
            catch (Exception exception)
            {
                session.RestoreSnapshot(rollback);
                validation.Add("document", $"Could not apply the document edit: {exception.Message}");
                return false;
            }
        }

        public bool TryUndo(TextureLabApplicationController controller, out TextureLabValidationResult validation)
        {
            validation = new TextureLabValidationResult();
            if (controller == null)
            {
                validation.Add("controller", "The application controller is missing.");
                return false;
            }

            if (!hasBefore)
            {
                validation.Add("command", "The command has not been executed.");
                return false;
            }

            try
            {
                controller.Session.RestoreSnapshot(before);
                return true;
            }
            catch (Exception exception)
            {
                validation.Add("document", $"Could not undo the document edit: {exception.Message}");
                return false;
            }
        }

        private static bool HasSameNonEditableState(TextureLabDocument left, TextureLabDocument right)
        {
            if (left == null || right == null
                || left.schemaVersion != right.schemaVersion
                || !string.Equals(left.applicationVersion, right.applicationVersion, StringComparison.Ordinal)
                || !string.Equals(left.documentId, right.documentId, StringComparison.Ordinal)
                || !string.Equals(left.displayName, right.displayName, StringComparison.Ordinal)
                || !SameSource(left.sourceImage, right.sourceImage)
                || !SamePreviewPreferences(left.previewPreferences, right.previewPreferences)
                || !SameMetadata(left.metadata, right.metadata))
                return false;

            return true;
        }

        private static bool SameSource(TextureLabSourceImage left, TextureLabSourceImage right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left == null || right == null)
                return false;

            return string.Equals(left.originalFileName, right.originalFileName, StringComparison.Ordinal)
                && string.Equals(left.mimeType, right.mimeType, StringComparison.Ordinal)
                && left.width == right.width
                && left.height == right.height
                && string.Equals(left.embeddedEntryPath, right.embeddedEntryPath, StringComparison.Ordinal);
        }

        private static bool SamePreviewPreferences(TextureLabPreviewPreferences left, TextureLabPreviewPreferences right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left == null || right == null)
                return false;

            return left.tiles == right.tiles
                && left.zoom == right.zoom
                && left.channel == right.channel
                && left.maxDimension == right.maxDimension;
        }

        private static bool SameMetadata(
            IReadOnlyList<TextureLabMetadataEntry> left,
            IReadOnlyList<TextureLabMetadataEntry> right)
        {
            if (left == null || right == null)
                return left == null && right == null;
            if (left.Count != right.Count)
                return false;

            for (int index = 0; index < left.Count; index++)
            {
                TextureLabMetadataEntry leftEntry = left[index];
                TextureLabMetadataEntry rightEntry = right[index];
                if (ReferenceEquals(leftEntry, rightEntry))
                    continue;
                if (leftEntry == null || rightEntry == null
                    || !string.Equals(leftEntry.key, rightEntry.key, StringComparison.Ordinal)
                    || !string.Equals(leftEntry.value, rightEntry.value, StringComparison.Ordinal))
                    return false;
            }

            return true;
        }
    }

    public sealed class TextureLabPaletteAddCommand : ITextureLabCommand, ITextureLabCommandMemoryEstimate
    {
        private readonly TextureLabPaletteDefinition definition;
        private bool added;

        public TextureLabPaletteAddCommand(string name, TextureLabPaletteDefinition definition)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));

            Name = string.IsNullOrEmpty(name) ? "Add Texture Lab Palette" : name;
            this.definition = TextureLabSessionState.ClonePaletteDefinition(definition);
        }

        public string Name { get; }
        public long EstimatedMemoryBytes => 512 + TextureLabSessionState.EstimatePaletteMemory(definition);

        public bool TryExecute(TextureLabApplicationController controller, out TextureLabValidationResult validation)
        {
            validation = new TextureLabValidationResult();
            if (controller == null)
            {
                validation.Add("controller", "The application controller is missing.");
                return false;
            }

            if (!controller.Palettes.TryAddUser(definition, out validation))
                return false;

            added = true;
            return true;
        }

        public bool TryUndo(TextureLabApplicationController controller, out TextureLabValidationResult validation)
        {
            validation = new TextureLabValidationResult();
            if (controller == null)
            {
                validation.Add("controller", "The application controller is missing.");
                return false;
            }
            if (!added)
            {
                validation.Add("command", "The command has not been executed.");
                return false;
            }

            return controller.Palettes.TryRemoveUser(definition.id, out validation);
        }
    }

    public sealed class TextureLabPaletteRemoveCommand : ITextureLabCommand, ITextureLabCommandMemoryEstimate
    {
        private readonly string paletteId;
        private TextureLabPaletteDefinition removed;
        private bool hasRemoved;

        public TextureLabPaletteRemoveCommand(string name, string paletteId)
        {
            Name = string.IsNullOrEmpty(name) ? "Remove Texture Lab Palette" : name;
            this.paletteId = paletteId ?? string.Empty;
        }

        public string Name { get; }
        public long EstimatedMemoryBytes => 512
            + TextureLabSessionState.EstimatePaletteMemory(removed);

        public bool TryExecute(TextureLabApplicationController controller, out TextureLabValidationResult validation)
        {
            validation = new TextureLabValidationResult();
            if (controller == null)
            {
                validation.Add("controller", "The application controller is missing.");
                return false;
            }

            if (!hasRemoved && !controller.Palettes.TryGet(paletteId, out removed))
            {
                validation.Add("paletteId", "The palette does not exist.");
                return false;
            }

            if (!controller.Palettes.TryRemoveUser(paletteId, out validation))
                return false;

            hasRemoved = true;
            return true;
        }

        public bool TryUndo(TextureLabApplicationController controller, out TextureLabValidationResult validation)
        {
            validation = new TextureLabValidationResult();
            if (controller == null)
            {
                validation.Add("controller", "The application controller is missing.");
                return false;
            }
            if (!hasRemoved)
            {
                validation.Add("command", "The command has not been executed.");
                return false;
            }

            return controller.Palettes.TryAddUser(removed, out validation);
        }
    }

    public sealed class TextureLabPaletteUpdateCommand : ITextureLabCommand, ITextureLabCommandMemoryEstimate
    {
        private readonly TextureLabPaletteDefinition updated;
        private TextureLabPaletteDefinition before;
        private bool hasBefore;

        public TextureLabPaletteUpdateCommand(string name, TextureLabPaletteDefinition updated)
        {
            if (updated == null)
                throw new ArgumentNullException(nameof(updated));

            Name = string.IsNullOrEmpty(name) ? "Update Texture Lab Palette" : name;
            this.updated = TextureLabSessionState.ClonePaletteDefinition(updated);
        }

        public string Name { get; }
        public long EstimatedMemoryBytes => 512
            + TextureLabSessionState.EstimatePaletteMemory(updated)
            + (hasBefore ? TextureLabSessionState.EstimatePaletteMemory(before) : 0);

        public bool TryExecute(TextureLabApplicationController controller, out TextureLabValidationResult validation)
        {
            validation = new TextureLabValidationResult();
            if (controller == null)
            {
                validation.Add("controller", "The application controller is missing.");
                return false;
            }

            if (!hasBefore && !controller.Palettes.TryGet(updated.id, out before))
            {
                validation.Add("paletteId", "The palette does not exist.");
                return false;
            }

            if (!controller.Palettes.TryUpdate(updated, out validation))
                return false;

            hasBefore = true;
            return true;
        }

        public bool TryUndo(TextureLabApplicationController controller, out TextureLabValidationResult validation)
        {
            validation = new TextureLabValidationResult();
            if (controller == null)
            {
                validation.Add("controller", "The application controller is missing.");
                return false;
            }
            if (!hasBefore)
            {
                validation.Add("command", "The command has not been executed.");
                return false;
            }

            return controller.Palettes.TryUpdate(before, out validation);
        }
    }

    public sealed class TextureLabPresetAddCommand : ITextureLabCommand, ITextureLabCommandMemoryEstimate
    {
        private readonly TextureLabPresetDefinition definition;
        private bool added;

        public TextureLabPresetAddCommand(string name, TextureLabPresetDefinition definition)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));
            Name = string.IsNullOrEmpty(name) ? "Add Texture Lab Preset" : name;
            this.definition = TextureLabSessionState.ClonePresetDefinition(definition);
        }

        public string Name { get; }
        public long EstimatedMemoryBytes => 512 + TextureLabSessionState.EstimatePresetMemory(definition);

        public bool TryExecute(TextureLabApplicationController controller, out TextureLabValidationResult validation)
        {
            validation = new TextureLabValidationResult();
            if (controller == null)
            {
                validation.Add("controller", "The application controller is missing.");
                return false;
            }
            if (!controller.Presets.TryAddUser(definition, out validation))
                return false;
            added = true;
            return true;
        }

        public bool TryUndo(TextureLabApplicationController controller, out TextureLabValidationResult validation)
        {
            validation = new TextureLabValidationResult();
            if (controller == null)
            {
                validation.Add("controller", "The application controller is missing.");
                return false;
            }
            if (!added)
            {
                validation.Add("command", "The command has not been executed.");
                return false;
            }
            return controller.Presets.TryRemoveUser(definition.id, out validation);
        }
    }

    public sealed class TextureLabPresetRemoveCommand : ITextureLabCommand, ITextureLabCommandMemoryEstimate
    {
        private readonly string presetId;
        private TextureLabPresetDefinition removed;
        private bool hasRemoved;

        public TextureLabPresetRemoveCommand(string name, string presetId)
        {
            Name = string.IsNullOrEmpty(name) ? "Remove Texture Lab Preset" : name;
            this.presetId = presetId ?? string.Empty;
        }

        public string Name { get; }
        public long EstimatedMemoryBytes => 512 + TextureLabSessionState.EstimatePresetMemory(removed);

        public bool TryExecute(TextureLabApplicationController controller, out TextureLabValidationResult validation)
        {
            validation = new TextureLabValidationResult();
            if (controller == null)
            {
                validation.Add("controller", "The application controller is missing.");
                return false;
            }
            if (!hasRemoved && !controller.Presets.TryGet(presetId, out removed))
            {
                validation.Add("presetId", "The preset does not exist.");
                return false;
            }
            if (!controller.Presets.TryRemoveUser(presetId, out validation))
                return false;
            hasRemoved = true;
            return true;
        }

        public bool TryUndo(TextureLabApplicationController controller, out TextureLabValidationResult validation)
        {
            validation = new TextureLabValidationResult();
            if (controller == null)
            {
                validation.Add("controller", "The application controller is missing.");
                return false;
            }
            if (!hasRemoved)
            {
                validation.Add("command", "The command has not been executed.");
                return false;
            }
            return controller.Presets.TryAddUser(removed, out validation);
        }
    }

    public sealed class TextureLabPresetUpdateCommand : ITextureLabCommand, ITextureLabCommandMemoryEstimate
    {
        private readonly TextureLabPresetDefinition updated;
        private TextureLabPresetDefinition before;
        private bool hasBefore;

        public TextureLabPresetUpdateCommand(string name, TextureLabPresetDefinition updated)
        {
            if (updated == null)
                throw new ArgumentNullException(nameof(updated));
            Name = string.IsNullOrEmpty(name) ? "Update Texture Lab Preset" : name;
            this.updated = TextureLabSessionState.ClonePresetDefinition(updated);
        }

        public string Name { get; }
        public long EstimatedMemoryBytes => 512
            + TextureLabSessionState.EstimatePresetMemory(updated)
            + (hasBefore ? TextureLabSessionState.EstimatePresetMemory(before) : 0);

        public bool TryExecute(TextureLabApplicationController controller, out TextureLabValidationResult validation)
        {
            validation = new TextureLabValidationResult();
            if (controller == null)
            {
                validation.Add("controller", "The application controller is missing.");
                return false;
            }
            if (!hasBefore && !controller.Presets.TryGet(updated.id, out before))
            {
                validation.Add("presetId", "The preset does not exist.");
                return false;
            }
            if (!controller.Presets.TryUpdateUser(updated, out validation))
                return false;
            hasBefore = true;
            return true;
        }

        public bool TryUndo(TextureLabApplicationController controller, out TextureLabValidationResult validation)
        {
            validation = new TextureLabValidationResult();
            if (controller == null)
            {
                validation.Add("controller", "The application controller is missing.");
                return false;
            }
            if (!hasBefore)
            {
                validation.Add("command", "The command has not been executed.");
                return false;
            }
            return controller.Presets.TryUpdateUser(before, out validation);
        }
    }
}
