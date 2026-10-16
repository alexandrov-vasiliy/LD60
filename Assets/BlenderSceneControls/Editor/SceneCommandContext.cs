using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BlenderSceneControls
{
    // A toolbar or deferred GUI launch must not acquire a different target while the pointer travels.
    internal sealed class SceneCommandContext
    {
        internal readonly SceneView Source;
        private readonly int[] _selection;
        private readonly Object _active;
        private readonly MeshEditMode _mode;
        private readonly Mesh _mesh;
        private readonly MeshFilter _filter;
        private readonly int _revision;
        private readonly int _documentRevision;
        private readonly StageHandle _stage;
        private readonly Rect _window;
        private readonly Rect _viewport;
        internal SceneCommandContext(SceneView source)
        {
            Source = source;
            _selection = Selection.objects.Select(o => o.GetInstanceID()).ToArray();
            _active = Selection.activeObject;
            _mode = SceneControls.MeshEdit;
            _filter = Selection.activeGameObject ? Selection.activeGameObject.GetComponent<MeshFilter>() : null;
            _mesh = _filter ? _filter.sharedMesh : null;
            _revision = _mode?.Selection.Revision ?? 0;
            _documentRevision = _mode?.Selection.Document.Revision ?? 0;
            _stage = StageUtility.GetCurrentStageHandle();
            _window = source ? source.position : default;
            _viewport = source ? source.cameraViewport : default;
        }

        internal bool Current => Source && Source.position.Equals(_window) && Source.cameraViewport.Equals(_viewport) &&
            _stage == StageUtility.GetCurrentStageHandle() && _active == Selection.activeObject &&
            _selection.SequenceEqual(Selection.objects.Select(o => o.GetInstanceID())) &&
            _mode == SceneControls.MeshEdit && (!_filter || _filter.sharedMesh == _mesh) &&
            (_mode == null || _mode.Valid && _mode.MatchesUnitySelection && _mode.Source == Source &&
                _mode.Selection.Revision == _revision && _mode.Selection.Document.Revision == _documentRevision);
    }
}
