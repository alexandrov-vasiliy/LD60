using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace BlenderSceneControls
{
    internal static class GuideGeometry
    {
        internal static bool Finite(Vector2 value) => float.IsFinite(value.x) && float.IsFinite(value.y);

        internal static bool Project(Camera camera, Rect viewport, Vector3 world, out Vector2 point)
        {
            point = default;
            var projected = camera.WorldToViewportPoint(world);
            if (!GrabMath.Finite(projected) || projected.z < camera.nearClipPlane) return false;
            point = viewport.position + new Vector2(projected.x * viewport.width, (1 - projected.y) * viewport.height);
            return Finite(point);
        }

        internal static bool ClipLine(Rect rect, ref Vector2 a, ref Vector2 b)
        {
            if (!Finite(a) || !Finite(b) || rect.width <= 0 || rect.height <= 0) return false;
            var delta = b - a;
            if (!Finite(delta)) return false;
            var start = 0f;
            var end = 1f;
            bool Edge(float p, float q)
            {
                if (Mathf.Abs(p) < 0.000001f) return q >= 0;
                var ratio = q / p;
                if (p < 0) start = Mathf.Max(start, ratio);
                else end = Mathf.Min(end, ratio);
                return start <= end;
            }
            if (!Edge(-delta.x, a.x - rect.xMin) || !Edge(delta.x, rect.xMax - a.x) ||
                !Edge(-delta.y, a.y - rect.yMin) || !Edge(delta.y, rect.yMax - a.y)) return false;
            b = a + delta * end;
            a += delta * start;
            return Finite(a) && Finite(b);
        }

        internal static List<Vector2> ClipPolygon(Rect rect, List<Vector2> polygon)
        {
            for (var edge = 0; edge < 4 && polygon.Count > 0; edge++)
            {
                var result = new List<Vector2>();
                float Distance(Vector2 p) => edge == 0 ? p.x - rect.xMin : edge == 1 ? rect.xMax - p.x :
                    edge == 2 ? p.y - rect.yMin : rect.yMax - p.y;
                var previous = polygon[polygon.Count - 1];
                var before = Distance(previous);
                foreach (var current in polygon)
                {
                    var after = Distance(current);
                    if ((before >= 0) != (after >= 0))
                        result.Add(Vector2.Lerp(previous, current, before / (before - after)));
                    if (after >= 0) result.Add(current);
                    previous = current;
                    before = after;
                }
                polygon = result;
            }
            return polygon;
        }

        internal static bool AxisVisible(int candidate, int axis, bool plane) =>
            axis < 0 || (plane ? candidate != axis : candidate == axis);

        internal static bool PlaceHud(Rect viewport, Vector2 size, IReadOnlyList<Rect> obstacles,
            bool bottom, out Rect result)
        {
            result = default;
            if (size.x <= 0 || size.y <= 0 || size.x > viewport.width - 16 || size.y > viewport.height - 16) return false;
            var limit = Mathf.Min(viewport.height / 2, viewport.height - size.y - 8);
            for (var offset = 8f; offset <= limit; offset += 8)
            {
                var candidate = new Rect(viewport.x + 8, bottom ? viewport.yMax - size.y - offset : viewport.y + offset, size.x, size.y);
                var free = true;
                foreach (var obstacle in obstacles)
                    if (candidate.Overlaps(obstacle)) { free = false; break; }
                if (free) { result = candidate; return true; }
            }
            return false;
        }
    }

    internal static class ScenePresentation
    {
        private static bool _customCursor;
        private static GUIStyle _label;

        internal static void ResetCursor()
        {
            if (!_customCursor) return;
            UnityEngine.Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            _customCursor = false;
        }

        internal static void Cursor(ModalSession session)
        {
            var settings = SceneControlsConfiguration.Current.CursorFor(session.Mode == ModalMode.Scale,
                session.Mode == ModalMode.Rotate, session.RotateScale?.Trackball == true);
            var custom = settings.HasUsableImage;
            EditorGUIUtility.AddCursorRect(session.Viewport, custom ? MouseCursor.CustomCursor : settings.SafeFallback);
            if (Event.current.type != EventType.Repaint) return;
            if (custom)
            {
                UnityEngine.Cursor.SetCursor(settings.Image, settings.SafeHotspot, CursorMode.Auto);
                _customCursor = true;
            }
            else ResetCursor();
        }

        internal static void Draw(ModalSession session)
        {
            if (Event.current.type != EventType.Repaint) return;
            var config = SceneControlsConfiguration.Current;
            var viewport = session.Viewport;
            var grab = session.Grab;
            var rotateScale = session.RotateScale;
            var pivot = grab != null ? grab.Pivot : rotateScale != null ? rotateScale.Pivot : Vector3.zero;
            var basis = grab != null ? grab.Basis : rotateScale != null ? rotateScale.Basis : Quaternion.identity;
            var axis = grab != null ? grab.Axis : rotateScale != null ? rotateScale.Axis : -1;
            var plane = grab != null ? grab.Plane : rotateScale?.Plane == true;
            var camera = session.Source.camera;
            var oldColor = Handles.color;
            var oldDepthTest = Handles.zTest;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            Handles.BeginGUI();
            try
            {
                if (!(session.Transaction is MeshLoopCutTransaction) && !(session.Transaction is MeshInsetTransaction) && !(session.Transaction is MeshBevelTransaction) && camera && GuideGeometry.Project(camera, viewport, pivot, out var screenPivot))
                {
                    var depth = camera.WorldToViewportPoint(pivot).z;
                    var halfHeight = camera.orthographic ? camera.orthographicSize : depth * Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad / 2);
                    if (float.IsFinite(halfHeight) && halfHeight > 0 && halfHeight < 10000000)
                    {
                        var extent = halfHeight * Mathf.Max(1, camera.aspect) * 3;
                        var vectors = new[] { basis * Vector3.right, basis * Vector3.up, basis * Vector3.forward };
                        var colors = new[] { config.XAxis, config.YAxis, config.ZAxis };
                        if (plane && axis >= 0)
                        {
                            var u = vectors[(axis + 1) % 3] * halfHeight * 0.12f;
                            var v = vectors[(axis + 2) % 3] * halfHeight * 0.12f;
                            var polygon = new List<Vector2>();
                            foreach (var corner in new[] { pivot - u - v, pivot + u - v, pivot + u + v, pivot - u + v })
                            {
                                if (!GuideGeometry.Project(camera, viewport, corner, out var point)) { polygon.Clear(); break; }
                                polygon.Add(point);
                            }
                            polygon = GuideGeometry.ClipPolygon(viewport, polygon);
                            if (polygon.Count >= 3)
                            {
                                Handles.color = WithOpacity(config.Plane, config.SafeOpacity);
                                Handles.DrawAAConvexPolygon(polygon.ConvertAll(p => (Vector3)p).ToArray());
                            }
                        }
                        for (var i = 0; i < 3; i++)
                        {
                            if (!GuideGeometry.AxisVisible(i, axis, plane)) continue;
                            var a = pivot - vectors[i] * extent;
                            var b = pivot + vectors[i] * extent;
                            // Clip against the near plane before perspective division.
                            var za = camera.WorldToViewportPoint(a).z;
                            var zb = camera.WorldToViewportPoint(b).z;
                            var near = camera.nearClipPlane * 1.001f;
                            if (za < near && zb < near) continue;
                            if (za < near) a = Vector3.Lerp(a, b, (near - za) / (zb - za));
                            else if (zb < near) b = Vector3.Lerp(a, b, (near - za) / (zb - za));
                            if (!GuideGeometry.Project(camera, viewport, a, out var start) ||
                                !GuideGeometry.Project(camera, viewport, b, out var end) ||
                                !GuideGeometry.ClipLine(viewport, ref start, ref end)) continue;
                            Handles.color = WithOpacity(colors[i], config.SafeOpacity);
                            Handles.DrawAAPolyLine(config.SafeThickness, (Vector3)start, (Vector3)end);
                        }
                    }
                    if (session.Mode == ModalMode.Rotate || session.Mode == ModalMode.Scale)
                    {
                        // The native cursor's wrapped position, rather than accumulated transform motion.
                        var mouse = viewport.position + (session.PreviousCursor - session.PixelBounds.position) / session.PixelsPerPoint;
                        if (GuideGeometry.ClipLine(viewport, ref screenPivot, ref mouse))
                        {
                            var dashColor = WithOpacity(config.DottedLine, config.SafeOpacity);
                            var length = Vector2.Distance(screenPivot, mouse);
                            var direction = length > 0 ? (mouse - screenPivot) / length : Vector2.zero;
                            for (var offset = 0f; offset < length; offset += 10)
                            {
                                var start = (Vector3)(screenPivot + direction * offset);
                                var end = (Vector3)(screenPivot + direction * Mathf.Min(offset + 5, length));
                                // A wider black stroke keeps each dash readable over light scene geometry.
                                Handles.color = new Color(0, 0, 0, dashColor.a);
                                Handles.DrawAAPolyLine(config.SafeThickness + 2, start, end);
                                Handles.color = dashColor;
                                Handles.DrawAAPolyLine(config.SafeThickness, start, end);
                            }
                        }
                    }
                }
                Hud(session.Source, session.Viewport, session.Phase.Presentation(session, config.SwapYAndZAxisKeys), config);
            }
            finally
            {
                Handles.EndGUI();
                Handles.color = oldColor;
                Handles.zTest = oldDepthTest;
            }
        }

        private static Color WithOpacity(Color color, float opacity)
        {
            if (!float.IsFinite(color.r) || !float.IsFinite(color.g) || !float.IsFinite(color.b) || !float.IsFinite(color.a)) color = Color.white;
            color.a = Mathf.Clamp01(color.a) * opacity;
            return color;
        }

        internal static string Status(ModalSession session) => session.Phase.Presentation(session,
            SceneControlsConfiguration.Current.SwapYAndZAxisKeys).Status;

        internal static string Status(ModalMode mode, GrabState grab, RotateScaleState rotation) =>
            SceneStatusContext.Transform(mode, grab, rotation, SceneControlsConfiguration.Current.SwapYAndZAxisKeys).Status;

        internal static void Hud(SceneView source, Rect viewport, SceneStatusContext context, SceneControlsConfiguration config)
        {
            if (_label == null) _label = new GUIStyle(EditorStyles.miniLabel)
                { wordWrap = true, clipping = TextClipping.Clip, fontSize = 11, padding = new RectOffset(8, 8, 4, 4) };
            _label.normal.textColor = WithOpacity(config.Text, 1);
            var obstacles = new List<Rect>();
            foreach (var overlay in source.overlayCanvas.overlays)
            {
                if (!overlay.displayed || overlay.rootVisualElement == null) continue;
                var bounds = source.rootVisualElement.WorldToLocal(overlay.rootVisualElement.worldBound);
                if (float.IsFinite(bounds.x) && float.IsFinite(bounds.y) && bounds.width > 0 && bounds.height > 0)
                    obstacles.Add(new Rect(bounds.x - 4, bounds.y - 4, bounds.width + 8, bounds.height + 8));
            }
            var width = Mathf.Min(680, viewport.width - 16);
            DrawLabel(context.Status, false, 72);
            DrawLabel(viewport.width < 480 ? context.CompactHints : context.Hints, true, 56);

            void DrawLabel(string text, bool bottom, float maxHeight)
            {
                if (width < 100) return;
                var content = new GUIContent(text);
                for (var availableWidth = width; availableWidth >= 100; availableWidth -= 40)
                {
                    var height = _label.CalcHeight(content, availableWidth);
                    // Hide a panel only if no readable space remains among the native overlays.
                    if (height > maxHeight || !GuideGeometry.PlaceHud(viewport, new Vector2(availableWidth, height), obstacles, bottom, out var rect)) continue;
                    EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin ? new Color(0.08f, 0.08f, 0.08f, 0.92f) : new Color(0.18f, 0.18f, 0.18f, 0.92f));
                    GUI.Label(rect, content, _label);
                    obstacles.Add(rect);
                    return;
                }
            }
        }
    }
}
