using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace BlenderSceneControls
{
    internal sealed class MeshEditMode : IDisposable
    {
        internal readonly MeshFilter Target;
        internal readonly SceneView Source;
        internal readonly MeshEditTransaction Transaction;
        internal readonly MeshElementSelection Selection;
        private Mesh _sourceMesh;
        private readonly int _stageHash;
        private bool _box;
        private int _boxControl;
        internal int BoxControl => _boxControl;
        private bool _dragging;
        private Vector2 _boxStart;
        private Vector2 _boxEnd;
        private bool _previousMouseMove;
        private int _hoverEdge;
        private bool _disposed;
        private MeshDocument _preparationSnapshot;
        private string _preparationMessage;
        private int[] _proposedVertices = Array.Empty<int>();
        private Matrix4x4 _preparationWorld;
        private Mesh _topologyPreview;
        private int _preparationSelectionRevision;
        private int _preparationDocumentRevision;
        private Rect _preparationWindow;
        private Rect _preparationViewport;
        private Tool _preparationTool;
        internal bool Preparing => _preparationSnapshot != null;
        internal MeshSurfacePreview SurfacePreview;
        internal bool CanRecoverConnectivity => !MeshDocumentAsset.Find(Target.sharedMesh) &&
            Enumerable.Range(0, Target.sharedMesh.subMeshCount).All(i => Target.sharedMesh.GetTopology(i) == MeshTopology.Triangles);

        internal MeshEditMode(MeshFilter target, SceneView source)
        {
            Target = target;
            Source = source;
            _sourceMesh = target.sharedMesh;
            _stageHash = StageUtility.GetStageHandle(target.gameObject).GetHashCode();
            Transaction = new MeshEditTransaction(target);
            try
            {
                // Prepare only the detached document. Tab does not create assets or Undo entries.
                // Also repairs closed split seams in pairs created before automatic preparation.
                var validation = Transaction.EvaluatePreview();
                UnityEngine.Object.DestroyImmediate(validation);
                var connected = MeshTopologyPreparation.Recover(Transaction.Working, out _);
                var prepared = MeshTopologyPreparation.Quads(connected, out _, false);
                validation = MeshNativeBridge.Evaluate(prepared);
                UnityEngine.Object.DestroyImmediate(validation);
                Transaction.RestoreWorking(prepared);
                Selection = new MeshElementSelection(Transaction.Working);
            }
            catch { Transaction.Dispose(); throw; }
            _previousMouseMove = source.wantsMouseMove;
            source.wantsMouseMove = true;
        }

        internal void RestoreDocument(MeshDocument snapshot, bool topology = false)
        {
            Transaction.RestoreWorking(snapshot);
            if (topology) Selection.RefreshTopology(Transaction.Working);
            else Selection.RefreshGeometry(Transaction.Working);
        }
        internal void RefreshSource() => _sourceMesh = Target.sharedMesh;

        internal bool BeginPreparation(bool connectivity)
        {
            if (!Valid || Preparing || SelectingBox) return false;
            if (connectivity && !CanRecoverConnectivity)
                throw new InvalidOperationException("Connectivity recovery is only for triangle imports without authored companion topology.");
            var snapshot = Transaction.Working.Clone();
            // Apply the existing interactive allocation limit before any recovery search.
            var validation = Transaction.EvaluatePreview();
            UnityEngine.Object.DestroyImmediate(validation);
            var candidate = connectivity ? MeshTopologyPreparation.Recover(snapshot, out _preparationMessage) :
                MeshTopologyPreparation.Quads(snapshot, out _preparationMessage);
            return PublishTopology(snapshot, candidate);
        }

        internal bool BeginRemoval(MeshRemoval operation)
        {
            if (!Valid || Preparing || SelectingBox) return false;
            var validation = Transaction.EvaluatePreview();
            UnityEngine.Object.DestroyImmediate(validation);
            var snapshot = Transaction.Working.Clone();
            var candidate = MeshDeleteDissolve.Apply(snapshot, operation, out _preparationMessage);
            return PublishTopology(snapshot, candidate);
        }

        private bool PublishTopology(MeshDocument snapshot, MeshDocument candidate)
        {
            if (JsonUtility.ToJson(candidate) == JsonUtility.ToJson(snapshot))
            { Source.ShowNotification(new GUIContent(_preparationMessage)); return false; }
            var mesh = MeshNativeBridge.Evaluate(candidate);
            mesh.hideFlags = HideFlags.HideAndDontSave;
            _proposedVertices = candidate.Vertices.Where(v => snapshot.Vertices.Count(old => old.Position.Equals(v.Position)) >
                candidate.Vertices.Count(other => other.Position.Equals(v.Position))).Select(v => v.Id).ToArray();
            _preparationSnapshot = snapshot;
            _preparationWorld = Target.transform.localToWorldMatrix;
            Transaction.RestoreWorking(candidate);
            Selection.RefreshTopology(Transaction.Working);
            _preparationSelectionRevision = Selection.Revision;
            _preparationDocumentRevision = Transaction.Working.Revision;
            _preparationWindow = Source.position; _preparationViewport = Source.cameraViewport;
            _preparationTool = Tools.current;
            _topologyPreview = mesh;
            SurfacePreview?.Dispose();
            SurfacePreview = new MeshSurfacePreview(this, () => _topologyPreview, () => Preparing && PreparationValid);
            Source.Repaint();
            return true;
        }

        internal bool ConfirmPreparation()
        {
            if (!Preparing) return false;
            var snapshot = _preparationSnapshot;
            try
            {
                if (!PreparationValid)
                    throw new InvalidOperationException("Mesh preparation target or transform changed.");
                var path = Transaction.OwnedMesh ? null : Transaction.GeneratedPath();
                Transaction.Working.Revision++;
                var committed = Transaction.Commit(path);
                if (!committed) Transaction.RestoreWorking(snapshot);
                RefreshSource();
                Selection.RefreshTopology(Transaction.Working);
                return committed;
            }
            catch
            {
                Transaction.RestoreWorking(snapshot);
                Selection.RefreshTopology(Transaction.Working);
                throw;
            }
            finally { EndTopologyPreview(); }
        }

        internal void CancelPreparation()
        {
            if (!Preparing) return;
            Transaction.RestoreWorking(_preparationSnapshot);
            Selection.RefreshTopology(Transaction.Working);
            EndTopologyPreview();
        }

        internal bool PreparationValid => Valid && Target.transform.localToWorldMatrix.Equals(_preparationWorld) &&
            Selection.Revision == _preparationSelectionRevision && Transaction.Working.Revision == _preparationDocumentRevision &&
            Source.position.Equals(_preparationWindow) && Source.cameraViewport.Equals(_preparationViewport) && Tools.current == _preparationTool;

        private void EndTopologyPreview()
        {
            _preparationSnapshot = null; _proposedVertices = Array.Empty<int>();
            SurfacePreview?.Dispose(); SurfacePreview = null;
            if (_topologyPreview) UnityEngine.Object.DestroyImmediate(_topologyPreview);
            _topologyPreview = null;
            if (Source) Source.Repaint();
        }

        internal bool Valid => !_disposed && Target && Source && Target.sharedMesh == _sourceMesh &&
            !EditorApplication.isPlayingOrWillChangePlaymode &&
            PrefabStageUtility.GetCurrentPrefabStage() == null &&
            StageUtility.GetCurrentStageHandle().GetHashCode() == _stageHash;
        internal bool MatchesUnitySelection => UnityEditor.Selection.gameObjects.Length == 1 &&
            UnityEditor.Selection.activeGameObject == Target.gameObject;
        internal bool SelectingBox => _box;
        internal void CancelBox(bool sourceGUI = false)
        {
            if (_boxControl != 0)
            {
                if (sourceGUI) { if (GUIUtility.hotControl == _boxControl) GUIUtility.hotControl = 0; }
                else SceneControls.ReleaseMeshCapture(Source, _boxControl);
            }
            _boxControl = 0; _box = false; _dragging = false;
        }
        internal void Frame() => Source.Frame(Selection.WorldBounds(Target.transform), false);
        internal void SelectLoop() => Selection.Add(Selection.Loop(_hoverEdge), false);
        internal bool HasLoopSeed => Selection.Mode != MeshSelectionMode.Vertex && _hoverEdge != 0;
        internal bool Execute(SceneCommand command)
        {
            if (command >= SceneCommand.DeleteVertices && command <= SceneCommand.DissolveFaces)
                return BeginRemoval((MeshRemoval)(command - SceneCommand.DeleteVertices));
            switch (command)
            {
                case SceneCommand.RecoverConnectivity: return BeginPreparation(true);
                case SceneCommand.TrisToQuads: return BeginPreparation(false);
                case SceneCommand.ConfirmPreparation: return ConfirmPreparation();
                case SceneCommand.CancelPreparation: CancelPreparation(); return true;
                case SceneCommand.Vertex: Selection.SetMode(MeshSelectionMode.Vertex); break;
                case SceneCommand.Edge: Selection.SetMode(MeshSelectionMode.Edge); break;
                case SceneCommand.Face: Selection.SetMode(MeshSelectionMode.Face); break;
                case SceneCommand.XRay: Selection.XRay = !Selection.XRay; break;
                case SceneCommand.Median: Selection.PivotMode = MeshPivotMode.Median; break;
                case SceneCommand.ActiveElement: Selection.PivotMode = MeshPivotMode.Active; break;
                case SceneCommand.SelectAll: Selection.Assign(Selection.All); break;
                case SceneCommand.DeselectAll: Selection.Assign(Array.Empty<int>()); break;
                case SceneCommand.BoxSelect: _box = true; _dragging = false; break;
                case SceneCommand.SelectLoop: if (!HasLoopSeed) return false; SelectLoop(); break;
                case SceneCommand.FrameSelected: Frame(); break;
                default: return false;
            }
            Source.Repaint();
            return true;
        }
        internal void DisposeSelection() { CancelPreparation(); CancelBox(); Transaction.Dispose(); }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            SurfacePreview?.Dispose(); SurfacePreview = null;
            DisposeSelection();
            if (Source) { Source.wantsMouseMove = _previousMouseMove; Source.Repaint(); }
        }

        private Vector3 World(int id) => Target.transform.TransformPoint(Selection.Vertices[id].Position);
        private bool Project(Vector3 world, out Vector2 point)
        {
            point = HandleUtility.WorldToGUIPoint(world);
            return Source.camera.WorldToViewportPoint(world).z > Source.camera.nearClipPlane && Source.cameraViewport.Contains(point);
        }
        private float Hit(Ray ray, out int face)
        {
            var closest = float.PositiveInfinity; face = 0;
            foreach (var t in Selection.Triangles)
                if (MeshElementSelection.RayTriangle(ray, World(t.a), World(t.b), World(t.c), out var distance) && distance < closest)
                { closest = distance; face = t.face; }
            return closest;
        }
        private bool Visible(Vector3 world, Vector2 point)
        {
            if (Selection.XRay) return true;
            var ray = HandleUtility.GUIPointToWorldRay(point);
            var depth = Vector3.Dot(world - ray.origin, ray.direction);
            var hit = Hit(ray, out _);
            if (hit < depth - Mathf.Max(.0001f, Mathf.Abs(depth) * .00001f)) return false;
            // Native scene picking also accounts for other renderers, without adding colliders.
            var picked = HandleUtility.PickGameObject(point, false);
            return !picked || picked == Target.gameObject;
        }
        internal int Pick(Vector2 mouse, MeshSelectionMode mode)
        {
            if (mode == MeshSelectionMode.Face)
            {
                var ray = HandleUtility.GUIPointToWorldRay(mouse);
                var distance = Hit(ray, out var face);
                return face != 0 && Visible(ray.origin + ray.direction * distance, mouse) ? face : 0;
            }
            var best = 9f; var result = 0; var bestDepth = float.PositiveInfinity;
            if (mode == MeshSelectionMode.Vertex)
            {
                foreach (var v in Selection.Vertices.Values)
                {
                    var world = World(v.Id);
                    if (!Project(world, out var point)) continue;
                    var distance = Vector2.Distance(point, mouse);
                    var depth = Source.camera.WorldToViewportPoint(world).z;
                    if ((distance < best || Mathf.Approximately(distance, best) && depth < bestDepth) && Visible(world, point))
                    { best = distance; bestDepth = depth; result = v.Id; }
                }
            }
            else
            {
                foreach (var edge in Selection.Edges.Values)
                {
                    var a = World(edge.A); var b = World(edge.B);
                    if (!Project(a, out var p) || !Project(b, out var q)) continue;
                    var delta = q - p;
                    var fraction = delta.sqrMagnitude < .000001f ? 0 : Mathf.Clamp01(Vector2.Dot(mouse - p, delta) / delta.sqrMagnitude);
                    var point = p + fraction * delta;
                    // Screen interpolation is perspective-corrected by intersecting the ray with the edge plane.
                    var ray = HandleUtility.GUIPointToWorldRay(point);
                    var direction = b - a;
                    var dot = Vector3.Dot(ray.direction, direction);
                    var denominator = direction.sqrMagnitude - dot * dot;
                    var along = denominator > 1e-10f ? Mathf.Clamp01((Vector3.Dot(ray.origin - a, direction) - dot * Vector3.Dot(ray.origin - a, ray.direction)) / denominator) : fraction;
                    var world = a + direction * along;
                    var distance = Vector2.Distance(point, mouse);
                    var depth = Source.camera.WorldToViewportPoint(world).z;
                    if ((distance < best || Mathf.Approximately(distance, best) && depth < bestDepth) && Visible(world, point))
                    { best = distance; bestDepth = depth; result = edge.Id; }
                }
            }
            return result;
        }

        internal void Box(Rect rectangle, bool extend)
        {
            var ids = new List<int>();
            foreach (var id in Selection.All)
            {
                // Vertex: point; Edge/Face: all boundary vertices inside and visible.
                // This avoids selecting hidden geometry from a visible centroid alone.
                if (Selection.ElementVertices(id).All(v => Project(World(v), out var point) && rectangle.Contains(point) && Visible(World(v), point))) ids.Add(id);
            }
            Selection.Add(ids, extend);
        }
        internal bool Handle(Event current)
        {
            if (Preparing)
            {
                if (current.type == EventType.KeyDown && (current.keyCode == KeyCode.Return || current.keyCode == KeyCode.KeypadEnter))
                { ConfirmPreparation(); current.Use(); return true; }
                if (current.type == EventType.KeyDown && current.keyCode == KeyCode.Escape || current.type == EventType.MouseDown && current.button == 1 && !current.alt)
                { CancelPreparation(); current.Use(); return true; }
                if (current.type == EventType.Layout) HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
                if ((current.type == EventType.MouseDown || current.type == EventType.MouseUp) && current.button == 0 && !current.alt)
                { if (current.type == EventType.MouseDown) ConfirmPreparation(); current.Use(); return true; }
                return false;
            }
            var boxControl = GUIUtility.GetControlID("BSC Mesh Box".GetHashCode(), FocusType.Passive);
            if (current.type == EventType.MouseMove)
            {
                _hoverEdge = Pick(current.mousePosition, MeshSelectionMode.Edge);
                Source.Repaint();
            }
            if (_box)
            {
                if (current.type == EventType.KeyDown && current.keyCode == KeyCode.Escape || current.type == EventType.MouseDown && current.button == 1)
                { CancelBox(true); current.Use(); return true; }
                if (current.type == EventType.MouseDown && current.button == 0 && !current.alt)
                { Source.Focus(); _boxControl = boxControl; GUIUtility.hotControl = boxControl; _dragging = true; _boxStart = _boxEnd = current.mousePosition; current.Use(); return true; }
                if (_dragging && current.type == EventType.MouseDrag && current.button == 0)
                { _boxEnd = current.mousePosition; Source.Repaint(); current.Use(); return true; }
                if (_dragging && current.type == EventType.MouseUp && current.button == 0)
                {
                    _boxEnd = current.mousePosition;
                    Box(Rect.MinMaxRect(Mathf.Min(_boxStart.x, _boxEnd.x), Mathf.Min(_boxStart.y, _boxEnd.y), Mathf.Max(_boxStart.x, _boxEnd.x), Mathf.Max(_boxStart.y, _boxEnd.y)), current.shift);
                    CancelBox(true); Source.Repaint(); current.Use(); return true;
                }
            }
            if (current.type == EventType.MouseDown && current.button == 0 && !current.control && !current.command &&
                (!current.alt || !SceneControls.EmulateThreeButtonMouse))
            {
                if (current.alt)
                {
                    _hoverEdge = Pick(current.mousePosition, MeshSelectionMode.Edge);
                    Selection.Add(Selection.Loop(_hoverEdge), current.shift);
                }
                else Selection.Click(Pick(current.mousePosition, Selection.Mode), current.shift);
                Source.Focus(); current.Use(); Source.Repaint(); return true;
            }
            // Reserve component selection clicks only; wheel/MMB/RMB remain navigation.
            if (current.type == EventType.Layout) HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            return false;
        }

        internal void Draw()
        {
            if (Event.current.type != EventType.Repaint) return;
            var color = Handles.color; var depth = Handles.zTest;
            try
            {
                Handles.zTest = Selection.XRay ? CompareFunction.Always : CompareFunction.LessEqual;
                var selected = new HashSet<int>(Selection.Selected);
                foreach (var edge in Selection.Edges.Values)
                {
                    var selectedBoundary = Selection.Mode == MeshSelectionMode.Face && Selection.EdgeFaces[edge.Id].Any(selected.Contains);
                    Handles.color = selectedBoundary || Selection.Mode == MeshSelectionMode.Edge && selected.Contains(edge.Id) ? new Color(1, .55f, .05f) : new Color(.15f, .7f, 1, .8f);
                    Handles.DrawAAPolyLine(2, World(edge.A), World(edge.B));
                }
                if (Selection.Mode == MeshSelectionMode.Vertex)
                    foreach (var vertex in Selection.Vertices.Values)
                    {
                        var world = World(vertex.Id);
                        Handles.color = selected.Contains(vertex.Id) ? new Color(1, .55f, .05f) : new Color(.15f, .7f, 1);
                        Handles.DotHandleCap(0, world, Quaternion.identity, HandleUtility.GetHandleSize(world) * .035f, EventType.Repaint);
                    }
                if (Selection.Mode == MeshSelectionMode.Face)
                    foreach (var face in Selection.Faces.Values)
                    {
                        var world = Target.transform.TransformPoint(Selection.Center(face.Id));
                        Handles.color = selected.Contains(face.Id) ? new Color(1, .55f, .05f) : new Color(.15f, .7f, 1);
                        Handles.DotHandleCap(0, world, Quaternion.identity, HandleUtility.GetHandleSize(world) * .03f, EventType.Repaint);
                    }
                if (Selection.Mode == MeshSelectionMode.Face)
                    foreach (var t in Selection.Triangles.Where(t => selected.Contains(t.face)))
                    {
                        Handles.color = new Color(1, .55f, .05f, .25f);
                        Handles.DrawAAConvexPolygon(World(t.a), World(t.b), World(t.c));
                    }
                if (Selection.Selected.Length > 0)
                {
                    Handles.color = Color.white;
                    var pivot = Target.transform.TransformPoint(Selection.Pivot);
                    Handles.DotHandleCap(0, pivot, Quaternion.identity, HandleUtility.GetHandleSize(pivot) * .06f, EventType.Repaint);
                }
                foreach (var id in _proposedVertices)
                {
                    var world = World(id);
                    Handles.color = new Color(1, .3f, .05f);
                    Handles.DotHandleCap(0, world, Quaternion.identity, HandleUtility.GetHandleSize(world) * .07f, EventType.Repaint);
                }
            }
            finally { Handles.color = color; Handles.zTest = depth; }
            Handles.BeginGUI();
            try
            {
                if (_dragging)
                {
                    var rect = Rect.MinMaxRect(Mathf.Min(_boxStart.x, _boxEnd.x), Mathf.Min(_boxStart.y, _boxEnd.y), Mathf.Max(_boxStart.x, _boxEnd.x), Mathf.Max(_boxStart.y, _boxEnd.y));
                    EditorGUI.DrawRect(rect, new Color(.2f, .6f, 1, .18f));
                    GUI.Box(rect, GUIContent.none);
                }
                if (Preparing)
                    ScenePresentation.Hud(Source, Source.cameraViewport, SceneStatusContext.TopologyPreview(_preparationMessage), SceneControlsConfiguration.Current);
                else if (!SceneControls.Active)
                    ScenePresentation.Hud(Source, Source.cameraViewport, SceneStatusContext.MeshIdle(Selection, _box), SceneControlsConfiguration.Current);
            }
            finally { Handles.EndGUI(); }
        }
    }
}
