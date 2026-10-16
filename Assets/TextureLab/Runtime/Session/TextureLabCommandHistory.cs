using System;
using System.Collections.Generic;

namespace TextureLab.Runtime
{
    /// <summary>
    /// A user-visible runtime operation. Commands are intentionally not
    /// serialized; history exists only for the current application session.
    /// </summary>
    public interface ITextureLabCommand
    {
        string Name { get; }

        bool TryExecute(TextureLabApplicationController controller, out TextureLabValidationResult validation);

        bool TryUndo(TextureLabApplicationController controller, out TextureLabValidationResult validation);
    }

    /// <summary>
    /// Optional memory estimate supplied by commands that retain detached
    /// document snapshots. Commands without an estimate use a small safe
    /// baseline and are still covered by the count limit.
    /// </summary>
    public interface ITextureLabCommandMemoryEstimate
    {
        long EstimatedMemoryBytes { get; }
    }

    public sealed class TextureLabCommandHistoryOptions
    {
        public TextureLabCommandHistoryOptions(int maxCommands = 128, long maxMemoryBytes = 64L * 1024L * 1024L)
        {
            if (maxCommands < 1)
                throw new ArgumentOutOfRangeException(nameof(maxCommands));
            if (maxMemoryBytes < 1024)
                throw new ArgumentOutOfRangeException(nameof(maxMemoryBytes));

            MaxCommands = maxCommands;
            MaxMemoryBytes = maxMemoryBytes;
        }

        public int MaxCommands { get; }
        public long MaxMemoryBytes { get; }
    }

    /// <summary>
    /// Runtime command history shared by every host. Oldest undo entries are
    /// discarded when either configured bound is reached. Redo is cleared by
    /// every successful new command, matching standard editor semantics.
    /// </summary>
    public sealed class TextureLabCommandHistory
    {
        private const long DefaultCommandMemoryBytes = 1024;
        private delegate bool CommandOperation(
            TextureLabApplicationController controller,
            out TextureLabValidationResult validation);

        private readonly List<ITextureLabCommand> undoCommands = new();
        private readonly List<ITextureLabCommand> redoCommands = new();
        private readonly TextureLabCommandHistoryOptions options;

        public TextureLabCommandHistory(TextureLabCommandHistoryOptions options = null)
        {
            this.options = options ?? new TextureLabCommandHistoryOptions();
        }

        public event Action Changed;

        public bool CanUndo => undoCommands.Count > 0;
        public bool CanRedo => redoCommands.Count > 0;
        public int UndoCount => undoCommands.Count;
        public int RedoCount => redoCommands.Count;
        public long MemoryBytes => CalculateMemory(undoCommands) + CalculateMemory(redoCommands);
        public TextureLabCommandHistoryOptions Options => options;
        public string UndoName => CanUndo ? undoCommands[undoCommands.Count - 1].Name : string.Empty;
        public string RedoName => CanRedo ? redoCommands[redoCommands.Count - 1].Name : string.Empty;

        internal bool TryExecute(
            TextureLabApplicationController controller,
            ITextureLabCommand command,
            out TextureLabValidationResult validation)
        {
            validation = new TextureLabValidationResult();
            if (controller == null)
            {
                validation.Add("controller", "The application controller is missing.");
                return false;
            }

            if (command == null)
            {
                validation.Add("command", "The command is missing.");
                return false;
            }

            if (!TryInvoke(command.TryExecute, controller, validation))
                return false;

            undoCommands.Add(command);
            redoCommands.Clear();
            if (!KeepWithinLimits(controller, command, validation))
                return false;

            Changed?.Invoke();
            return true;
        }

        internal bool TryUndo(
            TextureLabApplicationController controller,
            out TextureLabValidationResult validation)
        {
            validation = new TextureLabValidationResult();
            if (!CanUndo)
            {
                validation.Add("history", "There is no command to undo.");
                return false;
            }

            int index = undoCommands.Count - 1;
            ITextureLabCommand command = undoCommands[index];
            if (!TryInvoke(command.TryUndo, controller, validation))
                return false;

            undoCommands.RemoveAt(index);
            redoCommands.Add(command);
            Changed?.Invoke();
            return true;
        }

        internal bool TryRedo(
            TextureLabApplicationController controller,
            out TextureLabValidationResult validation)
        {
            validation = new TextureLabValidationResult();
            if (!CanRedo)
            {
                validation.Add("history", "There is no command to redo.");
                return false;
            }

            int index = redoCommands.Count - 1;
            ITextureLabCommand command = redoCommands[index];
            if (!TryInvoke(command.TryExecute, controller, validation))
                return false;

            redoCommands.RemoveAt(index);
            undoCommands.Add(command);
            Changed?.Invoke();
            return true;
        }

        internal void Clear()
        {
            if (undoCommands.Count == 0 && redoCommands.Count == 0)
                return;

            undoCommands.Clear();
            redoCommands.Clear();
            Changed?.Invoke();
        }

        private bool KeepWithinLimits(
            TextureLabApplicationController controller,
            ITextureLabCommand latestCommand,
            TextureLabValidationResult validation)
        {
            while (undoCommands.Count > options.MaxCommands || CalculateMemory(undoCommands) > options.MaxMemoryBytes)
            {
                if (undoCommands.Count > 1)
                {
                    undoCommands.RemoveAt(0);
                    continue;
                }

                // A single command larger than the memory budget cannot be
                // retained. Roll it back so a user action is never reported
                // as successful while silently losing its undo entry.
                ITextureLabCommand onlyCommand = undoCommands[0];
                var rollbackValidation = new TextureLabValidationResult();
                if (!ReferenceEquals(onlyCommand, latestCommand)
                    || !TryInvoke(onlyCommand.TryUndo, controller, rollbackValidation))
                {
                    validation.Add("history", "The command exceeds the configured history memory limit and could not be rolled back.");
                    return true;
                }

                undoCommands.Clear();
                validation.Add("history", "The command exceeds the configured history memory limit.");
                validation.AddRange(rollbackValidation);
                return false;
            }

            return true;
        }

        private static long CalculateMemory(IReadOnlyList<ITextureLabCommand> commands)
        {
            long total = 0;
            foreach (ITextureLabCommand command in commands)
            {
                long estimate = command is ITextureLabCommandMemoryEstimate memoryAware
                    ? memoryAware.EstimatedMemoryBytes
                    : DefaultCommandMemoryBytes;
                total = SaturatingAdd(total, Math.Max(DefaultCommandMemoryBytes, estimate));
            }
            return total;
        }

        private static long SaturatingAdd(long left, long right)
        {
            if (right > long.MaxValue - left)
                return long.MaxValue;
            return left + right;
        }

        private static bool TryInvoke(
            CommandOperation operation,
            TextureLabApplicationController controller,
            TextureLabValidationResult validation)
        {
            try
            {
                if (operation(controller, out TextureLabValidationResult operationValidation))
                    return true;

                validation.AddRange(operationValidation);
                return false;
            }
            catch (Exception exception)
            {
                validation.Add("command", $"Command failed: {exception.Message}");
                return false;
            }
        }
    }
}
