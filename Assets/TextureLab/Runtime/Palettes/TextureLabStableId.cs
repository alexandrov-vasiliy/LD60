using UnityEngine;

namespace TextureLab.Runtime
{
    /// <summary>
    /// Produces a stable 128-bit identifier for authored built-ins whose asset
    /// does not yet carry an explicit serialized ID. User-authored objects use
    /// a random GUID; this helper is only a deterministic migration seam.
    /// </summary>
    public static class TextureLabStableId
    {
        public static string FromText(string value)
        {
            return Hash128.Compute(value ?? string.Empty).ToString();
        }
    }
}
