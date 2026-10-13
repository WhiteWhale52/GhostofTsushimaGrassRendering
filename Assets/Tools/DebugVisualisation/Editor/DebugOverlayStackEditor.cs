
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using DebugVisualisation.Core;

namespace DebugVisualization.Editor
{
    [CustomEditor(typeof(DebugOverlayStack))]
    public class DebugOverlayStackEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var stack = (DebugOverlayStack)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Active Overlays", EditorStyles.boldLabel);

            if (stack.Overlays.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No overlays registered. Overlays are added at runtime by " +
                    "GrassDebugBootstrapper.cs on the same GameObject.",
                    MessageType.Info);
                return;
            }

            foreach (var overlay in stack.Overlays)
            {
                EditorGUILayout.BeginHorizontal();

                // Toggle
                bool newEnabled = EditorGUILayout.ToggleLeft(
                    overlay.Name,
                    overlay.IsEnabled,
                    GUILayout.Width(180f));

                if (newEnabled != overlay.IsEnabled)
                {
                    overlay.IsEnabled = newEnabled;
                    SceneView.RepaintAll();
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space();

            if (GUILayout.Button("Repaint Scene Views"))
                SceneView.RepaintAll();
        }
    }
}
#endif