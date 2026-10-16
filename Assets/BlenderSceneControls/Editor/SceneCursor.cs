using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace BlenderSceneControls
{
    internal static class SceneCursor
    {
        private const string PositionPrefix = "BlenderSceneControls.3DCursor.";
        private static readonly Vector3[] Arc = new Vector3[9];
        private static readonly Vector3[] Line = new Vector3[2];
        private static SceneView _placementView;

        // Cursor is editor session state, not a scene object or a shared configuration value.
        // Prefab stages and Play Mode must never leak their world coordinates into the main stage.
        internal static string PositionKey(string prefabPath, bool playing) => PositionPrefix +
            (playing ? "Play." : "Edit.") + (string.IsNullOrEmpty(prefabPath) ? "Main" : "Prefab." + prefabPath);
        private static string CurrentKey => PositionKey(PrefabStageUtility.GetCurrentPrefabStage()?.assetPath,
            EditorApplication.isPlaying);

        internal static Vector3 Position
        {
            get
            {
                var value = SessionState.GetVector3(CurrentKey, Vector3.zero);
                return GrabMath.Finite(value) ? value : Vector3.zero;
            }
        }

        internal static bool SetPosition(Vector3 position)
        {
            if (!GrabMath.Finite(position)) return false;
            SessionState.SetVector3(CurrentKey, position);
            SceneView.RepaintAll();
            return true;
        }

        internal static Vector3 CreationPosition(SceneControlsConfiguration config, Vector3 viewPivot) =>
            config.Enable3DCursor ? Position : viewPivot;

        internal static bool IsPlacementGesture(EventType type, int button, bool shift, bool alt, bool control, bool command) =>
            type == EventType.MouseDown && button == 1 && shift && !alt && !control && !command;

        internal static bool TryPlacement(Ray ray, Vector3 viewForward, Vector3 previous, Vector3 viewPivot,
            bool surfaceHit, Vector3 surface, out Vector3 position)
        {
            position = previous;
            if (surfaceHit && GrabMath.Finite(surface)) { position = surface; return true; }
            if (!GrabMath.Finite(ray.origin) || !GrabMath.Finite(ray.direction) ||
                !GrabMath.Finite(viewForward) || viewForward.sqrMagnitude < 0.000001f ||
                !GrabMath.Finite(previous) || !GrabMath.Finite(viewPivot)) return false;
            var normal = viewForward.normalized;
            if (Mathf.Abs(Vector3.Dot(ray.direction, normal)) < 0.000001f) return false;
            var plane = new Plane(normal, previous);
            if (!plane.Raycast(ray, out var distance))
            {
                // A cursor behind the camera has no visible depth plane. Recover at the view pivot.
                plane = new Plane(normal, viewPivot);
                if (!plane.Raycast(ray, out distance)) return false;
            }
            position = ray.GetPoint(distance);
            return GrabMath.Finite(position);
        }

        internal static void ReleaseInput() => _placementView = null;

        internal static bool Handle(SceneView view, Event current)
        {
            if (!SceneControlsConfiguration.Current.Enable3DCursor) { ReleaseInput(); return false; }
            if (_placementView == view && (current.type == EventType.ContextClick ||
                current.type == EventType.MouseUp && current.button == 1))
            {
                if (current.type == EventType.ContextClick) ReleaseInput();
                current.Use();
                return true;
            }
            if (current.type == EventType.MouseDown) ReleaseInput();
            if (!IsPlacementGesture(current.type, current.button, current.shift, current.alt, current.control, current.command) ||
                SceneControls.NavigationActive || GUIUtility.hotControl != 0 || SceneControls.TextIsEditing() ||
                EditorWindow.mouseOverWindow != view || !ScenePointer.IsForeground || !view.camera ||
                !view.cameraViewport.Contains(current.mousePosition)) return false;
            var hit = HandleUtility.PlaceObject(current.mousePosition, out var surface, out _);
            if (TryPlacement(HandleUtility.GUIPointToWorldRay(current.mousePosition), view.camera.transform.forward,
                Position, view.pivot, hit, surface, out var position)) SetPosition(position);
            _placementView = view;
            current.Use();
            return true;
        }

        internal static bool CanEdit => SceneControls.Available && SceneControlsConfiguration.Current.Enable3DCursor &&
            !SceneControls.Active && !SceneControls.NavigationActive;

        [MenuItem("Tools/Blender Scene Controls/3D Cursor/Cursor to World Origin", false, 160)]
        internal static void ToOrigin() { if (CanEdit) SetPosition(Vector3.zero); }

        [MenuItem("Tools/Blender Scene Controls/3D Cursor/Cursor to World Origin", true)]
        private static bool CanToOrigin() => CanEdit;

        internal static bool CanToActive => CanEdit && Selection.activeTransform &&
            SceneCreation.CanTransform(new[] { Selection.activeTransform });

        [MenuItem("Tools/Blender Scene Controls/3D Cursor/Cursor to Active Object", false, 161)]
        internal static void ToActive() { if (CanToActive) SetPosition(Selection.activeTransform.position); }

        [MenuItem("Tools/Blender Scene Controls/3D Cursor/Cursor to Active Object", true)]
        private static bool ValidateToActive() => CanToActive;

        internal static void Draw(SceneView view)
        {
            if (!SceneControlsConfiguration.Current.Enable3DCursor || Event.current.type != EventType.Repaint ||
                !view.camera || !GuideGeometry.Project(view.camera, view.cameraViewport, Position, out var point) ||
                !view.cameraViewport.Contains(point)) return;
            var previousColor = Handles.color;
            var previousDepth = Handles.zTest;
            var previousMatrix = Handles.matrix;
            Handles.BeginGUI();
            GUI.BeginGroup(view.cameraViewport);
            try
            {
                Handles.matrix = Matrix4x4.identity;
                Handles.zTest = CompareFunction.Always;
                point -= view.cameraViewport.position;
                void Stroke(Vector2 start, Vector2 end, float width, Color color)
                {
                    Line[0] = start; Line[1] = end;
                    Handles.color = color;
                    Handles.DrawAAPolyLine(width, Line);
                }
                Stroke(point + Vector2.left * 14, point + Vector2.right * 14, 4, Color.black);
                Stroke(point + Vector2.down * 14, point + Vector2.up * 14, 4, Color.black);
                Stroke(point + Vector2.left * 14, point + Vector2.right * 14, 1, Color.white);
                Stroke(point + Vector2.down * 14, point + Vector2.up * 14, 1, Color.white);
                for (var segment = 0; segment < 8; segment++)
                {
                    for (var i = 0; i < Arc.Length; i++)
                    {
                        var angle = (segment + i / 8f) * Mathf.PI / 4;
                        Arc[i] = point + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 9;
                    }
                    Handles.color = Color.black;
                    Handles.DrawAAPolyLine(5, Arc);
                    Handles.color = segment % 2 == 0 ? Color.red : Color.white;
                    Handles.DrawAAPolyLine(3, Arc);
                }
            }
            finally
            {
                GUI.EndGroup();
                Handles.EndGUI();
                Handles.color = previousColor;
                Handles.zTest = previousDepth;
                Handles.matrix = previousMatrix;
            }
        }
    }
}
