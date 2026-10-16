using UnityEngine;

namespace BlenderSceneControls
{
    internal readonly struct SceneStatusContext
    {
        internal readonly SceneEditorMode EditorMode;
        internal readonly string Operation;
        internal readonly string Phase;
        internal readonly string Status;
        internal readonly string Hints;
        internal readonly string CompactHints;

        internal SceneStatusContext(SceneEditorMode editorMode, string operation, string phase,
            string status, string hints, string compactHints)
        {
            EditorMode = editorMode;
            Operation = operation;
            Phase = phase;
            Status = status;
            Hints = hints;
            CompactHints = compactHints;
        }

        internal static SceneStatusContext ObjectIdle => new SceneStatusContext(SceneEditorMode.Object,
            "", "Idle", "Object Mode", "G/R/S: transform · Shift+D: duplicate · Shift+A: add · F: frame · " + ShortcutProfiles.CommandHint(SceneCommand.ToggleMeshMode) + ": Mesh Edit",
            "G/R/S: transform · F: frame · Mesh Mode toolbar");

        internal static SceneStatusContext MeshIdle(MeshElementSelection selection, bool box)
        {
            var hints = box ? "Drag LMB: box · Shift: extend · Esc/RMB: cancel" :
                $"{ShortcutProfiles.CommandHint(SceneCommand.ToggleMeshMode)}: Object · {ShortcutProfiles.CommandHint(SceneCommand.Vertex)}/{ShortcutProfiles.CommandHint(SceneCommand.Edge)}/{ShortcutProfiles.CommandHint(SceneCommand.Face)}: type · Click/Shift: select · {ShortcutProfiles.CommandHint(SceneCommand.SelectAll)}/{ShortcutProfiles.CommandHint(SceneCommand.DeselectAll)}: all/none · {ShortcutProfiles.CommandHint(SceneCommand.BoxSelect)}: box · {ShortcutProfiles.CommandHint(SceneCommand.XRay)}: X-Ray · E: Extrude · Ctrl+R: Loop Cut · I: Inset · Ctrl+B: Edge Bevel · Ctrl+Shift+B: Vertex Bevel · Loop: cached edge";
            return new SceneStatusContext(SceneEditorMode.MeshEdit, "", box ? "Box Select" : "Idle",
                $"Mesh Edit | {selection.Mode}: {selection.Selected.Length} | X-Ray {(selection.XRay ? "On" : "Off")} | {selection.PivotMode}",
                hints + (box ? "" : " · E: Extrude · I: Inset · Ctrl+B: Edge Bevel · Ctrl+Shift+B: Vertex Bevel · X/Delete: Delete / Dissolve"), box ? hints : "G/R/S: mesh transform · E: Extrude · I: Inset · Ctrl+B: Edge Bevel · Ctrl+Shift+B: Vertex Bevel · X/Delete: Delete / Dissolve · F: frame");
        }

        internal static SceneStatusContext TopologyPreview(string message) => new SceneStatusContext(SceneEditorMode.MeshEdit,
            "Topology", "Preview", "Mesh Edit | " + message,
            "Enter/LMB or Confirm: apply · Esc/RMB or Cancel: restore the complete snapshot",
            "Enter/LMB: confirm · Esc/RMB: full cancel");

        internal static SceneStatusContext Transform(ModalMode mode, GrabState grab, RotateScaleState rotation, bool swapYAndZ, bool mesh = false)
        {
            var axisHint = swapYAndZ ? "X: X · Y: Z · Z: Y" : "XYZ: axis";
            return new SceneStatusContext(mesh ? SceneEditorMode.MeshEdit : SceneEditorMode.Object, mode.ToString(), mesh ? "Mesh Transform" : "Transform",
                (mesh ? "Mesh Edit | " : "") + TransformStatus(mode, grab, rotation),
                $"Enter/LMB: confirm · Esc/RMB: cancel · {axisHint} · Shift+axis: plane/normal · R R: trackball · Shift: precision · Ctrl: snap",
                $"Enter/LMB: confirm · Esc/RMB: cancel\n{axisHint} · Shift+axis: plane · Shift: precision · Ctrl: snap");
        }

        private static string TransformStatus(ModalMode mode, GrabState grab, RotateScaleState rotation)
        {
            var local = grab != null ? grab.Local : rotation?.Local == true;
            var description = grab != null ? grab.Description : rotation?.Description ?? "Diagnostic";
            var number = grab != null ? grab.Number : rotation?.Number;
            var value = grab != null ? grab.Offset.ToString("F3") : rotation == null ? "" :
                mode == ModalMode.Scale ? rotation.Value.ToString("F3") : rotation.Trackball ?
                Quaternion.Angle(Quaternion.identity, rotation.Rotation).ToString("F2") + " deg" : rotation.Value.ToString("F2") + " deg";
            var precision = grab != null ? grab.Precision : rotation?.Precision == true;
            var snap = grab != null ? grab.Snap : rotation?.Snap == true;
            var status = $"{mode} | {(local ? "Local" : "World")} | {description} | {value}";
            if (number?.Active == true) status += " | Input: " + number.Text;
            if (precision) status += number?.Active == true ? " | Precision (number overrides)" : " | Precision";
            if (snap) status += number?.Active == true ? " | Snap (number overrides)" : " | Snap";
            if (rotation?.WaitingForDirection == true || grab != null && grab.Number.Active && grab.Axis < 0 && grab.Offset == Vector3.zero)
                status += " | Move to define direction";
            else if (grab != null && !grab.MouseAvailable) status += " | Type a value: axis faces view";
            return status;
        }

    }
}
