using System;
using UnityEditor;
using UnityEngine;

namespace BlenderSceneControls
{
    internal sealed class RotateScaleState
    {
        private readonly Camera _camera;
        private readonly Rect _viewport;
        private readonly Vector3 _pivot;
        private readonly Quaternion _localRotation;
        private readonly Vector2 _screenPivot;
        private Vector2 _previousMouse;
        private Vector2 _scaleDirection;
        private float _scaleRadius;
        private float _raw;
        private float _snapOrigin;
        private Quaternion _rawRotation = Quaternion.identity;
        private Quaternion _snapRotation = Quaternion.identity;
        private Vector3 _trackAxis;
        private bool _shift;
        private bool _control;
        internal readonly NumberInput Number = new NumberInput();
        internal readonly bool IsScale;
        internal int Axis { get; private set; } = -1;
        internal bool Local { get; private set; }
        internal bool Plane { get; private set; }
        internal bool Trackball { get; private set; }
        internal bool Precision => _shift;
        internal bool Snap => _control;
        internal float Value { get; private set; }
        internal Quaternion Rotation { get; private set; } = Quaternion.identity;
        internal Vector3 Pivot => _pivot;
        internal Quaternion Basis => Local ? _localRotation : Quaternion.identity;
        internal bool WaitingForDirection => Trackball && Number.Active && _trackAxis == Vector3.zero;
        internal string Description => Trackball ? "Trackball" : Axis < 0 ? (IsScale ? "Uniform" : "View") :
            (Local ? "Local " : "World ") + (Plane && IsScale ? "Plane excluding " : "Axis ") + "XYZ"[Axis];
        internal Vector3 Factors
        {
            get
            {
                if (Axis < 0) return Vector3.one * Value;
                var result = Plane ? Vector3.one * Value : Vector3.one;
                result[Axis] = Plane ? 1 : Value;
                return result;
            }
        }

        internal RotateScaleState(Camera camera, Rect viewport, Vector3 pivot, Quaternion localRotation,
            Vector2 mouse, bool scale)
        {
            if (!camera || viewport.width <= 0 || viewport.height <= 0)
                throw new ArgumentException("A camera and non-empty viewport are required.");
            _camera = camera;
            _viewport = viewport;
            _pivot = pivot;
            _localRotation = localRotation;
            IsScale = scale;
            Value = _raw = _snapOrigin = scale ? 1 : 0;
            var projected = camera.WorldToViewportPoint(pivot);
            _screenPivot = projected.z > 0 && GrabMath.Finite(projected)
                ? viewport.position + new Vector2(projected.x * viewport.width, (1 - projected.y) * viewport.height)
                : viewport.center;
            Rebase(mouse);
        }

        private Vector3 RotationAxis => Axis < 0 ? _camera.transform.forward : Basis *
            (Axis == 0 ? Vector3.right : Axis == 1 ? Vector3.up : Vector3.forward);

        private void Rebase(Vector2 mouse)
        {
            _previousMouse = mouse;
            var radial = mouse - _screenPivot;
            _scaleRadius = Mathf.Max(20, radial.magnitude);
            _scaleDirection = radial.sqrMagnitude >= 4 ? radial.normalized : Vector2.right;
        }

        internal void Constrain(int axis, bool plane, Vector2 mouse)
        {
            // Rotation uses the chosen plane normal, so Shift does not create a separate constraint cycle.
            plane = IsScale && plane;
            if (Axis != axis || Plane != plane) { Axis = axis; Plane = plane; Local = false; }
            else if (!Local) Local = true;
            else { Axis = -1; Local = false; Plane = false; }
            if (Trackball) { Rotation.ToAngleAxis(out var angle, out _trackAxis); Value = angle; }
            Trackball = false;
            _raw = _snapOrigin = Value;
            Rebase(mouse);
            Apply();
        }

        internal void ToggleTrackball(Vector2 mouse)
        {
            if (IsScale) return;
            Trackball = !Trackball;
            Axis = -1;
            Local = Plane = false;
            if (Trackball)
            {
                _rawRotation = _snapRotation = Rotation;
                if (Quaternion.Angle(Rotation, Quaternion.identity) > 0.0001f)
                    Rotation.ToAngleAxis(out _, out _trackAxis);
            }
            else
            {
                Rotation.ToAngleAxis(out var angle, out _);
                _raw = _snapOrigin = Value = angle;
            }
            Rebase(mouse);
            Apply();
        }

        internal void Modifiers(bool shift, bool control)
        {
            if (_control != control)
            {
                _raw = _snapOrigin = Value;
                _rawRotation = _snapRotation = Rotation;
            }
            _shift = shift;
            _control = control;
        }

        internal void Move(Vector2 mouse)
        {
            var delta = mouse - _previousMouse;
            var precision = _shift ? 0.1f : 1;
            if (Trackball)
            {
                var axis = _camera.transform.up * delta.x + _camera.transform.right * delta.y;
                if (axis.sqrMagnitude > 0.000001f)
                {
                    if (!Number.Active)
                    {
                        _rawRotation = Quaternion.AngleAxis(delta.magnitude * 0.5f * precision, axis.normalized) * _rawRotation;
                        _rawRotation.ToAngleAxis(out _, out _trackAxis);
                    }
                    else if (_trackAxis == Vector3.zero) _trackAxis = axis.normalized;
                }
            }
            else if (!Number.Active)
            {
                var increment = IsScale ? Vector2.Dot(delta, _scaleDirection) / _scaleRadius : RotationDelta(mouse);
                var candidate = _raw + increment * precision;
                if (float.IsFinite(candidate)) _raw = candidate;
            }
            _previousMouse = mouse;
            Apply();
        }

        private float RotationDelta(Vector2 mouse)
        {
            var axis = RotationAxis;
            Ray RayAt(Vector2 point) => _camera.ViewportPointToRay(new Vector3(
                (point.x - _viewport.x) / _viewport.width, 1 - (point.y - _viewport.y) / _viewport.height, 0));
            if (GrabMath.PlanePoint(RayAt(_previousMouse), _pivot, axis, out var before) &&
                GrabMath.PlanePoint(RayAt(mouse), _pivot, axis, out var after) &&
                (before - _pivot).sqrMagnitude > 0.000001f && (after - _pivot).sqrMagnitude > 0.000001f &&
                (_previousMouse - _screenPivot).sqrMagnitude >= 16 && (mouse - _screenPivot).sqrMagnitude >= 16)
                return Vector3.SignedAngle(before - _pivot, after - _pivot, axis);
            // Near the pivot or an edge-on plane there is no stable polar angle.
            return (mouse.x - _previousMouse.x) * 0.5f;
        }

        internal void EditNumber(char character, bool backspace, Vector2 mouse)
        {
            var wasActive = Number.Active;
            Number.Edit(character, backspace);
            if (wasActive && !Number.Active)
            {
                _raw = _snapOrigin = Value;
                _rawRotation = _snapRotation = Rotation;
                Rebase(mouse);
            }
            Apply();
        }

        private void Apply()
        {
            if (Trackball)
            {
                if (Number.Active)
                {
                    if (Number.HasValue && _trackAxis != Vector3.zero)
                        Rotation = Quaternion.AngleAxis(Number.Value, _trackAxis);
                }
                else if (_control)
                {
                    var delta = _rawRotation * Quaternion.Inverse(_snapRotation);
                    delta.ToAngleAxis(out var angle, out var axis);
                    if (angle > 180) angle -= 360;
                    Rotation = Quaternion.AngleAxis(GrabMath.Snap(angle, EditorSnapSettings.rotate), axis) * _snapRotation;
                }
                else Rotation = _rawRotation;
                return;
            }
            if (Number.Active)
            {
                if (Number.HasValue) Value = Number.Value;
            }
            else Value = _control ? _snapOrigin + GrabMath.Snap(_raw - _snapOrigin,
                IsScale ? EditorSnapSettings.scale : EditorSnapSettings.rotate) : _raw;
            if (!IsScale) Rotation = Quaternion.AngleAxis(Value, RotationAxis);
        }
    }
}
