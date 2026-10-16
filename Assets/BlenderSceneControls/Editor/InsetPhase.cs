using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace BlenderSceneControls
{
    internal sealed class InsetPhase : ModalPhase
    {
        private GrabState _solver;
        private readonly Vector3 _axis;
        private readonly float _localPerWorld;
        private bool _depth, _snap;
        private MeshInsetTransaction Target(ModalSession session) => (MeshInsetTransaction)session.Transaction;
        internal InsetPhase(ModalSession session, Vector2 mouse)
        {
            _axis = session.Source.camera.transform.right;
            _localPerWorld = Target(session).LocalPerWorld(_axis);
            Rebase(session, mouse);
        }
        private void Rebase(ModalSession session, Vector2 mouse)
        {
            var target = Target(session);
            _solver = new GrabState(session.Source.camera, session.Viewport, target.Pivot, target.Orientation, mouse);
            _solver.ConstrainNormal(_axis, mouse);
            _solver.RebaseOffset(_axis * ((_depth ? target.Depth : target.Thickness) / _localPerWorld), mouse);
        }
        private void Preview(ModalSession session)
        {
            var target = Target(session);
            var value = _solver.Number.Active ? (_solver.Number.HasValue ? _solver.Number.Value : _depth ? target.Depth : target.Thickness) :
                Vector3.Dot(_solver.Offset, _axis) * _localPerWorld;
            if (!_solver.Number.Active && _snap) value = GrabMath.Snap(value, EditorSnapSettings.move.x);
            target.Preview(_depth ? target.Thickness : value, _depth ? value : target.Depth, target.Options);
        }
        internal override void Move(ModalSession session, Vector2 mouse) { _solver.Move(mouse); Preview(session); }
        internal override ModalInputResult HandleInput(ModalSession session, ModalInput current)
        {
            var target = Target(session);
            if (current.IsMouse || current.IsKey) { _solver.Modifiers(current.Shift, false); _snap = current.Control || current.Command; }
            if (current.Type == EventType.KeyDown)
            {
                if (current.Key == KeyCode.Escape) return ModalInputResult.CancelStage;
                if (current.Key == KeyCode.Return || current.Key == KeyCode.KeypadEnter)
                    return target.CanConfirm ? ModalInputResult.Confirm : ModalInputResult.Consumed;
                if (!current.Control && !current.Command && !current.Alt)
                {
                    var mouse = session.StartMouse + session.MouseDelta;
                    if (current.Key == KeyCode.D) { _depth = !_depth; Rebase(session, mouse); }
                    else if (current.Key == KeyCode.I || current.Key == KeyCode.B || current.Key == KeyCode.E || current.Key == KeyCode.O)
                    {
                        var o = target.Options;
                        var options = new InsetOptions(current.Key == KeyCode.I ? !o.Individual : o.Individual,
                            current.Key == KeyCode.B ? !o.Boundary : o.Boundary, current.Key == KeyCode.E ? !o.Even : o.Even,
                            current.Key == KeyCode.O ? !o.Outward : o.Outward);
                        target.Preview(target.Thickness, target.Depth, options);
                        return ModalInputResult.Consumed;
                    }
                    else if (current.Key == KeyCode.Backspace || current.Character >= '0' && current.Character <= '9' ||
                        current.Character == '-' || current.Character == '.' || current.Character == ',')
                    {
                        var hadNumber = _solver.Number.Active;
                        _solver.EditNumber(current.Character, current.Key == KeyCode.Backspace, mouse);
                        if (hadNumber && !_solver.Number.Active)
                            _solver.RebaseOffset(_axis * ((_depth ? target.Depth : target.Thickness) / _localPerWorld), mouse);
                    }
                }
            }
            if (current.Type == EventType.MouseDown && (current.Button == 0 || current.Button == 1))
                return current.Button == 1 ? ModalInputResult.CancelStage : target.CanConfirm ? ModalInputResult.Confirm : ModalInputResult.Consumed;
            Preview(session);
            return current.IsMouse || current.IsKey || current.Type == EventType.ScrollWheel ? ModalInputResult.Consumed : ModalInputResult.Unhandled;
        }
        internal override void Finish(ModalSession session, ModalTermination termination)
        {
            if (termination == ModalTermination.Confirm) Target(session).Confirm();
            else Target(session).Cancel();
        }
        internal override SceneStatusContext Presentation(ModalSession session, bool swapYAndZ)
        {
            var t = Target(session); var o = t.Options;
            return new SceneStatusContext(SceneEditorMode.MeshEdit, "Inset", o.Individual ? "Individual" : "Region",
                "Thickness " + t.Thickness.ToString("0.###", CultureInfo.InvariantCulture) + " · Depth " + t.Depth.ToString("0.###", CultureInfo.InvariantCulture) +
                " · Input " + (_depth ? "Depth " : "Thickness ") + _solver.Number.Text + " · Boundary " + (o.Individual || o.Boundary) + " · Even " + o.Even +
                " · Outset " + o.Outward + " · " + t.Message,
                "Mouse/number: local thickness/depth · I: Region/Individual · B: Boundary · E: Even · O: Outset (unavailable) · D: Edit Depth/Thickness · Shift: precision · Ctrl: snap · Enter/LMB: Confirm valid · Esc/RMB/context loss: full Cancel",
                "I: Region/Individual · B: Boundary · E: Even · D: Depth/Thickness\nEnter/LMB: confirm valid · Esc/RMB: full cancel");
        }
    }
}
