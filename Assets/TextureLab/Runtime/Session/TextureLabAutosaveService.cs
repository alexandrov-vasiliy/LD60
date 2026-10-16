using System;

namespace TextureLab.Runtime
{
    public sealed class TextureLabAutosaveOptions
    {
        public TextureLabAutosaveOptions(float debounceSeconds = 1f)
        {
            if (debounceSeconds < 0f)
                throw new ArgumentOutOfRangeException(nameof(debounceSeconds));
            DebounceSeconds = debounceSeconds;
        }

        public float DebounceSeconds { get; }
    }

    /// <summary>
    /// Debounced last-session writer. The host calls Update from its normal
    /// runtime tick and Flush on shutdown/visibility changes. No processing or
    /// GPU resources are owned here.
    /// </summary>
    public sealed class TextureLabAutosaveService : IDisposable
    {
        private readonly TextureLabApplicationController controller;
        private readonly ITextureLabLocalDataStore store;
        private readonly TextureLabAutosaveOptions options;
        private float elapsed;
        private bool pending;
        private bool disposed;
        private bool flushing;

        internal TextureLabAutosaveService(
            TextureLabApplicationController controller,
            ITextureLabLocalDataStore store,
            TextureLabAutosaveOptions options)
        {
            this.controller = controller ?? throw new ArgumentNullException(nameof(controller));
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.options = options ?? new TextureLabAutosaveOptions();
        }

        public bool IsPending => pending;
        public float SecondsUntilSave => pending
            ? Math.Max(0f, options.DebounceSeconds - elapsed)
            : 0f;
        public bool LastSaveSucceeded { get; private set; }
        public string LastSaveError { get; private set; } = string.Empty;

        public event Action Changed;

        internal void MarkPending()
        {
            if (disposed)
                return;

            pending = true;
            elapsed = 0f;
            Changed?.Invoke();
            if (options.DebounceSeconds <= 0f)
                TryFlush(out _);
        }

        public void Update(float deltaTime)
        {
            if (disposed || !pending || flushing)
                return;

            elapsed += Math.Max(0f, deltaTime);
            if (elapsed >= options.DebounceSeconds)
                TryFlush(out _);
        }

        public bool Flush(out TextureLabValidationResult validation)
        {
            validation = new TextureLabValidationResult();
            if (disposed)
            {
                validation.Add("autosave", "The autosave service has been disposed.");
                return false;
            }
            if (!pending)
                return true;

            return TryFlush(out validation);
        }

        public void Dispose()
        {
            if (disposed)
                return;

            Flush(out _);
            disposed = true;
            Changed = null;
        }

        private bool TryFlush(out TextureLabValidationResult validation)
        {
            validation = new TextureLabValidationResult();
            if (flushing)
            {
                validation.Add("autosave", "An autosave operation is already in progress.");
                return false;
            }

            flushing = true;
            controller.BeginProgress("autosave", false);
            try
            {
                if (!controller.Session.HasActiveDocument)
                {
                    pending = false;
                    elapsed = 0f;
                    LastSaveSucceeded = true;
                    LastSaveError = string.Empty;
                    controller.CompleteProgress();
                    Changed?.Invoke();
                    return true;
                }

                byte[] sourceBytes = controller.Session.OriginalEncodedBytes;
                if (sourceBytes == null)
                {
                    validation.Add("autosave", "The active document has no original encoded source bytes.");
                    return Fail(validation);
                }

                byte[] archive;
                try
                {
                    archive = TextureLabSessionArchive.Serialize(
                        controller.Session.ActiveDocument,
                        sourceBytes,
                        controller.Session.ActivePaletteId,
                        controller.Session.ActivePresetId,
                        controller.Session.IsDirty);
                }
                catch (Exception exception)
                {
                    validation.Add("autosave", $"Could not serialize the last session: {exception.Message}");
                    return Fail(validation);
                }

                if (!store.TryWriteAtomic(
                        TextureLabSessionArchive.LastSessionKey,
                        archive,
                        out TextureLabStorageResult result))
                {
                    validation.Add("autosave", $"Could not write the last session ({result.Code}): {result.Message}");
                    return Fail(validation);
                }

                pending = false;
                elapsed = 0f;
                LastSaveSucceeded = true;
                LastSaveError = string.Empty;
                controller.ClearError("storage.autosave");
                controller.CompleteProgress();
                Changed?.Invoke();
                return true;
            }
            finally
            {
                flushing = false;
            }
        }

        private bool Fail(TextureLabValidationResult validation)
        {
            LastSaveSucceeded = false;
            LastSaveError = validation.ToString();
            controller.ReportError("storage.autosave", LastSaveError, true);
            controller.CompleteProgress();
            Changed?.Invoke();
            return false;
        }
    }
}
