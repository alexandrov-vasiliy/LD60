using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.Toolbars;
using UnityEngine.UIElements;

namespace BlenderSceneControls
{
    [Overlay(typeof(SceneView), "BSC Mesh Mode", true, defaultDockZone = DockZone.TopToolbar, defaultDockPosition = DockPosition.Top)]
    public sealed class MeshModeToolbar : ToolbarOverlay
    {
        public MeshModeToolbar() : base("BSC/Object", "BSC/Mesh", "BSC/Vertex", "BSC/Edge", "BSC/Face", "BSC/XRay", "BSC/Median", "BSC/Active", "BSC/Counts") { }
    }

    [Overlay(typeof(SceneView), "BSC Mesh Actions", true, defaultDockZone = DockZone.RightToolbar, defaultDockPosition = DockPosition.Top)]
    public sealed class MeshActionsToolbar : ToolbarOverlay
    {
        public MeshActionsToolbar() : base("BSC/Grab", "BSC/Rotate", "BSC/Scale", "BSC/ApplyScale", "BSC/Frame", "BSC/All", "BSC/None", "BSC/Box", "BSC/Loop", "BSC/ConfirmPreparation", "BSC/CancelPreparation", "BSC/Extrude", "BSC/LoopCut", "BSC/Delete", "BSC/Inset", "BSC/Bevel", "BSC/VertexBevel", "BSC/Save", "BSC/SaveAs", "BSC/FBX") { }
    }

    internal abstract class SceneToolbarButton : EditorToolbarToggle, IAccessContainerWindow
    {
        public EditorWindow containerWindow { get; set; }
        private readonly SceneCommand _command;
        private readonly string _hint;
        private SceneCommandContext _context;
        protected SceneToolbarButton(string caption, SceneCommand command, string hint)
        {
            text = caption; _command = command; _hint = hint;
            style.minWidth = 28; style.flexShrink = 0;
            this.RegisterValueChangedCallback(_ => { SceneControls.ExecuteToolbar(_command, _context); Refresh(); });
            var refresh = schedule.Execute(Refresh).Every(100);
            refresh.Pause();
            RegisterCallback<AttachToPanelEvent>(_ => { Refresh(); refresh.Resume(); });
            RegisterCallback<DetachFromPanelEvent>(_ => { refresh.Pause(); _context = null; });
        }
        private void Refresh()
        {
            var source = containerWindow as SceneView;
            _context = new SceneCommandContext(source);
            var reason = SceneControls.CommandUnavailable(_command, source);
            SetEnabled(reason == null);
            tooltip = reason ?? (_hint + (ShortcutProfileService.MeshCommandIds.ContainsKey(_command) ? " | Current: " + ShortcutProfiles.CommandHint(_command) : ""));
            var selection = SceneControls.MeshEdit?.Selection;
            var selected = _command == SceneCommand.ObjectMode ? selection == null :
                _command == SceneCommand.MeshMode ? selection != null :
                _command == SceneCommand.Vertex ? selection?.Mode == MeshSelectionMode.Vertex :
                _command == SceneCommand.Edge ? selection?.Mode == MeshSelectionMode.Edge :
                _command == SceneCommand.Face ? selection?.Mode == MeshSelectionMode.Face :
                _command == SceneCommand.XRay ? selection?.XRay == true :
                _command == SceneCommand.Median ? selection?.PivotMode == MeshPivotMode.Median :
                _command == SceneCommand.ActiveElement && selection?.PivotMode == MeshPivotMode.Active;
            SetValueWithoutNotify(selected);
        }
    }

    [EditorToolbarElement("BSC/Counts", typeof(SceneView))]
    internal sealed class MeshCountsElement : EditorToolbarButton, IAccessContainerWindow
    {
        public EditorWindow containerWindow { get; set; }
        public MeshCountsElement()
        {
            style.minWidth = 58; style.flexShrink = 0;
            var refresh = schedule.Execute(Refresh).Every(100);
            refresh.Pause();
            RegisterCallback<AttachToPanelEvent>(_ => { Refresh(); refresh.Resume(); });
            RegisterCallback<DetachFromPanelEvent>(_ => refresh.Pause());
        }
        private void Refresh()
        {
            var mode = SceneControls.MeshEdit;
            var s = mode != null && mode.Source == containerWindow && mode.Valid ? mode.Selection : null;
            text = s == null ? "Object" : $"{s.Selected.Length}/{(s.Mode == MeshSelectionMode.Vertex ? s.Vertices.Count : s.Mode == MeshSelectionMode.Edge ? s.Edges.Count : s.Faces.Count)}";
            tooltip = s == null ? "Object Mode" : $"Selected / total {s.Mode}\nVertices: {s.Document.SelectedVertices.Length}/{s.Vertices.Count}\nEdges: {s.Document.SelectedEdges.Length}/{s.Edges.Count}\nFaces: {s.Document.SelectedFaces.Length}/{s.Faces.Count}";
        }
    }
    [EditorToolbarElement("BSC/Object", typeof(SceneView))] internal sealed class ObjectToolbarButton : SceneToolbarButton { public ObjectToolbarButton() : base("Object", SceneCommand.ObjectMode, "Object Mode") { } }
    [EditorToolbarElement("BSC/Mesh", typeof(SceneView))] internal sealed class MeshToolbarButton : SceneToolbarButton { public MeshToolbarButton() : base("Mesh", SceneCommand.MeshMode, "Mesh Edit Mode (Tab)") { } }
    [EditorToolbarElement("BSC/Vertex", typeof(SceneView))] internal sealed class VertexToolbarButton : SceneToolbarButton { public VertexToolbarButton() : base("V", SceneCommand.Vertex, "Vertex selection (1)") { } }
    [EditorToolbarElement("BSC/Edge", typeof(SceneView))] internal sealed class EdgeToolbarButton : SceneToolbarButton { public EdgeToolbarButton() : base("E", SceneCommand.Edge, "Edge selection (2)") { } }
    [EditorToolbarElement("BSC/Face", typeof(SceneView))] internal sealed class FaceToolbarButton : SceneToolbarButton { public FaceToolbarButton() : base("F", SceneCommand.Face, "Face selection (3)") { } }
    [EditorToolbarElement("BSC/XRay", typeof(SceneView))] internal sealed class XRayToolbarButton : SceneToolbarButton { public XRayToolbarButton() : base("X-Ray", SceneCommand.XRay, "Toggle selection through geometry (Alt+Z)") { } }
    [EditorToolbarElement("BSC/Median", typeof(SceneView))] internal sealed class MedianToolbarButton : SceneToolbarButton { public MedianToolbarButton() : base("Median", SceneCommand.Median, "Pivot: median of unique selected vertices") { } }
    [EditorToolbarElement("BSC/Active", typeof(SceneView))] internal sealed class ActiveToolbarButton : SceneToolbarButton { public ActiveToolbarButton() : base("Active", SceneCommand.ActiveElement, "Pivot: active element; falls back to median") { } }
    [EditorToolbarElement("BSC/Grab", typeof(SceneView))] internal sealed class GrabToolbarButton : SceneToolbarButton { public GrabToolbarButton() : base("G", SceneCommand.Grab, "Grab") { } }
    [EditorToolbarElement("BSC/Rotate", typeof(SceneView))] internal sealed class RotateToolbarButton : SceneToolbarButton { public RotateToolbarButton() : base("R", SceneCommand.Rotate, "Rotate") { } }
    [EditorToolbarElement("BSC/Scale", typeof(SceneView))] internal sealed class ScaleToolbarButton : SceneToolbarButton { public ScaleToolbarButton() : base("S", SceneCommand.Scale, "Scale") { } }
    [EditorToolbarElement("BSC/Frame", typeof(SceneView))] internal sealed class FrameToolbarButton : SceneToolbarButton { public FrameToolbarButton() : base("Frame", SceneCommand.FrameSelected, "Frame selection; empty mesh selection frames the whole mesh") { } }
    [EditorToolbarElement("BSC/All", typeof(SceneView))] internal sealed class AllToolbarButton : SceneToolbarButton { public AllToolbarButton() : base("All", SceneCommand.SelectAll, "Select all elements (A)") { } }
    [EditorToolbarElement("BSC/None", typeof(SceneView))] internal sealed class NoneToolbarButton : SceneToolbarButton { public NoneToolbarButton() : base("None", SceneCommand.DeselectAll, "Deselect all elements (Alt+A)") { } }
    [EditorToolbarElement("BSC/Box", typeof(SceneView))] internal sealed class BoxToolbarButton : SceneToolbarButton { public BoxToolbarButton() : base("Box", SceneCommand.BoxSelect, "Begin box selection in the source viewport (B)") { } }
    [EditorToolbarElement("BSC/Loop", typeof(SceneView))] internal sealed class LoopToolbarButton : SceneToolbarButton { public LoopToolbarButton() : base("Loop", SceneCommand.SelectLoop, "Select loop at the last hovered edge; available with mouse emulation") { } }
    [EditorToolbarElement("BSC/Extrude", typeof(SceneView))] internal sealed class ExtrudeToolbarButton : SceneToolbarButton { public ExtrudeToolbarButton() : base("Extrude", SceneCommand.Extrude, "Extrude") { } }
    [EditorToolbarElement("BSC/LoopCut", typeof(SceneView))] internal sealed class LoopCutToolbarButton : SceneToolbarButton { public LoopCutToolbarButton() : base("Loop Cut", SceneCommand.LoopCut, "Loop Cut and Slide") { } }
    [EditorToolbarElement("BSC/Delete", typeof(SceneView))] internal sealed class DeleteToolbarButton : SceneToolbarButton { public DeleteToolbarButton() : base("Delete", SceneCommand.DeleteDissolve, "Delete / Dissolve") { } }
    [EditorToolbarElement("BSC/Inset", typeof(SceneView))] internal sealed class InsetToolbarButton : SceneToolbarButton { public InsetToolbarButton() : base("Inset", SceneCommand.Inset, "Inset selected faces (I); I toggles Region / Individual") { } }
    [EditorToolbarElement("BSC/ApplyScale", typeof(SceneView))] internal sealed class ApplyScaleToolbarButton : SceneToolbarButton { public ApplyScaleToolbarButton() : base("Apply Scale", SceneCommand.ApplyScale, "Bake object scale into an independent mesh; reset Scale to 1; one Undo") { } }
    [EditorToolbarElement("BSC/Bevel", typeof(SceneView))] internal sealed class BevelToolbarButton : SceneToolbarButton { public BevelToolbarButton() : base("Bevel", SceneCommand.Bevel, "Edge Bevel (Ctrl+B); offset width, wheel segments, full Cancel") { } }
    [EditorToolbarElement("BSC/VertexBevel", typeof(SceneView))] internal sealed class VertexBevelToolbarButton : SceneToolbarButton { public VertexBevelToolbarButton() : base("V Bevel", SceneCommand.VertexBevel, "Vertex Bevel (Ctrl+Shift+B); offset width, wheel segments, full Cancel") { } }
    [EditorToolbarElement("BSC/Quads", typeof(SceneView))] internal sealed class QuadsToolbarButton : SceneToolbarButton { public QuadsToolbarButton() : base("Quads", SceneCommand.TrisToQuads, "Tris to Quads") { } }
    [EditorToolbarElement("BSC/Recover", typeof(SceneView))] internal sealed class RecoverToolbarButton : SceneToolbarButton { public RecoverToolbarButton() : base("Connect", SceneCommand.RecoverConnectivity, "Preview recovery of exact closed boundary seams") { } }
    [EditorToolbarElement("BSC/ConfirmPreparation", typeof(SceneView))] internal sealed class ConfirmPreparationToolbarButton : SceneToolbarButton { public ConfirmPreparationToolbarButton() : base("Confirm", SceneCommand.ConfirmPreparation, "Confirm topology preview (Enter / LMB)") { } }
    [EditorToolbarElement("BSC/CancelPreparation", typeof(SceneView))] internal sealed class CancelPreparationToolbarButton : SceneToolbarButton { public CancelPreparationToolbarButton() : base("Cancel", SceneCommand.CancelPreparation, "Cancel topology preview (Esc / RMB)") { } }
    [EditorToolbarElement("BSC/Save", typeof(SceneView))] internal sealed class SaveToolbarButton : SceneToolbarButton { public SaveToolbarButton() : base("Save", SceneCommand.SaveMesh, "Save Mesh") { } }
    [EditorToolbarElement("BSC/SaveAs", typeof(SceneView))] internal sealed class SaveAsToolbarButton : SceneToolbarButton { public SaveAsToolbarButton() : base("Save As", SceneCommand.SaveMeshAs, "Save Mesh As") { } }
    [EditorToolbarElement("BSC/FBX", typeof(SceneView))] internal sealed class FBXToolbarButton : SceneToolbarButton { public FBXToolbarButton() : base("FBX", SceneCommand.ExportFbx, "Export FBX") { } }
}
