using UnityEngine;

namespace BlenderSceneControls
{
    internal enum ModalInputResult { Unhandled, Consumed, Confirm, CancelStage }
    internal enum ModalTermination { Confirm, CancelStage, Abort }

    // A phase may consume a click and replace itself without ending the transaction.
    // Lifecycle abort never passes through the user-input handler.
    internal abstract class ModalPhase
    {
        internal abstract void Move(ModalSession session, Vector2 mouse);
        internal abstract ModalInputResult HandleInput(ModalSession session, ModalInput current);
        internal abstract void Finish(ModalSession session, ModalTermination termination);
        internal abstract SceneStatusContext Presentation(ModalSession session, bool swapYAndZ);
    }

    internal class TransformPhase : ModalPhase
    {
        internal override void Move(ModalSession session, Vector2 mouse)
        {
            session.Grab?.Move(mouse);
            session.RotateScale?.Move(mouse);
            session.Preview();
        }

        internal override ModalInputResult HandleInput(ModalSession session, ModalInput current)
        {
            if (session.Mode != ModalMode.Diagnostic && (current.IsKey || current.IsMouse))
            {
                session.Grab?.Modifiers(current.Shift, current.Control || current.Command);
                session.RotateScale?.Modifiers(current.Shift, current.Control || current.Command);
                if (current.Type == EventType.KeyDown)
                {
                    var axis = SceneControls.AxisForKey(current.Key, SceneControlsConfiguration.Current.SwapYAndZAxisKeys);
                    var mouse = session.StartMouse + session.MouseDelta;
                    if (axis >= 0 && !current.Control && !current.Command && !current.Alt)
                    {
                        session.Grab?.Constrain(axis, current.Shift, mouse);
                        session.RotateScale?.Constrain(axis, current.Shift, mouse);
                    }
                    else if (session.Mode == ModalMode.Rotate && current.Key == KeyCode.R &&
                        !current.Control && !current.Command && !current.Alt && !current.Shift)
                        session.RotateScale.ToggleTrackball(mouse);
                    else if (!current.Control && !current.Command && !current.Alt &&
                        (current.Key == KeyCode.Backspace || current.Character >= '0' && current.Character <= '9' ||
                         current.Character == '-' || current.Character == '.' || current.Character == ','))
                    {
                        session.Grab?.EditNumber(current.Character, current.Key == KeyCode.Backspace, mouse);
                        session.RotateScale?.EditNumber(current.Character, current.Key == KeyCode.Backspace, mouse);
                    }
                    session.Preview();
                }
            }
            if (current.Type == EventType.MouseDown && (current.Button == 0 || current.Button == 1))
                return current.Button == 0 ? ModalInputResult.Confirm : ModalInputResult.CancelStage;
            if (current.Type == EventType.KeyDown)
            {
                if (current.Key == KeyCode.Escape) return ModalInputResult.CancelStage;
                if (current.Key == KeyCode.Return || current.Key == KeyCode.KeypadEnter) return ModalInputResult.Confirm;
                if (session.Mode == ModalMode.Diagnostic && current.Key == KeyCode.RightArrow)
                {
                    session.HasOffset = !session.HasOffset;
                    session.Transaction.PreviewOffset(session.HasOffset ? Vector3.right * 0.25f : Vector3.zero);
                }
            }
            return current.IsMouse || current.IsKey || current.Type == EventType.ScrollWheel
                ? ModalInputResult.Consumed : ModalInputResult.Unhandled;
        }

        internal override void Finish(ModalSession session, ModalTermination termination)
        {
            if (termination == ModalTermination.Confirm) session.Transaction.Confirm();
            else session.Transaction.Cancel();
        }

        internal override SceneStatusContext Presentation(ModalSession session, bool swapYAndZ) =>
            SceneStatusContext.Transform(session.Mode, session.Grab, session.RotateScale, swapYAndZ, session.Transaction is MeshTransformTransaction);
    }

    internal sealed class ExtrudeMovePhase : TransformPhase
    {
        internal override void Finish(ModalSession session, ModalTermination termination)
        {
            var target = (MeshTransformTransaction)session.Transaction;
            if (termination == ModalTermination.CancelStage) target.CancelMove();
            else if (termination == ModalTermination.Confirm) target.Confirm();
            else target.Cancel();
        }

        internal override SceneStatusContext Presentation(ModalSession session, bool swapYAndZ)
        {
            var move = base.Presentation(session, swapYAndZ);
            return new SceneStatusContext(SceneEditorMode.MeshEdit, "Extrude", "Move", "Extrude | " + move.Status,
                "Enter/LMB: Confirm Extrusion · Esc/RMB: Cancel Move (Keeps Extrusion) · XYZ: axis · Shift+axis: plane · Shift: precision · Ctrl: snap · Context loss: full Abort",
                "Enter/LMB: confirm · Esc/RMB: Cancel Move (Keeps Extrusion)\nXYZ: axis · Shift+axis: plane · Context loss: full Abort");
        }
    }
}
