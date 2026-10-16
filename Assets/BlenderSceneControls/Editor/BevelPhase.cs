using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace BlenderSceneControls
{
    internal sealed class BevelPhase : ModalPhase
    {
        private readonly GrabState _solver;
        private readonly Vector3 _axis;
        private readonly float _localPerWorld;
        private bool _snap;
        private MeshBevelTransaction Target(ModalSession session) => (MeshBevelTransaction)session.Transaction;
        internal BevelPhase(ModalSession session, Vector2 mouse)
        {
            _axis = session.Source.camera.transform.right;
            _localPerWorld = Target(session).LocalPerWorld(_axis);
            _solver = new GrabState(session.Source.camera, session.Viewport, Target(session).Pivot, Target(session).Orientation, mouse);
            _solver.ConstrainNormal(_axis, mouse);
            _solver.RebaseOffset(_axis * (Target(session).Width / _localPerWorld), mouse);
        }
        private void Preview(ModalSession session)
        {
            var target = Target(session);
            var width = _solver.Number.Active ? (_solver.Number.HasValue ? _solver.Number.Value : target.Width) :
                Vector3.Dot(_solver.Offset, _axis) * _localPerWorld;
            if (!_solver.Number.Active && _snap) width = GrabMath.Snap(width, EditorSnapSettings.move.x);
            target.Preview(width, target.Segments);
        }
        internal override void Move(ModalSession session, Vector2 mouse)
        {
            _solver.Move(mouse); Preview(session);
        }
        internal override ModalInputResult HandleInput(ModalSession session, ModalInput current)
        {
            var target = Target(session);
            if (current.IsMouse || current.IsKey)
            {
                _solver.Modifiers(current.Shift, false);
                _snap = current.Control || current.Command;
            }
            if (current.Type == EventType.ScrollWheel)
            {
                if (current.Scroll.y != 0)
                    target.Preview(target.Width, Mathf.Clamp(target.Segments + (current.Scroll.y < 0 ? 1 : -1), 1, MeshBevel.MaxSegments));
                return ModalInputResult.Consumed;
            }
            if (current.Type == EventType.KeyDown)
            {
                if (current.Key == KeyCode.Escape) return ModalInputResult.CancelStage;
                if (current.Key == KeyCode.Return || current.Key == KeyCode.KeypadEnter)
                    return target.CanConfirm ? ModalInputResult.Confirm : ModalInputResult.Consumed;
                if (!current.Control && !current.Command && !current.Alt &&
                    (current.Key == KeyCode.Backspace || current.Character >= '0' && current.Character <= '9' ||
                    current.Character == '-' || current.Character == '.' || current.Character == ','))
                {
                    var mouse = session.StartMouse + session.MouseDelta;
                    var hadNumber = _solver.Number.Active;
                    _solver.EditNumber(current.Character, current.Key == KeyCode.Backspace, mouse);
                    if (hadNumber && !_solver.Number.Active)
                        _solver.RebaseOffset(_axis * (target.Width / _localPerWorld), mouse);
                }
            }
            if (current.Type == EventType.MouseDown && (current.Button == 0 || current.Button == 1))
                return current.Button == 1 ? ModalInputResult.CancelStage : target.CanConfirm ? ModalInputResult.Confirm : ModalInputResult.Consumed;
            Preview(session);
            return current.IsMouse || current.IsKey ? ModalInputResult.Consumed : ModalInputResult.Unhandled;
        }
        internal override void Finish(ModalSession session, ModalTermination termination)
        {
            if (termination == ModalTermination.Confirm) Target(session).Confirm();
            else Target(session).Cancel();
        }
        internal override SceneStatusContext Presentation(ModalSession session, bool swapYAndZ)
        {
            var t = Target(session);
            return new SceneStatusContext(SceneEditorMode.MeshEdit, "Bevel", t.VertexOnly ? "Vertices" : "Edges",
                "Offset " + t.Width.ToString("0.###", CultureInfo.InvariantCulture) + " · Segments " + t.Segments +
                " · Profile 0.5 · Overlap: refuse · Input " + _solver.Number.Text + " · " + t.Message,
                "Mouse/number: local offset · Wheel: 1–16 segments (budget applies) · Shift: precision · Ctrl: snap · Enter/LMB: Confirm valid · Esc/RMB/context loss: full Cancel",
                "Mouse/number: offset · Wheel: segments · Profile 0.5 · Overlap refused\nEnter/LMB: confirm valid · Esc/RMB: full cancel");
        }
    }
}
