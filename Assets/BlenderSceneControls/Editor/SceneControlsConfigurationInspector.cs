using UnityEditor;
using UnityEngine;

namespace BlenderSceneControls
{
    [CustomEditor(typeof(SceneControlsConfiguration))]
    internal sealed class SceneControlsConfigurationInspector : Editor
    {
        public override void OnInspectorGUI()
        {
            var current = Event.current;
            var interacted = current.type == EventType.MouseDown || current.type == EventType.KeyDown;
            if (DrawDefaultInspector() || interacted)
                SceneControls.ConfigurationInspectorInteracted();
            if (target == SceneControlsConfiguration.Current && SceneControlsConfiguration.Current.Enable3DCursor)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("3D Cursor — Current Stage", EditorStyles.boldLabel);
                using (new EditorGUI.DisabledScope(!SceneCursor.CanEdit))
                {
                    EditorGUI.BeginChangeCheck();
                    var position = EditorGUILayout.Vector3Field("World Position", SceneCursor.Position);
                    if (EditorGUI.EndChangeCheck()) SceneCursor.SetPosition(position);
                    if (GUILayout.Button("Cursor to World Origin")) SceneCursor.ToOrigin();
                    using (new EditorGUI.DisabledScope(!SceneCursor.CanToActive))
                        if (GUILayout.Button("Cursor to Active Object")) SceneCursor.ToActive();
                }
                EditorGUILayout.HelpBox("Shift+RMB in Scene View places the cursor. Shift+A creates primitives here. " +
                    "Position is kept for this Editor session, separately for Prefab Mode and Play Mode.", MessageType.Info);
                if (interacted) SceneControls.ConfigurationInspectorInteracted();
            }
        }
    }
}
