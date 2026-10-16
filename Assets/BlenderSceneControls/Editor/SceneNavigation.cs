using System;
using UnityEditor;
using UnityEngine;

namespace BlenderSceneControls
{
    internal enum NavigationGesture { None, Orbit, Pan, Zoom }

    internal static class NavigationMath
    {
        internal static NavigationGesture Gesture(int button, bool shift, bool control, bool alt,
            bool command, bool emulate, bool rotationLocked)
        {
            if (button == 2 && !alt && !control && !command)
                return shift || rotationLocked ? NavigationGesture.Pan : NavigationGesture.Orbit;
            if (button != 0 || !emulate || !alt || command) return NavigationGesture.None;
            // Ctrl takes priority when both Ctrl and Shift are held. Never process two gestures.
            if (control) return NavigationGesture.Zoom;
            return shift || rotationLocked ? NavigationGesture.Pan : NavigationGesture.Orbit;
        }

        internal static Vector3 PlaneOffset(Quaternion rotation, Vector2 normalized, float halfHeight, float aspect)
        {
            return rotation * new Vector3(normalized.x * halfHeight * aspect * 2,
                -normalized.y * halfHeight * 2, 0);
        }

        internal static Quaternion Orbit(Quaternion rotation, Vector2 delta)
        {
            return (Quaternion.AngleAxis(delta.x * 0.25f, Vector3.up) * rotation *
                Quaternion.AngleAxis(delta.y * 0.25f, Vector3.right)).normalized;
        }

        internal static float ZoomSize(float size, float amount)
        {
            if (!float.IsFinite(size) || size <= 0 || !float.IsFinite(amount)) return size;
            return Mathf.Clamp(size * Mathf.Exp(Mathf.Clamp(amount, -20, 20)), 0.0001f, 1000000);
        }

        internal static Vector3 ZoomPivot(Vector3 pivot, Vector3 anchor, float oldSize, float newSize)
        {
            return anchor + (pivot - anchor) * (newSize / oldSize);
        }
    }

    // Own only our drag. Native RMB fly mode and unhandled gestures retain Unity ownership.
    internal static class SceneNavigation
    {
        private const string ReleaseCommand = "BSC.ReleaseNavigation";
        private static SceneView _source;
        private static int _control;
        private static int _button;
        private static int _releaseControl;
        private static NavigationGesture _gesture;
        private static Rect _windowRect;
        private static Rect _viewport;
        private static float _scale;
        private static float _halfHeightPerSize;
        private static bool _in2DMode;
        private static bool _rotationLocked;
        private static Vector3 _anchor;
        internal static bool Active => _source;

        internal static bool ReleaseEvent(Event current)
        {
            if (current.type != EventType.ExecuteCommand || current.commandName != ReleaseCommand) return false;
            if (GUIUtility.hotControl == _releaseControl) GUIUtility.hotControl = 0;
            current.Use();
            return true;
        }

        internal static void End(bool sourceGUI = false)
        {
            var source = _source;
            var control = _control;
            _source = null;
            _control = 0;
            _gesture = NavigationGesture.None;
            if (!source) return;
            if (sourceGUI)
            {
                if (GUIUtility.hotControl == control) GUIUtility.hotControl = 0;
            }
            else
            {
                _releaseControl = control;
                try { source.SendEvent(EditorGUIUtility.CommandEvent(ReleaseCommand)); }
                finally { _releaseControl = 0; }
            }
            source.Repaint();
        }

        internal static void Tick()
        {
            if (!Active) return;
            if (!ScenePointer.IsForeground || EditorWindow.focusedWindow != _source ||
                SceneControls.TextIsEditing() || _source.position != _windowRect ||
                _source.cameraViewport != _viewport || _source.in2DMode != _in2DMode ||
                _source.isRotationLocked != _rotationLocked)
                End();
        }

        private static float HalfHeightPerSize(SceneView view)
        {
            var camera = view.camera;
            return camera.orthographic ? camera.orthographicSize / view.size :
                Mathf.Abs(view.cameraDistance) * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f) / view.size;
        }

        private static Vector3 Anchor(SceneView view, Vector2 mouse, float halfHeightPerSize)
        {
            var viewport = view.cameraViewport;
            var normalized = new Vector2((mouse.x - viewport.x) / viewport.width - 0.5f,
                (mouse.y - viewport.y) / viewport.height - 0.5f);
            return view.pivot + NavigationMath.PlaneOffset(view.rotation, normalized,
                view.size * halfHeightPerSize, view.camera.aspect);
        }

        internal static void ApplyDrag(SceneView view, NavigationGesture gesture, Vector2 delta,
            float halfHeightPerSize, Vector3 anchor)
        {
            var pivot = view.pivot;
            var rotation = view.rotation;
            var size = view.size;
            if (gesture == NavigationGesture.Orbit && !view.in2DMode && !view.isRotationLocked)
                rotation = NavigationMath.Orbit(rotation, delta);
            else if (gesture == NavigationGesture.Pan || gesture == NavigationGesture.Orbit)
                pivot -= NavigationMath.PlaneOffset(rotation,
                    new Vector2(delta.x / view.cameraViewport.width, delta.y / view.cameraViewport.height),
                    size * halfHeightPerSize, view.camera.aspect);
            else if (gesture == NavigationGesture.Zoom)
            {
                var nextSize = NavigationMath.ZoomSize(size, delta.y * 0.01f);
                pivot = NavigationMath.ZoomPivot(pivot, anchor, size, nextSize);
                size = nextSize;
            }
            Apply(view, pivot, rotation, size);
        }

        internal static void Zoom(SceneView view, Vector2 mouse, float amount)
        {
            if (!view.camera || view.size <= 0 || !float.IsFinite(view.size)) return;
            var anchor = Anchor(view, mouse, HalfHeightPerSize(view));
            Unlock(view);
            ZoomAtAnchor(view, anchor, amount);
        }

        private static void Unlock(SceneView view)
        {
            // Unity's lock flag is internal. The public framing API can clear the lock;
            // restore the camera immediately so framing never becomes part of navigation.
            var pivot = view.pivot;
            var rotation = view.rotation;
            var size = view.size;
            var ortho = view.orthographic;
            view.FrameSelected(false, true);
            view.LookAt(pivot, rotation, size, ortho, true);
        }

        internal static void ZoomAtAnchor(SceneView view, Vector3 anchor, float amount)
        {
            var size = NavigationMath.ZoomSize(view.size, amount);
            Apply(view, NavigationMath.ZoomPivot(view.pivot, anchor, view.size, size), view.rotation, size);
        }

        private static void Apply(SceneView view, Vector3 pivot, Quaternion rotation, float size)
        {
            if (!GrabMath.Finite(pivot) || !float.IsFinite(size) || size <= 0 ||
                !float.IsFinite(Quaternion.Dot(rotation, rotation))) return;
            view.LookAt(pivot, rotation, size, view.orthographic, true);
            view.Repaint();
        }

        internal static bool FocusSelection(SceneView view)
        {
            if (!SceneCreation.CanTransform(SceneControls.SelectedTransforms)) return false;
            return view.FrameSelected(false, true);
        }

        internal static bool Handle(SceneView view, Event current, int control)
        {
            if (Active)
            {
                if (view != _source) return false;
                if ((current.type == EventType.ExecuteCommand || current.type == EventType.ValidateCommand) &&
                    current.commandName == "FrameSelected")
                { current.Use(); return true; }
                if (GUIUtility.hotControl != _control || Mathf.Abs(EditorGUIUtility.pixelsPerPoint - _scale) > 0.001f)
                { End(true); return false; }
                if (current.type == EventType.MouseUp && current.button == _button ||
                    current.type == EventType.KeyDown && current.keyCode == KeyCode.Escape)
                {
                    current.Use();
                    End(true);
                    return true;
                }
                if (current.type == EventType.MouseDrag)
                    ApplyDrag(view, _gesture, current.delta, _halfHeightPerSize, _anchor);
                if (current.isMouse || current.isKey || current.type == EventType.ScrollWheel)
                {
                    current.Use();
                    return true;
                }
                return false;
            }
            if (SceneControls.Active || EditorWindow.mouseOverWindow != view || !ScenePointer.IsForeground ||
                SceneControls.TextIsEditing() || GUIUtility.hotControl != 0 ||
                !view.cameraViewport.Contains(current.mousePosition)) return false;
            if (ShortcutProfiles.Matches("Blender Scene Controls/Frame Selected", current) ||
                ((current.type == EventType.ExecuteCommand || current.type == EventType.ValidateCommand) &&
                 current.commandName == "FrameSelected"))
            {
                if (current.type != EventType.ValidateCommand) SceneControls.TryExecute(SceneCommand.FrameSelected, view, control);
                current.Use();
                return true;
            }
            if (current.type == EventType.ScrollWheel && !current.alt && !current.control && !current.command)
            {
                Zoom(view, current.mousePosition, current.delta.y * 0.08f);
                current.Use();
                return true;
            }
            if (current.type != EventType.MouseDown) return false;
            var gesture = NavigationMath.Gesture(current.button, current.shift, current.control, current.alt,
                current.command, SceneControls.EmulateThreeButtonMouse, view.in2DMode || view.isRotationLocked);
            if (gesture == NavigationGesture.None || !float.IsFinite(view.size) || view.size <= 0) return false;
            _source = view;
            _control = control;
            _button = current.button;
            _gesture = gesture;
            _windowRect = view.position;
            _viewport = view.cameraViewport;
            _scale = EditorGUIUtility.pixelsPerPoint;
            _in2DMode = view.in2DMode;
            _rotationLocked = view.isRotationLocked;
            _halfHeightPerSize = HalfHeightPerSize(view);
            _anchor = Anchor(view, current.mousePosition, _halfHeightPerSize);
            Unlock(view);
            view.Focus();
            GUIUtility.hotControl = control;
            current.Use();
            return true;
        }
    }
}
