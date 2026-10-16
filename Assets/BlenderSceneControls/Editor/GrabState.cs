using System;
using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace BlenderSceneControls
{
    internal sealed class NumberInput
    {
        internal string Text { get; private set; } = "";
        internal bool Active => Text.Length != 0;
        internal float Value { get; private set; }
        internal bool HasValue { get; private set; }

        internal void Edit(char character, bool backspace = false)
        {
            if (backspace)
                Text = Text.Length == 0 ? "" : Text.Substring(0, Text.Length - 1);
            else if (Text.Length < 48 && (character >= '0' && character <= '9' || character == '-' || character == '.' || character == ','))
            {
                if (character == '-' && Text.Length != 0) return;
                if ((character == '.' || character == ',') && Text.Contains(".")) return;
                Text += character == ',' ? '.' : character;
            }
            if (!Active) { HasValue = false; Value = 0; return; }
            if (float.TryParse(Text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var value) && float.IsFinite(value))
            { Value = value; HasValue = true; }
            // Incomplete/overflow input retains the last finite value until corrected or cleared.
        }
    }

    internal static class GrabMath
    {
        internal static bool PlanePoint(Ray ray, Vector3 pivot, Vector3 normal, out Vector3 point)
        {
            point = default;
            var denominator = Vector3.Dot(ray.direction, normal);
            if (Mathf.Abs(denominator) < 0.02f) return false;
            var distance = Vector3.Dot(pivot - ray.origin, normal) / denominator;
            if (!float.IsFinite(distance) || distance < 0) return false;
            point = ray.GetPoint(distance);
            return Finite(point);
        }

        internal static bool AxisPoint(Ray ray, Vector3 pivot, Vector3 axis, out Vector3 point)
        {
            point = default;
            var cosine = Vector3.Dot(ray.direction, axis);
            var denominator = 1 - cosine * cosine;
            // A view-aligned axis has no mouse solution; numeric input remains available.
            if (denominator < 0.001f) return false;
            var relative = ray.origin - pivot;
            var amount = (Vector3.Dot(relative, axis) - cosine * Vector3.Dot(relative, ray.direction)) / denominator;
            point = pivot + axis * amount;
            return Finite(point);
        }

        internal static bool Finite(Vector3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
        internal static float Snap(float value, float step) => float.IsFinite(step) && step > 0.000001f
            ? Mathf.Round(value / step) * step : value;
    }

    internal sealed class GrabState
    {
        private readonly Camera _camera;
        private readonly Rect _viewport;
        private readonly Vector3 _pivot;
        private readonly Vector3 _viewNormal;
        private readonly Quaternion _localRotation;
        private Vector3 _previousPoint;
        private bool _pointValid;
        private Vector3 _raw;
        private Vector3 _snapOrigin;
        private Vector3 _direction;
        private bool _shift;
        private bool _control;
        private Vector3 _normalConstraint;
        internal readonly NumberInput Number = new NumberInput();
        internal int Axis { get; private set; } = -1;
        internal bool Local { get; private set; }
        internal bool Plane { get; private set; }
        internal Vector3 Pivot => _pivot;
        internal Quaternion Basis => Local ? _localRotation : Quaternion.identity;
        internal bool Precision => _shift;
        internal bool Snap => _control;
        internal Vector3 Offset { get; private set; }
        internal bool MouseAvailable { get; private set; } = true;
        internal string Description => _normalConstraint != Vector3.zero ? "Region Normal" : Axis < 0 ? "View" : (Local ? "Local " : "World ") + (Plane ? "Plane excluding " : "Axis ") + "XYZ"[Axis];

        internal GrabState(SceneView source, Vector3 pivot, Quaternion rotation, Vector2 mouse)
            : this(source.camera, source.cameraViewport, pivot, rotation, mouse)
        {
        }

        internal GrabState(Camera camera, Rect viewport, Vector3 pivot, Quaternion rotation, Vector2 mouse)
        {
            if (!camera || viewport.width <= 0 || viewport.height <= 0)
                throw new ArgumentException("A camera and non-empty viewport are required.");
            _camera = camera;
            _viewport = viewport;
            _pivot = pivot;
            _localRotation = rotation;
            _viewNormal = _camera.transform.forward;
            Rebase(mouse);
        }

        private Vector3 AxisVector => _normalConstraint != Vector3.zero ? _normalConstraint : (Local ? _localRotation : Quaternion.identity) *
            (Axis == 0 ? Vector3.right : Axis == 1 ? Vector3.up : Vector3.forward);

        private bool Point(Vector2 mouse, out Vector3 point)
        {
            if ((Axis >= 0 || _normalConstraint != Vector3.zero) && !Plane && Mathf.Abs(Vector3.Dot(AxisVector, _viewNormal)) > 0.9995f)
            {
                point = default;
                return false;
            }
            var ray = _camera.ViewportPointToRay(new Vector3((mouse.x - _viewport.x) / _viewport.width,
                1 - (mouse.y - _viewport.y) / _viewport.height, 0));
            return (Axis >= 0 || _normalConstraint != Vector3.zero) && !Plane ? GrabMath.AxisPoint(ray, _pivot, AxisVector, out point) :
                GrabMath.PlanePoint(ray, _pivot, Axis >= 0 ? AxisVector : _viewNormal, out point);
        }

        private Vector3 Project(Vector3 value)
        {
            if (_normalConstraint != Vector3.zero) return Vector3.Project(value, _normalConstraint);
            if (Axis < 0) return Vector3.ProjectOnPlane(value, _viewNormal);
            return Plane ? Vector3.ProjectOnPlane(value, AxisVector) : Vector3.Project(value, AxisVector);
        }

        private void Rebase(Vector2 mouse)
        {
            _pointValid = Point(mouse, out _previousPoint);
            MouseAvailable = _pointValid;
        }

        internal void Constrain(int axis, bool plane, Vector2 mouse)
        {
            _normalConstraint = Vector3.zero;
            if (Axis != axis || Plane != plane) { Axis = axis; Local = false; Plane = plane; }
            else if (!Local) Local = true;
            else { Axis = -1; Local = false; Plane = false; }
            _raw = Project(Offset);
            Offset = _snapOrigin = _raw;
            _direction = _raw.normalized;
            Rebase(mouse);
            ApplyNumber();
        }

        internal void ConstrainNormal(Vector3 normal, Vector2 mouse)
        {
            if (!GrabMath.Finite(normal) || normal.sqrMagnitude < 1e-12f)
                throw new ArgumentException("Selected region has no stable normal; select a region with a nonzero area normal.");
            _normalConstraint = normal.normalized;
            Rebase(mouse);
        }

        internal void Modifiers(bool shift, bool control)
        {
            if (_control != control)
            {
                // Start/stop snapping at the current value, without rounding the already-applied offset.
                _raw = _snapOrigin = Offset;
            }
            _shift = shift;
            _control = control;
        }

        internal void Move(Vector2 mouse)
        {
            var valid = Point(mouse, out var point);
            MouseAvailable = valid;
            if (valid && _pointValid && !Number.Active)
            {
                _raw += (point - _previousPoint) * (_shift ? 0.1f : 1);
                if (_raw.sqrMagnitude > 0.00000001f) _direction = _raw.normalized;
                Offset = _control ? _snapOrigin + Snapped(_raw - _snapOrigin) : _raw;
            }
            else if (valid && Number.Active && _direction == Vector3.zero)
            {
                var direction = point - _previousPoint;
                if (_pointValid && direction.sqrMagnitude > 0.00000001f) _direction = direction.normalized;
            }
            _previousPoint = point;
            _pointValid = valid;
            ApplyNumber();
        }

        private Vector3 Snapped(Vector3 value)
        {
            var steps = EditorSnapSettings.move;
            if ((Axis >= 0 || _normalConstraint != Vector3.zero) && !Plane)
            {
                var step = Axis == 0 ? steps.x : Axis == 1 ? steps.y : steps.z;
                return AxisVector * GrabMath.Snap(Vector3.Dot(value, AxisVector), step);
            }
            var rotation = Axis >= 0 && Local ? _localRotation : Quaternion.identity;
            var basis = Quaternion.Inverse(rotation) * value;
            basis = new Vector3(GrabMath.Snap(basis.x, steps.x), GrabMath.Snap(basis.y, steps.y), GrabMath.Snap(basis.z, steps.z));
            return Project(rotation * basis);
        }

        internal void EditNumber(char character, bool backspace, Vector2 mouse)
        {
            var wasActive = Number.Active;
            Number.Edit(character, backspace);
            if (wasActive && !Number.Active)
            {
                _raw = _snapOrigin = Offset;
                Rebase(mouse);
            }
            ApplyNumber();
        }

        // A dimensionless rail phase converts its numeric factor back to a world
        // offset when the buffer clears, then resumes the same pointer solver.
        internal void RebaseOffset(Vector3 offset, Vector2 mouse)
        {
            if (!GrabMath.Finite(offset)) throw new ArgumentException("Offset must be finite.");
            Offset = _raw = _snapOrigin = offset;
            Rebase(mouse);
        }

        private void ApplyNumber()
        {
            if (!Number.Active || !Number.HasValue) return;
            var direction = (Axis >= 0 || _normalConstraint != Vector3.zero) && !Plane ? AxisVector : _direction;
            if (direction != Vector3.zero) Offset = direction * Number.Value;
        }
    }
}
