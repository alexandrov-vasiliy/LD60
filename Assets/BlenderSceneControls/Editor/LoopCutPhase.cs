using System.Globalization;
using UnityEngine;

namespace BlenderSceneControls
{
    internal sealed class LoopCutPreviewPhase : ModalPhase
    {
        private NumberInput _number = new NumberInput();
        private bool _validNumber = true;
        private MeshLoopCutTransaction Target(ModalSession session) => (MeshLoopCutTransaction)session.Transaction;
        internal override void Move(ModalSession session, Vector2 mouse) { }
        internal override ModalInputResult HandleInput(ModalSession session, ModalInput current)
        {
            var target = Target(session);
            if (current.Type == EventType.MouseMove || current.Type == EventType.MouseDrag)
                target.Hover(target.Pick(current.MousePosition));
            if (current.Type == EventType.ScrollWheel)
            {
                _number = new NumberInput(); _validNumber = true;
                target.SetCount(Mathf.Clamp(target.Count + (current.Scroll.y < 0 ? 1 : current.Scroll.y > 0 ? -1 : 0), 1, MeshLoopCut.MaxCuts));
            }
            if (current.Type == EventType.KeyDown)
            {
                if (current.Key == KeyCode.Escape) return ModalInputResult.CancelStage;
                if (!current.Control && !current.Command && !current.Alt && (current.Key == KeyCode.Backspace || current.Character >= '0' && current.Character <= '9'))
                {
                    _number.Edit(current.Character, current.Key == KeyCode.Backspace);
                    _validNumber = !_number.Active || int.TryParse(_number.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed >= 1 && parsed <= MeshLoopCut.MaxCuts;
                    if (int.TryParse(_number.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var count)) target.SetCount(count);
                    else if (!_number.Active) target.SetCount(1);
                }
            }
            if (current.Type == EventType.MouseDown)
            {
                if (current.Button == 1) return ModalInputResult.CancelStage;
                if (current.Button == 0 && _validNumber && target.BeginSlide())
                    session.SetPhase(new LoopCutSlidePhase(session, session.StartMouse + session.MouseDelta));
            }
            return current.IsMouse || current.IsKey || current.Type == EventType.ScrollWheel ? ModalInputResult.Consumed : ModalInputResult.Unhandled;
        }
        internal override void Finish(ModalSession session, ModalTermination termination) => Target(session).Cancel();
        internal override SceneStatusContext Presentation(ModalSession session, bool swapYAndZ) =>
            new SceneStatusContext(SceneEditorMode.MeshEdit, "Loop Cut", "Preview", Target(session).Message + " · Input: " + _number.Text,
                "Hover: Select Ring · Wheel/positive integer: Cuts (1–32) · LMB: Cut and Slide · Esc/RMB: Cancel Preview · Context loss: full Abort",
                "Hover: ring · Wheel/number: cuts · LMB: Cut and Slide\nEsc/RMB: Cancel Preview");
    }

    internal sealed class LoopCutSlidePhase : ModalPhase
    {
        private readonly GrabState _solver;
        private readonly Vector3 _rail;
        private bool _snap;
        internal LoopCutSlidePhase(ModalSession session, Vector2 mouse)
        {
            var target = (MeshLoopCutTransaction)session.Transaction;
            _rail = target.RailDirection;
            _solver = new GrabState(session.Source.camera, session.Viewport, target.Pivot, target.Orientation, mouse);
            _solver.ConstrainNormal(_rail.normalized, mouse);
        }
        private void Preview(ModalSession session)
        {
            var factor = _solver.Number.Active && _solver.Number.HasValue ? _solver.Number.Value :
                Vector3.Dot(_solver.Offset, _rail) / _rail.sqrMagnitude * (((MeshLoopCutTransaction)session.Transaction).Count + 1);
            if (_snap) factor = GrabMath.Snap(factor, .1f);
            ((MeshLoopCutTransaction)session.Transaction).Slide(factor);
        }
        internal override void Move(ModalSession session, Vector2 mouse) { _solver.Move(mouse); Preview(session); }
        internal override ModalInputResult HandleInput(ModalSession session, ModalInput current)
        {
            if (current.IsMouse || current.IsKey)
            {
                _solver.Modifiers(current.Shift, false);
                _snap = current.Control || current.Command;
            }
            if (current.Type == EventType.KeyDown)
            {
                if (current.Key == KeyCode.Escape) return ModalInputResult.CancelStage;
                if (current.Key == KeyCode.Return || current.Key == KeyCode.KeypadEnter) return ModalInputResult.Confirm;
                if (!current.Control && !current.Command && !current.Alt &&
                    (current.Key == KeyCode.Backspace || current.Character >= '0' && current.Character <= '9' || current.Character == '-' || current.Character == '.' || current.Character == ','))
                {
                    var hadNumber = _solver.Number.Active;
                    var mouse = session.StartMouse + session.MouseDelta;
                    _solver.EditNumber(current.Character, current.Key == KeyCode.Backspace, mouse);
                    if (hadNumber && !_solver.Number.Active)
                    {
                        var target = (MeshLoopCutTransaction)session.Transaction;
                        _solver.RebaseOffset(_rail * (target.Factor / (target.Count + 1)), mouse);
                    }
                }
            }
            if (current.Type == EventType.MouseDown && (current.Button == 0 || current.Button == 1))
                return current.Button == 0 ? ModalInputResult.Confirm : ModalInputResult.CancelStage;
            Preview(session);
            return current.IsMouse || current.IsKey || current.Type == EventType.ScrollWheel ? ModalInputResult.Consumed : ModalInputResult.Unhandled;
        }
        internal override void Finish(ModalSession session, ModalTermination termination)
        {
            var target = (MeshLoopCutTransaction)session.Transaction;
            if (termination == ModalTermination.Abort || !target.IsValid) { target.Cancel(); return; }
            if (termination == ModalTermination.CancelStage) target.Slide(0);
            target.Confirm();
        }
        internal override SceneStatusContext Presentation(ModalSession session, bool swapYAndZ) =>
            new SceneStatusContext(SceneEditorMode.MeshEdit, "Loop Cut", "Slide", "Slide " + ((MeshLoopCutTransaction)session.Transaction).Factor.ToString("0.###", CultureInfo.InvariantCulture) + " · " + _solver.Number.Text,
                "Mouse/number: Slide Factor (-1…1, clamped) · Shift: precision · Ctrl: snap 0.1 · Enter/LMB: Confirm · Esc/RMB: Center (Keeps Cuts) · Context loss: full Abort",
                "Mouse/number: factor · Shift: precision · Ctrl: snap\nEnter/LMB: confirm · Esc/RMB: Center (Keeps Cuts)");
    }
}
