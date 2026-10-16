using System;
using UnityEditor;
using UnityEngine;

namespace BlenderSceneControls
{
    public enum ToolVisibility { Never, DuringOperation, WhileEnabled }

    [Serializable]
    public sealed class OperationCursor
    {
        public Texture2D Image;
        public Vector2 Hotspot;
        public MouseCursor BuiltinFallback = MouseCursor.MoveArrow;

        internal MouseCursor SafeFallback => Enum.IsDefined(typeof(MouseCursor), BuiltinFallback) &&
            BuiltinFallback != MouseCursor.CustomCursor ? BuiltinFallback : MouseCursor.Arrow;
        internal bool HasUsableImage => Image && Image.isReadable && Image.width <= 256 && Image.height <= 256 &&
            Image.format == TextureFormat.RGBA32 && Image.mipmapCount == 1;
        internal Vector2 SafeHotspot => !Image ? Vector2.zero : new Vector2(
            Mathf.Clamp(float.IsFinite(Hotspot.x) ? Hotspot.x : 0, 0, Image.width - 1),
            Mathf.Clamp(float.IsFinite(Hotspot.y) ? Hotspot.y : 0, 0, Image.height - 1));
    }

    // The fixed path is the active configuration. Other assets of this type are never selected implicitly.
    public sealed class SceneControlsConfiguration : ScriptableObject
    {
        public const string AssetPath = "Assets/BlenderSceneControls/Editor/Resources/SceneControlsConfiguration.asset";
        private static SceneControlsConfiguration _asset;
        private static SceneControlsConfiguration _defaults;
        private static bool _loaded;

        [Header("Guides")]
        public Color XAxis = new Color(1, 0.25f, 0.25f);
        public Color YAxis = new Color(0.3f, 1, 0.35f);
        public Color ZAxis = new Color(0.3f, 0.55f, 1);
        public Color Plane = new Color(0.65f, 0.8f, 1, 0.12f);
        public Color DottedLine = new Color(1, 1, 1, 0.8f);
        public Color Text = new Color(0.95f, 0.95f, 0.95f, 1);
        [Range(0, 1)] public float Opacity = 0.7f;
        [Range(1, 6)] public float LineThickness = 2;
        [Header("Cursors")]
        public OperationCursor GrabCursor = new OperationCursor { BuiltinFallback = MouseCursor.MoveArrow };
        public OperationCursor RotateCursor = new OperationCursor { BuiltinFallback = MouseCursor.RotateArrow };
        public OperationCursor ScaleCursor = new OperationCursor { BuiltinFallback = MouseCursor.ScaleArrow };
        public OperationCursor TrackballCursor = new OperationCursor { BuiltinFallback = MouseCursor.Orbit };
        [Header("Behaviour")]
        public bool EnableInPlayMode = true;
        public bool EmulateThreeButtonMouse;
        [Tooltip("Inside G/R/S, Z selects Unity Y (height) and Y selects Unity Z. Transform axes and shortcut profiles stay unchanged.")]
        public bool SwapYAndZAxisKeys;
        [Tooltip("Show a 3D cursor in Scene View. Shift+RMB places it; Shift+A primitives start at its position.")]
        public bool Enable3DCursor;
        public ToolVisibility ToolVisibility = ToolVisibility.DuringOperation;

        public static SceneControlsConfiguration Current
        {
            get
            {
                if (!_loaded)
                {
                    _asset = AssetDatabase.LoadAssetAtPath<SceneControlsConfiguration>(AssetPath);
                    _loaded = true;
                }
                return Resolve(_asset);
            }
        }

        internal static SceneControlsConfiguration Resolve(SceneControlsConfiguration asset)
        {
            if (asset) return asset;
            if (!_defaults)
            {
                _defaults = CreateInstance<SceneControlsConfiguration>();
                _defaults.hideFlags = HideFlags.HideAndDontSave;
            }
            return _defaults;
        }

        internal float SafeOpacity => float.IsFinite(Opacity) ? Mathf.Clamp01(Opacity) : 0.7f;
        internal float SafeThickness => float.IsFinite(LineThickness) ? Mathf.Clamp(LineThickness, 1, 6) : 2;
        internal OperationCursor CursorFor(bool scale, bool rotate, bool trackball) =>
            (trackball ? TrackballCursor : scale ? ScaleCursor : rotate ? RotateCursor : GrabCursor) ??
            new OperationCursor { BuiltinFallback = trackball ? MouseCursor.Orbit : scale ? MouseCursor.ScaleArrow :
                rotate ? MouseCursor.RotateArrow : MouseCursor.MoveArrow };

        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            EditorApplication.projectChanged += Invalidate;
            AssemblyReloadEvents.beforeAssemblyReload += ReleaseDefaults;
            EditorApplication.quitting += ReleaseDefaults;
        }

        private static void ReleaseDefaults()
        {
            if (_defaults) DestroyImmediate(_defaults);
            _defaults = null;
        }

        internal static void Invalidate()
        {
            _loaded = false;
            SceneView.RepaintAll();
        }

        private void OnValidate() => EditorApplication.delayCall += Invalidate;

        // Explicit creation only: deletion and domain reload must keep the built-in fallback usable.
        public static SceneControlsConfiguration EnsureDefaultAsset()
        {
            var existing = AssetDatabase.LoadAssetAtPath<SceneControlsConfiguration>(AssetPath);
            if (existing) return existing;
            if (AssetDatabase.LoadMainAssetAtPath(AssetPath))
                throw new InvalidOperationException("The configuration path contains another asset type.");
            const string folder = "Assets/BlenderSceneControls/Editor/Resources";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/BlenderSceneControls/Editor", "Resources");
            var configuration = CreateInstance<SceneControlsConfiguration>();
            AssetDatabase.CreateAsset(configuration, AssetPath);
            AssetDatabase.SaveAssetIfDirty(configuration);
            Invalidate();
            return configuration;
        }

        [MenuItem("Tools/Blender Scene Controls/Select Configuration", false, 140)]
        private static void SelectConfiguration()
        {
            Selection.activeObject = EnsureDefaultAsset();
            EditorGUIUtility.PingObject(Selection.activeObject);
        }
    }

    internal sealed class ToolVisibilityState
    {
        private bool _ownsHidden;
        private bool _previous;

        internal static bool ShouldHide(ToolVisibility mode, bool enabled, bool operation) => enabled &&
            (mode == ToolVisibility.WhileEnabled || mode == ToolVisibility.DuringOperation && operation);

        internal void Update(bool hide)
        {
            if (hide)
            {
                if (!_ownsHidden) { _previous = Tools.hidden; _ownsHidden = true; }
                Tools.hidden = true;
            }
            else Restore();
        }

        internal void Restore()
        {
            if (!_ownsHidden) return;
            Tools.hidden = _previous;
            _ownsHidden = false;
        }
    }
}
