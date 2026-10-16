using UnityEngine;

namespace TextureLab.Runtime
{
    internal static class RuntimeObjectLifetime
    {
        internal static void Destroy(Object target)
        {
            if (target == null)
                return;

            if (Application.isPlaying)
                Object.Destroy(target);
            else
                Object.DestroyImmediate(target);
        }
    }
}
