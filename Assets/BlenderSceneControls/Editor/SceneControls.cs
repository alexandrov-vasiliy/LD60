using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.ShortcutManagement;
using UnityEditor.SceneManagement;

namespace BlenderSceneControls
{
    [InitializeOnLoad]
    public static class SceneControls
    {
        private const string ReleaseCommand = "BSC.ReleaseModalControl";
        private const string BeginGrabCommand = "BSC.BeginGrab";
        private const string BeginRotateCommand = "BSC.BeginRotate";
        private const string BeginScaleCommand = "BSC.BeginScale";
        private const string DuplicateCommand = "BSC.Duplicate";
        private const string AddCommand = "BSC.Add";
        private const string PrimitiveCommand = "BSC.Primitive.";
        private static Action<SceneView, int> _createPrimitive;
        private static Action<SceneView, int> _toolbarCommand;
        private static SceneView _toolbarSource;
        private static readonly GrabShortcutContext ShortcutContext = new GrabShortcutContext();
        private static readonly MeshElementShortcutContext MeshShortcutContext = new MeshElementShortcutContext();
        private static ModalSession _session;
        private static int _releaseControl;
        private static SceneView _hoverView;
        private static Vector2 _hoverMouse;
        private static Vector2 _hoverCursor;
        private static Rect _hoverRect;
        private static Rect _hoverViewport;
        private static float _hoverScale;
        public static bool Enabled { get; private set; }
        public static bool Active => _session != null;
        private static MeshEditMode _meshEdit;
        private static SceneView _meshReleaseSource;
        private static int _meshReleaseControl;
        internal static void ReleaseMeshCapture(SceneView source, int control)
        {
            _meshReleaseSource = source; _meshReleaseControl = control;
            if (source)
            {
                SceneView.beforeSceneGui -= ReleasePendingMeshCapture;
                SceneView.beforeSceneGui += ReleasePendingMeshCapture;
                source.Repaint();
            }
        }
        private static void ReleasePendingMeshCapture(SceneView view)
        {
            if (_meshReleaseSource && _meshReleaseSource != view) return;
            if (_meshReleaseSource && GUIUtility.hotControl == _meshReleaseControl) GUIUtility.hotControl = 0;
            _meshReleaseSource = null; _meshReleaseControl = 0;
            SceneView.beforeSceneGui -= ReleasePendingMeshCapture;
        }
        internal static MeshEditMode MeshEdit => _meshEdit;
        internal static SceneEditorMode EditorMode => _meshEdit != null ? SceneEditorMode.MeshEdit : SceneEditorMode.Object;
        public static bool NavigationActive => SceneNavigation.Active;
        private static readonly ToolVisibilityState ToolVisibility = new ToolVisibilityState();
        private static bool _toolsSuspended;
        private static bool _lastEmulation;
        private static bool _configurationInspectorInteracted;
        internal static bool AllowedInState(bool enabled, bool playing, bool enableInPlayMode) =>
            enabled && (!playing || enableInPlayMode);
        public static bool Available => AllowedInState(Enabled, EditorApplication.isPlaying, SceneControlsConfiguration.Current.EnableInPlayMode);
        public static bool EmulateThreeButtonMouse
        {
            get => SceneControlsConfiguration.Current.EmulateThreeButtonMouse;
            set
            {
                if (EmulateThreeButtonMouse == value) return;
                SceneNavigation.End();
                var config = SceneControlsConfiguration.EnsureDefaultAsset();
                config.EmulateThreeButtonMouse = value;
                EditorUtility.SetDirty(config);
            }
        }

        static SceneControls()
        {
            // Import and reload always start disabled. No profile changes or persistent enable flag.
            AssemblyReloadEvents.beforeAssemblyReload += Disable;
            EditorApplication.quitting += Disable;
        }

        [MenuItem("Tools/Blender Scene Controls/Enable", false, 100)]
        public static void Enable()
        {
            if (Enabled) return;
            if (!ScenePointer.Supported) throw new PlatformNotSupportedException("Windows Editor required.");
            Enabled = true;
            _toolsSuspended = false;
            _lastEmulation = EmulateThreeButtonMouse;
            SceneView.beforeSceneGui += OnSceneGUI;
            SceneView.duringSceneGui += OnScenePresentation;
            EditorApplication.update += Tick;
            Selection.selectionChanged += SelectionChanged;
            EditorApplication.playModeStateChanged += PlayModeChanged;
            EditorApplication.focusChanged += FocusChanged;
            Undo.undoRedoPerformed += UndoRedo;
            ShortcutManager.RegisterContext(ShortcutContext);
            ShortcutManager.RegisterContext(MeshShortcutContext);
            UpdateToolVisibility();
            SceneView.RepaintAll();
        }

        [MenuItem("Tools/Blender Scene Controls/Enable", true)]
        private static bool CanEnable() => !Enabled;

        [MenuItem("Tools/Blender Scene Controls/Disable", false, 101)]
        public static void Disable()
        {
            try { SceneNavigation.End(); End(ModalTermination.Abort, false); ExitMeshEdit(); }
            finally
            {
                Enabled = false;
                ToolVisibility.Restore();
                ScenePresentation.ResetCursor();
                SceneCursor.ReleaseInput();
                SceneView.beforeSceneGui -= OnSceneGUI;
                SceneView.duringSceneGui -= OnScenePresentation;
                EditorApplication.update -= Tick;
                Selection.selectionChanged -= SelectionChanged;
                EditorApplication.playModeStateChanged -= PlayModeChanged;
                EditorApplication.focusChanged -= FocusChanged;
                Undo.undoRedoPerformed -= UndoRedo;
                ShortcutManager.UnregisterContext(ShortcutContext);
                ShortcutManager.UnregisterContext(MeshShortcutContext);
                _hoverView = null;
                _createPrimitive = null;
                _toolbarCommand = null;
                _toolbarSource = null;
                SceneView.RepaintAll();
            }
        }

        [MenuItem("Tools/Blender Scene Controls/Disable", true)]
        private static bool CanDisable() => Enabled;

        private static void SelectionChanged()
        {
            SceneNavigation.End(); End(ModalTermination.Abort, false, "selection changed");
            if (_meshEdit != null && !_meshEdit.MatchesUnitySelection) ExitMeshEdit();
        }
        private static void PlayModeChanged(PlayModeStateChange state)
        {
            _toolsSuspended = state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.ExitingPlayMode;
            SceneNavigation.End();
            End(ModalTermination.Abort, false, "Play transition");
            ExitMeshEdit();
            SceneCursor.ReleaseInput();
            UpdateToolVisibility();
        }
        private static void FocusChanged(bool focused)
        {
            _toolsSuspended = !focused;
            if (!focused) { ExitMeshEdit(); SceneNavigation.End(); End(ModalTermination.Abort, false, "application focus lost"); ScenePresentation.ResetCursor(); SceneCursor.ReleaseInput(); }
            UpdateToolVisibility();
        }
        private static void UndoRedo() { SceneNavigation.End(); End(ModalTermination.Abort, false, "Undo/Redo"); ExitMeshEdit(); }

        private static void UpdateToolVisibility() => ToolVisibility.Update(!_toolsSuspended &&
            (Available && _meshEdit != null || ToolVisibilityState.ShouldHide(SceneControlsConfiguration.Current.ToolVisibility, Available, Active)));

        internal static int AxisForKey(KeyCode key, bool swapYAndZ) => key == KeyCode.X ? 0 :
            key == KeyCode.Y ? (swapYAndZ ? 2 : 1) : key == KeyCode.Z ? (swapYAndZ ? 1 : 2) : -1;

        // A command definition, not a profile rewrite. Context is inactive on import/Disable.
        [Shortcut("Blender Scene Controls/Grab", typeof(GrabShortcutContext), KeyCode.G)]
        private static void GrabShortcut()
        {
            if (CanStartGrab) _hoverView.SendEvent(EditorGUIUtility.CommandEvent(BeginGrabCommand));
        }

        [Shortcut("Blender Scene Controls/Rotate", typeof(GrabShortcutContext), KeyCode.R)]
        private static void RotateShortcut()
        {
            if (CanStartGrab) _hoverView.SendEvent(EditorGUIUtility.CommandEvent(BeginRotateCommand));
        }

        [Shortcut("Blender Scene Controls/Scale", typeof(GrabShortcutContext), KeyCode.S)]
        private static void ScaleShortcut()
        {
            if (CanStartGrab) _hoverView.SendEvent(EditorGUIUtility.CommandEvent(BeginScaleCommand));
        }

        [Shortcut("Blender Scene Controls/Duplicate", typeof(GrabShortcutContext), KeyCode.D, ShortcutModifiers.Shift)]
        private static void DuplicateShortcut()
        {
            if (CanStartGrab) _hoverView.SendEvent(EditorGUIUtility.CommandEvent(DuplicateCommand));
        }

        [Shortcut("Blender Scene Controls/Add Primitive", typeof(GrabShortcutContext), KeyCode.A, ShortcutModifiers.Shift)]
        private static void AddShortcut()
        {
            if (CanStart) _hoverView.SendEvent(EditorGUIUtility.CommandEvent(AddCommand));
        }

        [Shortcut("Blender Scene Controls/Frame Selected", typeof(GrabShortcutContext), KeyCode.F)]
        private static void FrameShortcut()
        {
            if (CanStart) TryExecute(SceneCommand.FrameSelected, _hoverView, 0);
        }

        [Shortcut("Blender Scene Controls/Mesh Mode", typeof(GrabShortcutContext), KeyCode.Tab)]
        private static void MeshModeShortcut() => MeshShortcut(SceneCommand.ToggleMeshMode);
        [Shortcut("Blender Scene Controls/Vertex", typeof(MeshElementShortcutContext), KeyCode.Alpha1)]
        private static void VertexShortcut() => MeshShortcut(SceneCommand.Vertex);
        [Shortcut("Blender Scene Controls/Edge", typeof(MeshElementShortcutContext), KeyCode.Alpha2)]
        private static void EdgeShortcut() => MeshShortcut(SceneCommand.Edge);
        [Shortcut("Blender Scene Controls/Face", typeof(MeshElementShortcutContext), KeyCode.Alpha3)]
        private static void FaceShortcut() => MeshShortcut(SceneCommand.Face);
        [Shortcut("Blender Scene Controls/X-Ray", typeof(MeshElementShortcutContext), KeyCode.Z, ShortcutModifiers.Alt)]
        private static void XRayShortcut() => MeshShortcut(SceneCommand.XRay);
        [Shortcut("Blender Scene Controls/Select All Elements", typeof(MeshElementShortcutContext), KeyCode.A)]
        private static void AllElementsShortcut() => MeshShortcut(SceneCommand.SelectAll);
        [Shortcut("Blender Scene Controls/Deselect All Elements", typeof(MeshElementShortcutContext), KeyCode.A, ShortcutModifiers.Alt)]
        private static void NoElementsShortcut() => MeshShortcut(SceneCommand.DeselectAll);
        [Shortcut("Blender Scene Controls/Box Select Elements", typeof(MeshElementShortcutContext), KeyCode.B)]
        private static void BoxElementsShortcut() => MeshShortcut(SceneCommand.BoxSelect);
        private static void MeshShortcut(SceneCommand command)
        {
            if (CanStart) TryExecute(command, _hoverView, 0);
        }

        internal static bool CanStart => Available && !Active && !SceneNavigation.Active && GUIUtility.hotControl == 0 && _hoverView &&
            EditorWindow.mouseOverWindow == _hoverView && !TextIsEditing() && ScenePointer.IsForeground &&
            _hoverRect == _hoverView.position && _hoverViewport == _hoverView.cameraViewport;

        internal static Transform[] SelectedTransforms => Selection.gameObjects.Select(go => go.transform).ToArray();
        internal static bool CanStartGrab => CanStart && (_meshEdit != null
            ? _meshEdit.Valid && _meshEdit.MatchesUnitySelection && !_meshEdit.SelectingBox && !_meshEdit.Preparing && _meshEdit.Selection.Selected.Length > 0
            : SceneCreation.CanTransform(SelectedTransforms));

        // Called in the source GUI for commands needing a control ID. Shortcut callbacks
        // keep their native ExecuteCommand transport; future toolbars must supply their view.
        internal static bool TryExecute(SceneCommand command, SceneView source, int control)
        {
            if (!Available || Active || SceneNavigation.Active || !source || TextIsEditing() ||
                GUIUtility.hotControl != 0 || !ScenePointer.IsForeground || EditorWindow.mouseOverWindow != source)
                return false;
            if (command == SceneCommand.Extrude)
                return CommandUnavailable(command, source) == null && BeginTransform(source, control, ModalMode.Grab, true);
            if (command == SceneCommand.LoopCut || command == SceneCommand.Inset || command == SceneCommand.Bevel || command == SceneCommand.VertexBevel)
            {
                if (CommandUnavailable(command, source) != null || source != _hoverView) return false;
                var cursor = ScenePointer.Read();
                var mouse = _hoverMouse + (cursor - _hoverCursor) / _hoverScale;
                if (!source.cameraViewport.Contains(mouse)) return false;
                if (command == SceneCommand.Bevel || command == SceneCommand.VertexBevel) StartBevel(source, control, mouse, cursor, command == SceneCommand.VertexBevel);
                else if (command == SceneCommand.Inset) StartInset(source, control, mouse, cursor);
                else StartLoopCut(source, control, mouse, cursor);
                return true;
            }
            if (command >= SceneCommand.ToggleMeshMode)
                return ExecuteModeCommand(command, source);
            if (_meshEdit != null)
            {
                if (!_meshEdit.Valid || !_meshEdit.MatchesUnitySelection || source != _meshEdit.Source) return false;
                if (command == SceneCommand.Grab || command == SceneCommand.Rotate || command == SceneCommand.Scale)
                    return BeginTransform(source, control, command == SceneCommand.Grab ? ModalMode.Grab : command == SceneCommand.Rotate ? ModalMode.Rotate : ModalMode.Scale);
                if (command != SceneCommand.FrameSelected) return false;
                _meshEdit.Frame();
                return true;
            }
            switch (command)
            {
                case SceneCommand.Grab: return BeginTransform(source, control, ModalMode.Grab);
                case SceneCommand.Rotate: return BeginTransform(source, control, ModalMode.Rotate);
                case SceneCommand.Scale: return BeginTransform(source, control, ModalMode.Scale);
                case SceneCommand.Duplicate: return BeginCreation(source, control, true);
                case SceneCommand.AddPrimitive: return BeginCreation(source, control, false);
                case SceneCommand.FrameSelected: return SceneNavigation.FocusSelection(source);
                default: return false;
            }
        }

        internal static string CommandUnavailable(SceneCommand command, SceneView source)
        {
            if (!Available) return "Enable Blender Scene Controls first; Mesh Edit is unavailable in Play Mode.";
            if (!source) return "A source Scene View is required.";
            if (Active || SceneNavigation.Active || GUIUtility.hotControl != 0 || _meshEdit?.SelectingBox == true)
                return "Finish the current operation or native capture first.";
            if (TextIsEditing()) return "Finish editing the text field first.";
            if (_meshEdit != null && (!_meshEdit.Valid || !_meshEdit.MatchesUnitySelection || _meshEdit.Source != source))
                return "Use the original Mesh Edit Scene View and target.";
            if (command == SceneCommand.ApplyScale || command == SceneCommand.ApplyScaleMenu)
            {
                if (_meshEdit?.Preparing == true) return "Finish topology preparation first.";
                var selected = Selection.gameObjects;
                var target = _meshEdit != null ? _meshEdit.Target : selected.Length == 1 ? selected[0].GetComponent<MeshFilter>() : null;
                return MeshApplyScale.Unavailable(target);
            }
            if (command == SceneCommand.ConfirmPreparation || command == SceneCommand.CancelPreparation)
                return _meshEdit?.Preparing == true ? null : "Start a topology preparation preview first.";
            if (_meshEdit?.Preparing == true) return "Confirm or cancel the topology preparation first.";
            if (command >= SceneCommand.SaveMesh && command <= SceneCommand.ExportFbx)
            {
                var selected = Selection.gameObjects;
                var target = _meshEdit != null ? _meshEdit.Target : selected.Length == 1 ? selected[0].GetComponent<MeshFilter>() : null;
                return MeshSaveExport.Unavailable(target);
            }
            if (command >= SceneCommand.DeleteDissolve && command <= SceneCommand.DissolveFaces)
                return _meshEdit == null ? "Enter Mesh Edit Mode first." :
                    _meshEdit.Selection.Selected.Length == 0 ? "Select mesh elements first." : null;
            if (command == SceneCommand.RecoverConnectivity || command == SceneCommand.TrisToQuads)
                return _meshEdit == null ? "Enter Mesh Edit Mode first." :
                    command == SceneCommand.RecoverConnectivity && !_meshEdit.CanRecoverConnectivity ? "Authored or companion topology is preserved; recovery is for triangle imports only." : null;
            if (command == SceneCommand.Extrude)
                return _meshEdit == null ? "Enter Mesh Edit Mode first." :
                    _meshEdit.Selection.Selected.Length == 0 ? "Select mesh elements first." : null;
            if (command == SceneCommand.Bevel || command == SceneCommand.VertexBevel)
                return _meshEdit == null ? "Enter Mesh Edit Mode first." :
                    _meshEdit.Selection.Mode != (command == SceneCommand.VertexBevel ? MeshSelectionMode.Vertex : MeshSelectionMode.Edge) || _meshEdit.Selection.Selected.Length == 0 ?
                    (command == SceneCommand.VertexBevel ? "Select vertices for Vertex Bevel." : "Select edges for Edge Bevel.") : null;
            if (command == SceneCommand.Inset)
                return _meshEdit == null ? "Enter Mesh Edit Mode first." :
                    _meshEdit.Selection.Mode != MeshSelectionMode.Face || _meshEdit.Selection.Selected.Length == 0 ? "Select faces for Inset." : null;
            if (command == SceneCommand.LoopCut)
                return _meshEdit == null ? "Enter Mesh Edit Mode first." : null;
            if (command == SceneCommand.Grab || command == SceneCommand.Rotate || command == SceneCommand.Scale)
                return _meshEdit != null ? (_meshEdit.Selection.Selected.Length > 0 ? null : "Select mesh elements first.") :
                    SceneCreation.CanTransform(SelectedTransforms) ? null : "Select transformable scene objects.";
            if (command == SceneCommand.ObjectMode || command == SceneCommand.FrameSelected) return null;
            if (command == SceneCommand.ToggleMeshMode || command == SceneCommand.MeshMode)
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return "Mesh Edit is unavailable in Play Mode.";
                if (_meshEdit != null) return null;
                var selected = Selection.gameObjects;
                var filter = selected.Length == 1 ? selected[0].GetComponent<MeshFilter>() : null;
                return filter && filter.sharedMesh && selected[0].scene.IsValid() &&
                    PrefabStageUtility.GetCurrentPrefabStage() == null ? null : "Select one scene object with a supported static MeshFilter outside Prefab Stage.";
            }
            if (command >= SceneCommand.Vertex && command <= SceneCommand.SelectLoop)
                return _meshEdit == null ? "Enter Mesh Edit Mode first." :
                    command == SceneCommand.SelectLoop && !_meshEdit.HasLoopSeed ? "Hover an edge in Edge or Face mode first; the button uses that cached edge." : null;
            return "This command is unavailable here.";
        }

        internal static bool ExecuteToolbar(SceneCommand command, SceneCommandContext context)
        {
            if (context == null || !context.Current || CommandUnavailable(command, context.Source) != null || !ScenePointer.IsForeground) return false;
            var source = context.Source;
            if (command == SceneCommand.Grab || command == SceneCommand.Rotate || command == SceneCommand.Scale || command == SceneCommand.Extrude || command == SceneCommand.LoopCut || command == SceneCommand.Inset || command == SceneCommand.Bevel || command == SceneCommand.VertexBevel)
            {
                _toolbarSource = source;
                _toolbarCommand = (view, control) =>
                {
                    if (view != source || !context.Current || CommandUnavailable(command, view) != null || !ScenePointer.IsForeground) return;
                    // UITK has no IMGUI control ID. Acquire it on the next real source GUI callback.
                    _hoverScale = EditorGUIUtility.pixelsPerPoint;
                    if (command == SceneCommand.Bevel || command == SceneCommand.VertexBevel)
                    {
                        StartBevel(view, control, view.cameraViewport.center, ScenePointer.Read(), command == SceneCommand.VertexBevel);
                        return;
                    }
                    if (command == SceneCommand.Inset)
                    {
                        StartInset(view, control, view.cameraViewport.center, ScenePointer.Read());
                        return;
                    }
                    if (command == SceneCommand.LoopCut)
                    {
                        StartLoopCut(view, control, view.cameraViewport.center, ScenePointer.Read());
                        return;
                    }
                    StartSession(view, control, command == SceneCommand.Grab ? ModalMode.Grab :
                        command == SceneCommand.Rotate ? ModalMode.Rotate : command == SceneCommand.Scale ? ModalMode.Scale : ModalMode.Grab,
                        view.cameraViewport.center, ScenePointer.Read(), extrude: command == SceneCommand.Extrude);
                };
                source.Repaint();
                return true;
            }
            if (command == SceneCommand.FrameSelected)
                return _meshEdit != null ? _meshEdit.Execute(command) : SceneNavigation.FocusSelection(source);
            return ExecuteModeCommand(command, source);
        }

        private static bool ExecuteModeCommand(SceneCommand command, SceneView source)
        {
            if (CommandUnavailable(command, source) != null) return false;
            if (command >= SceneCommand.SaveMesh && command <= SceneCommand.ExportFbx)
            {
                try
                {
                    var target = _meshEdit != null ? _meshEdit.Target : Selection.activeGameObject.GetComponent<MeshFilter>();
                    var done = MeshSaveExport.Execute(command, target, _meshEdit, new SceneCommandContext(source));
                    if (done) source.ShowNotification(new GUIContent(command == SceneCommand.ExportFbx ? "FBX exported (local geometry, metres)" : "Mesh pair saved; scene Save remains explicit"));
                    return done;
                }
                catch (Exception error) { source.ShowNotification(new GUIContent(error.Message)); return false; }
            }
            if (command == SceneCommand.ApplyScaleMenu)
            {
                var context = new SceneCommandContext(source);
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("Scale"), false, () => ExecuteToolbar(SceneCommand.ApplyScale, context));
                menu.ShowAsContext();
                return true;
            }
            if (command == SceneCommand.ApplyScale)
            {
                try
                {
                    var target = _meshEdit != null ? _meshEdit.Target : Selection.activeGameObject.GetComponent<MeshFilter>();
                    var changed = MeshApplyScale.Apply(target, _meshEdit);
                    source.ShowNotification(new GUIContent(changed ? "Scale applied: (1, 1, 1)" : "Scale is already (1, 1, 1)"));
                    source.Repaint();
                    return true;
                }
                catch (Exception error) { source.ShowNotification(new GUIContent(error.Message)); return false; }
            }
            if (command == SceneCommand.DeleteDissolve)
            {
                var context = new SceneCommandContext(source);
                var menu = new GenericMenu();
                var labels = new[] { "Vertices", "Edges", "Faces", "Only Edges & Faces", "Only Faces", "Dissolve Vertices", "Dissolve Edges", "Dissolve Faces" };
                for (var i = 0; i < labels.Length; i++)
                {
                    var choice = (SceneCommand)((int)SceneCommand.DeleteVertices + i);
                    menu.AddItem(new GUIContent(labels[i]), false, () => ExecuteToolbar(choice, context));
                }
                menu.ShowAsContext();
                return true;
            }
            if (command == SceneCommand.ObjectMode || command == SceneCommand.ToggleMeshMode && _meshEdit != null)
            { ExitMeshEdit(); source.Repaint(); return true; }
            if (command == SceneCommand.MeshMode || command == SceneCommand.ToggleMeshMode)
            {
                if (_meshEdit != null) return true;
                try
                {
                    _meshEdit = new MeshEditMode(Selection.activeGameObject.GetComponent<MeshFilter>(), source);
                    source.Focus(); UpdateToolVisibility(); source.Repaint(); return true;
                }
                catch (InvalidOperationException exception) { source.ShowNotification(new GUIContent(exception.Message)); return false; }
            }
            try { return _meshEdit != null && _meshEdit.Execute(command); }
            catch (InvalidOperationException exception) { source.ShowNotification(new GUIContent(exception.Message)); return false; }
        }

        private static bool BeginTransform(SceneView view, int control, ModalMode mode, bool extrude = false)
        {
            if (!CanStartGrab || view != _hoverView || GUIUtility.hotControl != 0) return false;
            var cursor = ScenePointer.Read();
            var mouse = _hoverMouse + (cursor - _hoverCursor) / _hoverScale;
            if (!view.cameraViewport.Contains(mouse)) return false;
            StartSession(view, control, mode, mouse, cursor, extrude: extrude);
            return true;
        }

        private static void StartSession(SceneView view, int control, ModalMode mode, Vector2 mouse, Vector2 cursor, SceneCreation creation = null, bool extrude = false)
        {
            view.Focus();
            MeshTransformTransaction meshTarget = null;
            ModalSession session;
            try
            {
                meshTarget = _meshEdit != null ? new MeshTransformTransaction(_meshEdit, extrude) : null;
                session = meshTarget != null ? new ModalSession(view, control, mouse, cursor, _hoverScale, mode, meshTarget, view.cameraViewport)
                    : new ModalSession(view, control, mouse, cursor, _hoverScale, mode, creation);
                if (extrude)
                {
                    session.SetPhase(new ExtrudeMovePhase());
                    if (meshTarget.RegionNormal != Vector3.zero) session.Grab.ConstrainNormal(meshTarget.RegionNormal, mouse);
                }
            }
            catch { meshTarget?.Cancel(); throw; }
            _session = session;
            view.wantsMouseMove = true;
            UpdateToolVisibility();
            GUIUtility.hotControl = control;
            Debug.Log($"BSC {mode} began. Move mouse; X/Y/Z axis; Shift+axis plane/normal; R again: trackball; type a value; Enter/LMB confirm, Esc/RMB cancel.");
        }

        private static void StartBevel(SceneView view, int control, Vector2 mouse, Vector2 cursor, bool vertexOnly)
        {
            view.Focus();
            var target = new MeshBevelTransaction(_meshEdit, vertexOnly);
            try
            {
                var session = new ModalSession(view, control, mouse, cursor, _hoverScale, ModalMode.Diagnostic, target, view.cameraViewport);
                session.SetPhase(new BevelPhase(session, mouse));
                _session = session;
                view.wantsMouseMove = true;
                UpdateToolVisibility();
                GUIUtility.hotControl = control;
            }
            catch { target.Cancel(); throw; }
        }

        private static void StartInset(SceneView view, int control, Vector2 mouse, Vector2 cursor)
        {
            view.Focus();
            var target = new MeshInsetTransaction(_meshEdit);
            try
            {
                var session = new ModalSession(view, control, mouse, cursor, _hoverScale, ModalMode.Diagnostic, target, view.cameraViewport);
                session.SetPhase(new InsetPhase(session, mouse));
                _session = session;
                view.wantsMouseMove = true;
                UpdateToolVisibility();
                GUIUtility.hotControl = control;
            }
            catch { target.Cancel(); throw; }
        }

        private static void StartLoopCut(SceneView view, int control, Vector2 mouse, Vector2 cursor)
        {
            view.Focus();
            var target = new MeshLoopCutTransaction(_meshEdit);
            try
            {
                var session = new ModalSession(view, control, mouse, cursor, _hoverScale, ModalMode.Diagnostic, target, view.cameraViewport);
                session.SetPhase(new LoopCutPreviewPhase());
                target.Hover(target.Pick(mouse));
                _session = session;
                view.wantsMouseMove = true;
                UpdateToolVisibility();
                GUIUtility.hotControl = control;
            }
            catch { target.Cancel(); throw; }
        }

        private static bool BeginCreation(SceneView view, int control, bool duplicate)
        {
            if (!CanStart || duplicate && !SceneCreation.CanDuplicate(SelectedTransforms) || view != _hoverView || GUIUtility.hotControl != 0) return false;
            var cursor = ScenePointer.Read();
            var mouse = _hoverMouse + (cursor - _hoverCursor) / _hoverScale;
            if (!view.cameraViewport.Contains(mouse)) return false;
            if (duplicate)
            {
                LaunchCreation(view, control, mouse, SceneCreation.Duplicate(SelectedTransforms, Selection.activeTransform));
                return true;
            }
            var stage = StageUtility.GetCurrentStageHandle();
            var rect = view.position;
            var viewport = view.cameraViewport;
            var scale = _hoverScale;
            var pivot = SceneCursor.CreationPosition(SceneControlsConfiguration.Current, view.pivot);
            var cursorEnabled = SceneControlsConfiguration.Current.Enable3DCursor;
            var menu = new GenericMenu();
            foreach (PrimitiveType type in Enum.GetValues(typeof(PrimitiveType)))
            {
                var primitive = type;
                menu.AddItem(new GUIContent(type.ToString()), false, () =>
                {
                    // Menu pointer travel must not become the first Grab delta. Keep the source/position,
                    // then establish a new native cursor origin when the item is chosen.
                    if (!Available || Active || !view || !ScenePointer.IsForeground || TextIsEditing() ||
                        view.position != rect || view.cameraViewport != viewport ||
                        SceneControlsConfiguration.Current.Enable3DCursor != cursorEnabled ||
                        StageUtility.GetCurrentStageHandle() != stage) return;
                    _createPrimitive = (source, id) =>
                    {
                        if (source != view || GUIUtility.hotControl != 0) return;
                        _hoverScale = scale;
                        var menuMouse = mouse + (ScenePointer.Read() - cursor) / scale;
                        LaunchCreation(view, id, menuMouse, SceneCreation.Primitive(primitive, pivot));
                    };
                    try { view.SendEvent(EditorGUIUtility.CommandEvent(PrimitiveCommand)); }
                    finally { _createPrimitive = null; }
                });
            }
            menu.ShowAsContext();
            return true;
        }

        private static void LaunchCreation(SceneView view, int control, Vector2 mouse, SceneCreation creation)
        {
            try
            {
                creation.Select();
                StartSession(view, control, ModalMode.Grab, mouse, ScenePointer.Read(), creation);
            }
            catch
            {
                // Creation remains available and undoable even if acquiring the modal session fails.
                creation.RecordUndo("Blender Scene Controls Create");
                throw;
            }
        }

        internal static bool TextIsEditing()
        {
            return EditorGUIUtility.editingTextField || HasFocusedTextElement();
        }

        private static bool HasFocusedTextElement()
        {
            var element = EditorWindow.focusedWindow?.rootVisualElement?.panel?.focusController?.focusedElement as VisualElement;
            for (; element != null; element = element.parent)
                if (element.ClassListContains(TextInputBaseField<string>.ussClassName)) return true;
            return false;
        }

        internal static void ConfigurationInspectorInteracted() => _configurationInspectorInteracted = true;

        internal static void ReleaseConfigurationTextFocus(bool sceneClick)
        {
            if (!_configurationInspectorInteracted || !sceneClick) return;
            _configurationInspectorInteracted = false;
            // The color picker's text editor can leave this global flag latched after closing.
            // A real click back in the scene ends that Inspector edit. Native scene fields still
            // receive the click below and can start their own text editing normally.
            EditorGUIUtility.editingTextField = false;
        }

        private static void Tick()
        {
            if (!Available)
            {
                SceneNavigation.End();
                End(ModalTermination.Abort, false, "disabled in Play Mode");
            }
            if (_lastEmulation != EmulateThreeButtonMouse)
            {
                SceneNavigation.End();
                _lastEmulation = EmulateThreeButtonMouse;
            }
            UpdateToolVisibility();
            try { SceneNavigation.Tick(); }
            catch (Exception exception) { HandleException(exception); }
            if (_meshEdit != null && (!_meshEdit.Valid || !_meshEdit.MatchesUnitySelection ||
                EditorWindow.focusedWindow != _meshEdit.Source)) ExitMeshEdit();
            if (_meshEdit?.Preparing == true && (!_meshEdit.PreparationValid || !ScenePointer.IsForeground || TextIsEditing() || GUIUtility.hotControl != 0))
                _meshEdit.CancelPreparation();
            if (!Active) return;
            try
            {
                var session = _session;
                if (!ScenePointer.IsForeground || !session.Source || !session.SelectionMatches ||
                    !session.Transaction.IsValid || EditorWindow.focusedWindow != session.InitialFocus ||
                    session.Source.position != session.WindowRect || session.Source.cameraViewport != session.Viewport ||
                    TextIsEditing() || Tools.current != session.PreviousTool)
                {
                    End(ModalTermination.Abort, false, "source/focus/selection/tool changed or text input");
                    return;
                }
                var cursor = ScenePointer.Read();
                session.Sample(cursor);
                session.Move(session.StartMouse + session.MouseDelta);
                var wrapped = ScenePointer.Wrap(cursor, session.PixelBounds, 4 * session.PixelsPerPoint);
                if (wrapped != cursor) session.PreviousCursor = ScenePointer.Move(wrapped);
                session.Source.Repaint();
            }
            catch (Exception exception) { HandleException(exception); }
        }

        private static void OnSceneGUI(SceneView view)
        {
            var current = Event.current;
            if (_meshReleaseSource == view && _meshReleaseControl != 0)
            {
                if (GUIUtility.hotControl == _meshReleaseControl) GUIUtility.hotControl = 0;
                _meshReleaseSource = null; _meshReleaseControl = 0;
            }
            if (SceneNavigation.ReleaseEvent(current)) return;
            if (current.type == EventType.ExecuteCommand && current.commandName == ReleaseCommand)
            {
                if (GUIUtility.hotControl == _releaseControl) GUIUtility.hotControl = 0;
                current.Use();
                return;
            }
            if (!Available) return;
            try
            {
                ReleaseConfigurationTextFocus(!Active && !SceneNavigation.Active && current.type == EventType.MouseDown &&
                    current.button == 0 && GUIUtility.hotControl == 0 && EditorWindow.focusedWindow == view &&
                    EditorWindow.mouseOverWindow == view && view.cameraViewport.Contains(current.mousePosition) &&
                    !HasFocusedTextElement());
                var control = GUIUtility.GetControlID("BSC Modal Session".GetHashCode(), FocusType.Passive);
                if (current.type == EventType.MouseMove && view.cameraViewport.Contains(current.mousePosition) && EditorWindow.mouseOverWindow == view)
                {
                    _hoverView = view; _hoverMouse = current.mousePosition; _hoverCursor = ScenePointer.Read();
                    _hoverRect = view.position; _hoverViewport = view.cameraViewport; _hoverScale = EditorGUIUtility.pixelsPerPoint;
                }
                if (_toolbarCommand != null && !Active && current.type == EventType.Layout)
                {
                    var pending = _toolbarCommand;
                    // Leave another Scene View's launch pending until its own callback arrives.
                    if (_toolbarSource == view)
                    { _toolbarCommand = null; _toolbarSource = null; pending(view, control); }
                }
                if (!Active)
                {
                    if (HandleMeshMode(view, current, control)) return;
                    if (SceneCursor.Handle(view, current)) return;
                    if (SceneNavigation.Handle(view, current, control)) return;
                    if (current.type == EventType.ExecuteCommand && current.commandName == PrimitiveCommand && _createPrimitive != null)
                    {
                        _createPrimitive(view, control);
                        current.Use();
                        return;
                    }
                    if (_meshEdit != null && _meshEdit.Source != view) return;
                    if (current.type == EventType.MouseMove && EditorWindow.mouseOverWindow == view)
                    {
                        _hoverView = view;
                        _hoverMouse = current.mousePosition;
                        _hoverCursor = ScenePointer.Read();
                        _hoverRect = view.position;
                        _hoverViewport = view.cameraViewport;
                        _hoverScale = EditorGUIUtility.pixelsPerPoint;
                    }
                    if ((current.type == EventType.ExecuteCommand && (current.commandName == DuplicateCommand || current.commandName == AddCommand)) ||
                        ShortcutProfiles.Matches("Blender Scene Controls/Duplicate", current) ||
                        ShortcutProfiles.Matches("Blender Scene Controls/Add Primitive", current))
                    {
                        var command = current.commandName == DuplicateCommand ||
                            ShortcutProfiles.Matches("Blender Scene Controls/Duplicate", current) ? SceneCommand.Duplicate : SceneCommand.AddPrimitive;
                        if (TryExecute(command, view, control)) current.Use();
                        return;
                    }
                    if ((current.type == EventType.ExecuteCommand && (current.commandName == BeginGrabCommand ||
                         current.commandName == BeginRotateCommand || current.commandName == BeginScaleCommand)) ||
                        ShortcutProfiles.Matches("Blender Scene Controls/Grab", current) ||
                        ShortcutProfiles.Matches("Blender Scene Controls/Rotate", current) ||
                        ShortcutProfiles.Matches("Blender Scene Controls/Scale", current))
                    {
                        var command = current.commandName == BeginRotateCommand || ShortcutProfiles.Matches("Blender Scene Controls/Rotate", current) ? SceneCommand.Rotate :
                            current.commandName == BeginScaleCommand || ShortcutProfiles.Matches("Blender Scene Controls/Scale", current) ? SceneCommand.Scale : SceneCommand.Grab;
                        if (TryExecute(command, view, control)) current.Use();
                    }
                    return;
                }
                var active = _session;
                if (active.Source != view) return;
                if ((current.type == EventType.ExecuteCommand || current.type == EventType.ValidateCommand) &&
                    current.commandName == "FrameSelected")
                { current.Use(); return; }
                if (view.cameraViewport != active.Viewport ||
                    Mathf.Abs(EditorGUIUtility.pixelsPerPoint - active.PixelsPerPoint) > 0.001f ||
                    GUIUtility.hotControl != active.Control)
                { End(ModalTermination.Abort, true, "viewport/DPI/capture changed"); return; }
                var result = active.Phase.HandleInput(active, new ModalInput(current));
                if (result == ModalInputResult.Confirm || result == ModalInputResult.CancelStage)
                {
                    current.Use();
                    End(result == ModalInputResult.Confirm ? ModalTermination.Confirm : ModalTermination.CancelStage, true, "input");
                    return;
                }
                if (result == ModalInputResult.Consumed) current.Use();
                if (current.type == EventType.Layout) HandleUtility.AddDefaultControl(active.Control);
                if (current.isMouse || current.isKey || current.type == EventType.ScrollWheel) current.Use();
            }
            catch (Exception exception)
            {
                HandleException(exception);
                if (exception is ExitGUIException) throw;
            }
        }

        private static void OnScenePresentation(SceneView view)
        {
            if (!Available) return;
            try
            {
                SceneCursor.Draw(view);
                if (_meshEdit != null && _meshEdit.Source == view && _meshEdit.Valid) _meshEdit.Draw();
                if (_session == null)
                {
                    if (_meshEdit == null)
                    {
                        Handles.BeginGUI();
                        try { ScenePresentation.Hud(view, view.cameraViewport, SceneStatusContext.ObjectIdle, SceneControlsConfiguration.Current); }
                        finally { Handles.EndGUI(); }
                    }
                    return;
                }
                if (_session.Source != view) return;
                if (_session.Transaction is MeshLoopCutTransaction loopCut) loopCut.Draw();
                ScenePresentation.Cursor(_session);
                ScenePresentation.Draw(_session);
            }
            catch (Exception exception) { HandleException(exception); }
        }

        private static bool HandleMeshMode(SceneView view, Event current, int control)
        {
            if (_meshEdit != null && !_meshEdit.Valid) ExitMeshEdit();
            if (_meshEdit != null && _meshEdit.Source == view && _meshEdit.BoxControl != 0)
            {
                if (TextIsEditing() || GUIUtility.hotControl != _meshEdit.BoxControl) _meshEdit.CancelBox();
                else return _meshEdit.Handle(current);
            }
            if (SceneNavigation.Active || TextIsEditing() || GUIUtility.hotControl != 0 ||
                !ScenePointer.IsForeground || EditorWindow.mouseOverWindow != view ||
                !view.cameraViewport.Contains(current.mousePosition)) return false;
            if (_meshEdit != null && _meshEdit.Source != view) return false;
            if (current.type == EventType.KeyDown)
            {
                foreach (var entry in ShortcutProfileService.MeshCommandIds)
                {
                    if (!ShortcutProfiles.Matches(entry.Value, current)) continue;
                    if (TryExecute(entry.Key, view, 0)) { current.Use(); return true; }
                }
            }
            if (current.type == EventType.KeyDown && current.keyCode == KeyCode.A && current.control &&
                !current.shift && !current.alt && !current.command)
            { if (TryExecute(SceneCommand.ApplyScaleMenu, view, control)) current.Use(); return true; }
            if (_meshEdit == null) return false;
            if (!_meshEdit.Preparing && current.type == EventType.KeyDown && current.keyCode == KeyCode.B &&
                current.control && !current.command && !current.alt)
            { if (TryExecute(current.shift ? SceneCommand.VertexBevel : SceneCommand.Bevel, view, control)) current.Use(); return true; }
            if (!_meshEdit.Preparing && current.type == EventType.KeyDown && current.keyCode == KeyCode.R &&
                current.control && !current.command && !current.alt && !current.shift)
            { if (TryExecute(SceneCommand.LoopCut, view, control)) current.Use(); return true; }
            if (!_meshEdit.Preparing && current.type == EventType.KeyDown && current.keyCode == KeyCode.I &&
                !current.alt && !current.control && !current.command && !current.shift)
            { if (TryExecute(SceneCommand.Inset, view, control)) current.Use(); return true; }
            if (!_meshEdit.Preparing && current.type == EventType.KeyDown && current.keyCode == KeyCode.E &&
                !current.alt && !current.control && !current.command && !current.shift)
            { if (TryExecute(SceneCommand.Extrude, view, control)) current.Use(); return true; }
            if (!_meshEdit.Preparing && current.type == EventType.KeyDown && !current.alt && !current.control && !current.command && !current.shift &&
                (current.keyCode == KeyCode.X || current.keyCode == KeyCode.Delete || current.keyCode == KeyCode.Backspace))
            { TryExecute(SceneCommand.DeleteDissolve, view, control); current.Use(); return true; }
            if ((current.type == EventType.ExecuteCommand && (current.commandName == BeginGrabCommand ||
                current.commandName == BeginRotateCommand || current.commandName == BeginScaleCommand)) ||
                ShortcutProfiles.Matches("Blender Scene Controls/Grab", current) ||
                ShortcutProfiles.Matches("Blender Scene Controls/Rotate", current) ||
                ShortcutProfiles.Matches("Blender Scene Controls/Scale", current))
            {
                var command = current.commandName == BeginRotateCommand || ShortcutProfiles.Matches("Blender Scene Controls/Rotate", current) ? SceneCommand.Rotate :
                    current.commandName == BeginScaleCommand || ShortcutProfiles.Matches("Blender Scene Controls/Scale", current) ? SceneCommand.Scale : SceneCommand.Grab;
                if (TryExecute(command, view, control)) { current.Use(); return true; }
                return false;
            }
            if (_meshEdit.Handle(current)) return true;
            // Mesh transforms/operators belong to later stages. Never move the owner's Transform here.
            if (current.type == EventType.KeyDown && !current.alt &&
                (current.keyCode == KeyCode.G || current.keyCode == KeyCode.R || current.keyCode == KeyCode.S ||
                 current.keyCode == KeyCode.D || current.keyCode == KeyCode.A || current.keyCode == KeyCode.X ||
                 current.keyCode == KeyCode.Delete || current.keyCode == KeyCode.Backspace || current.keyCode == KeyCode.I || current.keyCode == KeyCode.E))
            { current.Use(); return true; }
            if ((current.type == EventType.ValidateCommand || current.type == EventType.ExecuteCommand) &&
                (current.commandName == "SelectAll" || current.commandName == "DeselectAll" || current.commandName == "Delete" || current.commandName == "SoftDelete" || current.commandName == "Duplicate"))
            { current.Use(); return true; }
            return false;
        }

        internal static void ExitMeshEdit()
        {
            if (_session?.Transaction is MeshTransformTransaction || _session?.Transaction is MeshLoopCutTransaction || _session?.Transaction is MeshInsetTransaction || _session?.Transaction is MeshBevelTransaction)
                End(ModalTermination.Abort, false, "Mesh Edit exit");
            var mode = _meshEdit;
            _meshEdit = null;
            mode?.Dispose();
            UpdateToolVisibility();
        }

        private const string MeshMenu = "Tools/Blender Scene Controls/Mesh Selection/";
        private static bool CanMeshMenu() => Available && !Active && !SceneNavigation.Active &&
            _meshEdit != null && _meshEdit.Valid && !TextIsEditing();
        [MenuItem(MeshMenu + "X-Ray")]
        private static void ToggleMeshXRay() => ExecuteMeshMenu(SceneCommand.XRay);
        [MenuItem(MeshMenu + "X-Ray", true)]
        private static bool CanXRay() { Menu.SetChecked(MeshMenu + "X-Ray", _meshEdit?.Selection.XRay == true); return CanMeshMenu(); }
        [MenuItem(MeshMenu + "Median")]
        private static void MedianPivot() => ExecuteMeshMenu(SceneCommand.Median);
        [MenuItem(MeshMenu + "Median", true)]
        private static bool CanMedian() { Menu.SetChecked(MeshMenu + "Median", _meshEdit?.Selection.PivotMode == MeshPivotMode.Median); return CanMeshMenu(); }
        [MenuItem(MeshMenu + "Active Element")]
        private static void ActivePivot() => ExecuteMeshMenu(SceneCommand.ActiveElement);
        [MenuItem(MeshMenu + "Active Element", true)]
        private static bool CanActivePivot() { Menu.SetChecked(MeshMenu + "Active Element", _meshEdit?.Selection.PivotMode == MeshPivotMode.Active); return CanMeshMenu(); }
        [MenuItem(MeshMenu + "Select Loop at Pointer")]
        private static void MeshLoop() => ExecuteMeshMenu(SceneCommand.SelectLoop);
        private static void ExecuteMeshMenu(SceneCommand command)
        { if (CanMeshMenu()) ExecuteToolbar(command, new SceneCommandContext(_meshEdit.Source)); }
        [MenuItem(MeshMenu + "Select Loop at Pointer", true)]
        private static bool CanMeshLoop() => CanMeshMenu();

        [MenuItem("Tools/Blender Scene Controls/Mesh Preparation/Recover Connectivity")]
        private static void RecoverMeshConnectivity() => ExecuteMeshMenu(SceneCommand.RecoverConnectivity);
        [MenuItem("Tools/Blender Scene Controls/Mesh Preparation/Tris to Quads")]
        private static void PrepareMeshQuads() => ExecuteMeshMenu(SceneCommand.TrisToQuads);
        [MenuItem("Tools/Blender Scene Controls/Mesh Preparation/Confirm")]
        private static void ConfirmMeshPreparation() => ExecuteMeshMenu(SceneCommand.ConfirmPreparation);
        [MenuItem("Tools/Blender Scene Controls/Mesh Preparation/Cancel")]
        private static void CancelMeshPreparation() => ExecuteMeshMenu(SceneCommand.CancelPreparation);
        [MenuItem("Tools/Blender Scene Controls/Mesh Edit/Delete or Dissolve")]
        private static void DeleteMeshElements() => ExecuteMeshMenu(SceneCommand.DeleteDissolve);
        [MenuItem("Tools/Blender Scene Controls/Apply/Scale")]
        private static void ApplyMeshScale()
        {
            var source = _meshEdit != null ? _meshEdit.Source : SceneView.lastActiveSceneView;
            ExecuteToolbar(SceneCommand.ApplyScale, new SceneCommandContext(source));
        }
        [MenuItem("Tools/Blender Scene Controls/Apply/Scale", true)]
        private static bool CanApplyMeshScale() => CommandUnavailable(SceneCommand.ApplyScale,
            _meshEdit != null ? _meshEdit.Source : SceneView.lastActiveSceneView) == null;
        private static void ExecuteMeshFile(SceneCommand command) => ExecuteToolbar(command,
            new SceneCommandContext(_meshEdit != null ? _meshEdit.Source : SceneView.lastActiveSceneView));
        [MenuItem("Tools/Blender Scene Controls/Mesh/Save Mesh")]
        private static void SaveMeshFile() => ExecuteMeshFile(SceneCommand.SaveMesh);
        [MenuItem("Tools/Blender Scene Controls/Mesh/Save Mesh As")]
        private static void SaveMeshFileAs() => ExecuteMeshFile(SceneCommand.SaveMeshAs);
        [MenuItem("Tools/Blender Scene Controls/Mesh/Export FBX")]
        private static void ExportMeshFile() => ExecuteMeshFile(SceneCommand.ExportFbx);
        [MenuItem("Tools/Blender Scene Controls/Mesh/Save Mesh", true)]
        [MenuItem("Tools/Blender Scene Controls/Mesh/Save Mesh As", true)]
        [MenuItem("Tools/Blender Scene Controls/Mesh/Export FBX", true)]
        private static bool CanMeshFile() => CommandUnavailable(SceneCommand.SaveMesh,
            _meshEdit != null ? _meshEdit.Source : SceneView.lastActiveSceneView) == null;
        [MenuItem("Tools/Blender Scene Controls/Mesh Edit/Bevel Edges")]
        private static void BevelMeshEdges() => ExecuteMeshMenu(SceneCommand.Bevel);
        [MenuItem("Tools/Blender Scene Controls/Mesh Edit/Bevel Vertices")]
        private static void BevelMeshVertices() => ExecuteMeshMenu(SceneCommand.VertexBevel);
        [MenuItem("Tools/Blender Scene Controls/Mesh Edit/Inset Faces")]
        private static void InsetMeshFaces() => ExecuteMeshMenu(SceneCommand.Inset);

        [MenuItem("Tools/Blender Scene Controls/Mesh Edit/Extrude")]
        private static void ExtrudeMeshElements() => ExecuteMeshMenu(SceneCommand.Extrude);

        [MenuItem("Tools/Blender Scene Controls/Mesh Edit/Loop Cut and Slide")]
        private static void LoopCutMesh() => ExecuteMeshMenu(SceneCommand.LoopCut);

        private static void HandleException(Exception exception)
        {
            try { SceneNavigation.End(); End(ModalTermination.Abort, false); ExitMeshEdit(); }
            finally { if (!(exception is ExitGUIException)) Debug.LogException(exception); }
        }

        // Clear ownership before any callbacks/Undo events can reenter this method.
        private static void End(ModalTermination termination, bool sourceGUI, string reason = "disable/exception")
        {
            var session = _session;
            if (session == null) return;
            _session = null;
            try
            {
                session.Finish(termination);
                if (session.Mode != ModalMode.Diagnostic) Debug.Log(termination == ModalTermination.Confirm ? $"BSC {session.Mode} confirmed ({reason})." : $"BSC {session.Mode} cancelled ({reason}).");
            }
            finally
            {
                try
                {
                    if (sourceGUI)
                    {
                        if (GUIUtility.hotControl == session.Control) GUIUtility.hotControl = 0;
                    }
                    else if (session.Source)
                    {
                        if (session.Transaction is MeshTransformTransaction || session.Transaction is MeshLoopCutTransaction || session.Transaction is MeshInsetTransaction || session.Transaction is MeshBevelTransaction)
                            ReleaseMeshCapture(session.Source, session.Control);
                        else
                        {
                            _releaseControl = session.Control;
                            try { session.Source.SendEvent(EditorGUIUtility.CommandEvent(ReleaseCommand)); }
                            finally { _releaseControl = 0; }
                        }
                    }
                }
                finally
                {
                    ScenePresentation.ResetCursor();
                    UpdateToolVisibility();
                    Tools.current = session.PreviousTool;
                    if (session.Source)
                    {
                        session.Source.wantsMouseMove = session.PreviousWantsMouseMove;
                        session.Source.Repaint();
                    }
                    // The system cursor is never locked/hidden; remove the local cursor rect by repaint.
                   // if (session.Source && ScenePointer.IsForeground) ScenePointer.Move(session.StartCursor);
                }
            }
        }
    }

    public sealed class GrabShortcutContext : IShortcutContext
    {
        public bool active => SceneControls.CanStart;
    }
    public sealed class MeshElementShortcutContext : IShortcutContext
    {
        public bool active => SceneControls.MeshEdit != null && SceneControls.CanStart;
    }
}
