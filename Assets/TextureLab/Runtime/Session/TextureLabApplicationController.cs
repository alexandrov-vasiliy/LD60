using System;

namespace TextureLab.Runtime
{
    /// <summary>
    /// Runtime application boundary shared by Editor, Windows, and Web hosts.
    /// It owns session lifecycle, local-data services and command history while
    /// remaining independent from UnityEditor and UI code.
    /// </summary>
    public sealed class TextureLabApplicationController : IDisposable
    {
        private bool disposed;
        private readonly TextureLabCommandHistory commandHistory;
        private readonly TextureLabAutosaveService autosave;

        public TextureLabApplicationController(
            TextureLabSessionState session = null,
            TextureLabPaletteRepository paletteRepository = null,
            TextureLabPresetRepository presetRepository = null,
            TextureLabUserDataRepository localDataRepository = null,
            TextureLabCommandHistoryOptions historyOptions = null,
            TextureLabAutosaveOptions autosaveOptions = null)
        {
            Session = session ?? new TextureLabSessionState();
            Palettes = paletteRepository ?? TextureLabPaletteRepository.CreateWithBuiltIns();
            Presets = presetRepository ?? TextureLabPresetRepository.CreateWithBuiltIns();
            if (localDataRepository != null
                && (!ReferenceEquals(localDataRepository.Palettes, Palettes)
                    || !ReferenceEquals(localDataRepository.Presets, Presets)))
            {
                throw new ArgumentException(
                    "The local-data repository must use the controller's palette and preset repositories.",
                    nameof(localDataRepository));
            }

            LocalData = localDataRepository ?? new TextureLabUserDataRepository(
                Palettes,
                Presets,
                new TextureLabFileLocalDataStore());
            commandHistory = new TextureLabCommandHistory(historyOptions);
            autosave = new TextureLabAutosaveService(this, LocalData.Store, autosaveOptions);

            Progress = TextureLabProgressState.Idle;
            LastError = TextureLabErrorState.None;

            Palettes.PaletteChanged += OnPaletteChanged;
            Presets.PresetChanged += OnPresetChanged;
            commandHistory.Changed += OnCommandHistoryChanged;
            autosave.Changed += OnAutosaveChanged;
            SessionChanged += OnSessionChangedForAutosave;

            if (!LocalData.TryLoad(out TextureLabValidationResult localDataValidation))
                ReportError("storage.userData", localDataValidation.ToString(), true);
            if (!Session.HasActiveDocument)
                TryRestoreLastSession(out _);
        }

        public TextureLabSessionState Session { get; }
        public TextureLabPaletteRepository Palettes { get; }
        public TextureLabPresetRepository Presets { get; }
        public TextureLabUserDataRepository LocalData { get; }
        public TextureLabAutosaveService Autosave => autosave;
        public TextureLabCommandHistory CommandHistory => commandHistory;
        public bool CanUndo => commandHistory.CanUndo;
        public bool CanRedo => commandHistory.CanRedo;
        public string UndoName => commandHistory.UndoName;
        public string RedoName => commandHistory.RedoName;
        public TextureLabProgressState Progress { get; private set; }
        public TextureLabErrorState LastError { get; private set; }

        public event Action SessionChanged;
        public event Action<string> PaletteRepositoryChanged;
        public event Action<string> PresetRepositoryChanged;
        public event Action CommandHistoryChanged;
        public event Action StatusChanged;
        public event Action AutosaveChanged;
        public event Action PreviewPreferencesChanged;

        /// <summary>
        /// Validates the complete document before atomically replacing the
        /// current session. The incoming source buffer is copied by the state.
        /// </summary>
        public bool TryOpenDocument(
            TextureLabDocument document,
            byte[] originalEncodedBytes,
            UnityEngine.Texture2D sourceTexture,
            out TextureLabValidationResult validation)
        {
            validation = new TextureLabValidationResult();
            if (!EnsureActive(validation))
                return false;

            validation.AddRange(TextureLabPortableSerializer.ValidateDocument(document, originalEncodedBytes));
            if (!validation.IsValid)
                return Fail("validation.document", validation);

            Session.ReplaceDocument(document, sourceTexture, originalEncodedBytes);
            commandHistory.Clear();
            ClearError("validation.document");
            SessionChanged?.Invoke();
            return true;
        }

        public bool TryRestoreLastSession(out TextureLabValidationResult validation)
        {
            validation = new TextureLabValidationResult();
            if (!EnsureActive(validation))
                return false;

            if (!LocalData.Store.TryRead(
                    TextureLabSessionArchive.LastSessionKey,
                    out byte[] archive,
                    out TextureLabStorageResult readResult))
            {
                if (readResult.NotFound)
                    return true;
                validation.Add("lastSession", $"Could not read the last session ({readResult.Code}): {readResult.Message}");
                return Fail("storage.lastSession", validation);
            }

            if (!TextureLabSessionArchive.TryDeserialize(
                    archive,
                    out TextureLabDocument document,
                    out byte[] sourceBytes,
                    out string activePaletteId,
                    out string activePresetId,
                    out bool isDirty,
                    out TextureLabValidationResult archiveValidation))
            {
                validation.AddRange(archiveValidation);
                return Fail("storage.lastSession", validation);
            }

            if (!string.IsNullOrEmpty(activePaletteId) && !Palettes.Contains(activePaletteId))
                validation.Add("activePaletteId", "The last-session palette is not available in local repositories.");
            if (!string.IsNullOrEmpty(activePresetId) && !Presets.Contains(activePresetId))
                validation.Add("activePresetId", "The last-session preset is not available in local repositories.");
            if (!validation.IsValid)
                return Fail("storage.lastSession", validation);

            Session.RestorePersistedSession(document, sourceBytes, activePaletteId, activePresetId, isDirty);
            commandHistory.Clear();
            ClearError("storage.lastSession");
            SessionChanged?.Invoke();
            return true;
        }

        public bool TryExecuteCommand(
            ITextureLabCommand command,
            out TextureLabValidationResult validation)
        {
            validation = new TextureLabValidationResult();
            if (!EnsureActive(validation))
                return false;

            if (!commandHistory.TryExecute(this, command, out validation))
                return Fail("command", validation);

            ClearError("command");
            SessionChanged?.Invoke();
            return true;
        }

        public bool TryUndo(out TextureLabValidationResult validation)
        {
            validation = new TextureLabValidationResult();
            if (!EnsureActive(validation))
                return false;

            if (!commandHistory.TryUndo(this, out validation))
                return Fail("command.undo", validation);

            ClearError("command.undo");
            SessionChanged?.Invoke();
            return true;
        }

        public bool TryRedo(out TextureLabValidationResult validation)
        {
            validation = new TextureLabValidationResult();
            if (!EnsureActive(validation))
                return false;

            if (!commandHistory.TryRedo(this, out validation))
                return Fail("command.redo", validation);

            ClearError("command.redo");
            SessionChanged?.Invoke();
            return true;
        }

        public bool TrySelectPalette(string paletteId, out TextureLabValidationResult validation)
        {
            validation = new TextureLabValidationResult();
            if (!EnsureActive(validation))
                return false;

            if (!string.IsNullOrEmpty(paletteId) && !Palettes.Contains(paletteId))
            {
                validation.Add("paletteId", "The palette does not exist in the repository.");
                return Fail("validation.palette", validation);
            }

            if (Session.ActivePaletteId == (paletteId ?? string.Empty))
                return true;

            Session.SetActivePalette(paletteId);
            ClearError("validation.palette");
            SessionChanged?.Invoke();
            return true;
        }

        public bool TrySelectPreset(string presetId, out TextureLabValidationResult validation)
        {
            validation = new TextureLabValidationResult();
            if (!EnsureActive(validation))
                return false;

            if (!string.IsNullOrEmpty(presetId) && !Presets.Contains(presetId))
            {
                validation.Add("presetId", "The preset does not exist in the repository.");
                return Fail("validation.preset", validation);
            }

            if (Session.ActivePresetId == (presetId ?? string.Empty))
                return true;

            Session.SetActivePreset(presetId);
            ClearError("validation.preset");
            SessionChanged?.Invoke();
            return true;
        }

        public void SetCapabilities(TextureLabCapabilitySnapshot snapshot)
        {
            if (disposed)
                return;

            Session.SetCapabilities(snapshot);
            SessionChanged?.Invoke();
        }

        /// <summary>
        /// Preview-only state is intentionally outside document undo and does
        /// not mark the document dirty or schedule autosave.
        /// </summary>
        public void SetPreviewPreferences(TextureLabPreviewPreferences preferences)
        {
            if (disposed || !Session.HasActiveDocument || preferences == null)
                return;

            Session.SetPreviewPreferences(preferences);
            PreviewPreferencesChanged?.Invoke();
        }

        public void Update(float deltaTime)
        {
            if (!disposed)
                autosave.Update(deltaTime);
        }

        public bool FlushAutosave(out TextureLabValidationResult validation)
        {
            if (disposed)
            {
                validation = new TextureLabValidationResult();
                validation.Add("controller", "The application controller has been disposed.");
                return false;
            }
            return autosave.Flush(out validation);
        }

        public void BeginProgress(string operation, bool canCancel)
        {
            if (disposed)
                return;
            Progress = new TextureLabProgressState(true, operation, 0f, canCancel);
            StatusChanged?.Invoke();
        }

        public void ReportProgress(float progress)
        {
            if (disposed || !Progress.IsBusy)
                return;
            Progress = new TextureLabProgressState(true, Progress.Operation, progress, Progress.CanCancel);
            StatusChanged?.Invoke();
        }

        public void CompleteProgress()
        {
            if (disposed || !Progress.IsBusy)
                return;
            Progress = TextureLabProgressState.Idle;
            StatusChanged?.Invoke();
        }

        public void ReportError(string code, string message, bool persistent)
        {
            if (disposed)
                return;
            LastError = new TextureLabErrorState(code, message, persistent);
            StatusChanged?.Invoke();
        }

        public void ClearError(string code = null)
        {
            if (disposed || !LastError.HasError)
                return;
            if (!string.IsNullOrEmpty(code) && !string.Equals(LastError.Code, code, StringComparison.Ordinal))
                return;
            LastError = TextureLabErrorState.None;
            StatusChanged?.Invoke();
        }

        public void Dispose()
        {
            if (disposed)
                return;

            autosave.Flush(out _);
            SessionChanged -= OnSessionChangedForAutosave;
            Palettes.PaletteChanged -= OnPaletteChanged;
            Presets.PresetChanged -= OnPresetChanged;
            commandHistory.Changed -= OnCommandHistoryChanged;
            autosave.Changed -= OnAutosaveChanged;
            autosave.Dispose();
            commandHistory.Clear();
            disposed = true;
        }

        private bool Fail(string code, TextureLabValidationResult validation)
        {
            ReportError(code, validation == null ? "The operation failed." : validation.ToString(), false);
            return false;
        }

        private bool EnsureActive(TextureLabValidationResult validation)
        {
            if (!disposed)
                return true;

            validation.Add("controller", "The application controller has been disposed.");
            return false;
        }

        private void OnPaletteChanged(string paletteId)
        {
            PaletteRepositoryChanged?.Invoke(paletteId);
            if (LocalData.IsLoading)
                return;
            if (Palettes.IsBuiltIn(paletteId))
                return;

            TextureLabValidationResult validation;
            bool success;
            if (Palettes.TryGet(paletteId, out TextureLabPaletteDefinition definition))
                success = LocalData.TrySavePalette(definition, out validation);
            else
                success = LocalData.TryDeletePalette(paletteId, out validation);

            if (success)
                ClearError("storage.userData");
            else
                ReportError("storage.userData", validation.ToString(), true);
        }

        private void OnPresetChanged(string presetId)
        {
            PresetRepositoryChanged?.Invoke(presetId);
            if (LocalData.IsLoading)
                return;
            if (Presets.IsBuiltIn(presetId))
                return;

            TextureLabValidationResult validation;
            bool success;
            if (Presets.TryGet(presetId, out TextureLabPresetDefinition definition))
                success = LocalData.TrySavePreset(definition, out validation);
            else
                success = LocalData.TryDeletePreset(presetId, out validation);

            if (success)
                ClearError("storage.userData");
            else
                ReportError("storage.userData", validation.ToString(), true);
        }

        private void OnCommandHistoryChanged()
        {
            CommandHistoryChanged?.Invoke();
        }

        private void OnSessionChangedForAutosave()
        {
            autosave.MarkPending();
        }

        private void OnAutosaveChanged()
        {
            AutosaveChanged?.Invoke();
        }
    }
}
