using System;

namespace TextureLab.Runtime
{
    /// <summary>
    /// Host-facing progress state. Processing services can report progress
    /// without depending on a particular UI toolkit.
    /// </summary>
    [Serializable]
    public sealed class TextureLabProgressState
    {
        public static TextureLabProgressState Idle { get; } =
            new TextureLabProgressState(false, string.Empty, 0f, false);

        public TextureLabProgressState(bool isBusy, string operation, float progress, bool canCancel)
        {
            IsBusy = isBusy;
            Operation = operation ?? string.Empty;
            Progress = Clamp01(progress);
            CanCancel = canCancel;
        }

        public bool IsBusy { get; }
        public string Operation { get; }
        public float Progress { get; }
        public bool CanCancel { get; }

        private static float Clamp01(float value)
        {
            return value < 0f ? 0f : value > 1f ? 1f : value;
        }
    }

    /// <summary>
    /// The latest controlled application error. The state remains visible to
    /// hosts until it is explicitly cleared or the same operation succeeds.
    /// </summary>
    [Serializable]
    public sealed class TextureLabErrorState
    {
        public static TextureLabErrorState None { get; } =
            new TextureLabErrorState(string.Empty, string.Empty, false);

        public TextureLabErrorState(string code, string message, bool isPersistent)
        {
            Code = code ?? string.Empty;
            Message = message ?? string.Empty;
            IsPersistent = isPersistent;
        }

        public string Code { get; }
        public string Message { get; }
        public bool IsPersistent { get; }
        public bool HasError => !string.IsNullOrEmpty(Code) || !string.IsNullOrEmpty(Message);
    }
}
