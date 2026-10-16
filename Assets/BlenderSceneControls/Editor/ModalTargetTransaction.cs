using UnityEngine;

namespace BlenderSceneControls
{
    // The implementation owns its snapshot and Undo policy. Solvers supply absolute results.
    internal abstract class ModalTargetTransaction
    {
        internal abstract bool IsValid { get; }
        internal abstract Vector3 Pivot { get; }
        internal abstract Quaternion Orientation { get; }
        internal abstract void PreviewOffset(Vector3 offset);
        internal abstract void PreviewRotation(Vector3 pivot, Quaternion rotation);
        internal abstract void PreviewScale(Vector3 pivot, Quaternion basis, Vector3 factors);
        internal abstract bool Confirm();
        internal abstract void Cancel();
    }
}
