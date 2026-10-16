using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BlenderSceneControls
{
    internal enum ModalMode { Diagnostic, Grab, Rotate, Scale }
    internal enum SceneEditorMode { Object, MeshEdit }

    internal sealed class ModalSession
    {
        internal readonly SceneView Source;
        internal readonly EditorWindow InitialFocus;
        internal readonly int[] SelectionIds;
        internal readonly Object ActiveObject;
        internal readonly ModalTargetTransaction Transaction;
        internal ModalPhase Phase { get; private set; }
        internal readonly int InitialUndoGroup;
        internal readonly ModalMode Mode;
        internal readonly GrabState Grab;
        internal readonly RotateScaleState RotateScale;
        internal readonly Rect WindowRect;
        internal readonly Rect Viewport;
        internal readonly Rect PixelBounds;
        internal readonly float PixelsPerPoint;
        internal readonly Vector2 StartCursor;
        internal readonly Vector2 StartMouse;
        internal Vector2 MouseDelta { get; private set; }
        internal Vector2 PreviousCursor;
        internal readonly int Control;
        internal readonly bool PreviousWantsMouseMove;
        internal readonly bool PreviousToolsHidden;
        internal readonly Tool PreviousTool;
        internal bool HasOffset;

        internal ModalSession(SceneView source, int control, Vector2 mouse, Vector2 cursor, float scale,
            ModalMode mode = ModalMode.Diagnostic, SceneCreation creation = null)
            : this(source, control, mouse, cursor, scale, mode,
                new TransformTransaction(SceneControls.SelectedTransforms,
                    creation == null ? "Blender Scene Controls " + mode : "Blender Scene Controls Create and Grab",
                    creation, Selection.activeTransform, Tools.pivotMode == PivotMode.Center), source.cameraViewport)
        {
        }

        internal ModalSession(SceneView source, int control, Vector2 mouse, Vector2 cursor, float scale,
            ModalMode mode, ModalTargetTransaction target, Rect viewport)
        {
            Source = source;
            InitialFocus = EditorWindow.focusedWindow;
            SelectionIds = Selection.objects.Select(obj => obj.GetInstanceID()).OrderBy(id => id).ToArray();
            ActiveObject = Selection.activeObject;
            Mode = mode;
            Transaction = target ?? throw new System.ArgumentNullException(nameof(target));
            Phase = new TransformPhase();
            InitialUndoGroup = Undo.GetCurrentGroup();
            WindowRect = source.position;
            Viewport = viewport;
            PixelsPerPoint = scale;
            StartMouse = mouse;
            StartCursor = PreviousCursor = cursor;
            // Experimental physical origin calibration, retained explicitly as an S01 risk.
            PixelBounds = new Rect(cursor - (mouse - Viewport.position) * scale, Viewport.size * scale);
            Control = control;
            PreviousWantsMouseMove = source.wantsMouseMove;
            PreviousToolsHidden = Tools.hidden;
            PreviousTool = Tools.current;
            if (mode != ModalMode.Diagnostic)
            {
                var pivot = Transaction.Pivot;
                var orientation = Transaction.Orientation;
                if (mode == ModalMode.Grab) Grab = new GrabState(source.camera, Viewport, pivot, orientation, mouse);
                else RotateScale = new RotateScaleState(source.camera, Viewport, pivot, orientation, mouse, mode == ModalMode.Scale);
            }
        }

        internal void SetPhase(ModalPhase phase)
        {
            Phase = phase ?? throw new System.ArgumentNullException(nameof(phase));
        }

        internal void Finish(ModalTermination termination) => Phase.Finish(this, termination);

        internal void Move(Vector2 mouse)
        {
            Phase.Move(this, mouse);
        }

        internal void Preview()
        {
            if (Grab != null) Transaction.PreviewOffset(Grab.Offset);
            else if (RotateScale != null)
            {
                if (Mode == ModalMode.Scale) Transaction.PreviewScale(RotateScale.Pivot, RotateScale.Basis, RotateScale.Factors);
                else Transaction.PreviewRotation(RotateScale.Pivot, RotateScale.Rotation);
            }
        }

        internal bool SelectionMatches => ActiveObject == Selection.activeObject &&
            SelectionIds.SequenceEqual(Selection.objects.Select(obj => obj.GetInstanceID()).OrderBy(id => id));

        internal void Sample(Vector2 cursor)
        {
            MouseDelta += (cursor - PreviousCursor) / PixelsPerPoint;
            PreviousCursor = cursor;
        }
    }
}
