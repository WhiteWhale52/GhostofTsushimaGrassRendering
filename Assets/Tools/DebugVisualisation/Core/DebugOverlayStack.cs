
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.TerrainTools;
using UnityEngine;


namespace DebugVisualisation.Core
{
    [ExecuteInEditMode]
    public class DebugOverlayStack : MonoBehaviour
    {
        private readonly List<DebugOverlay> overlays = new List<DebugOverlay>();

        [SerializeField] private bool drawInEditMode = true;

        [SerializeField, Range(0f, 1f)] private float globalAlpha = 1f;

        public float GlobalAlpha => globalAlpha;

        public static DebugOverlayStack Instance { get; private set; }

        private void OnEnable()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[DebugOverlayStack] Duplicate instance - only" + " one DebugOverlayStack instance allowed per scene");

                return;
            }

            Instance = this;

            SceneView.duringSceneGui += OnSceneGUI;
        }

        private void OnDisable()
        {
            if (Instance == this) Instance = null;
            SceneView.duringSceneGui -= OnSceneGUI;

            foreach (var overlay in overlays)
                overlay.OnDeactivate();
        }

        public void Register(DebugOverlay overlay)
        {
            if (overlays.Contains(overlay)) return;
            overlay.OnActivate();
            overlays.Add(overlay);
        }

        public void Unregister(DebugOverlay overlay)
        {
            if (overlays.Remove(overlay))
                overlay.OnDeactivate();
        }

        public IReadOnlyList<DebugOverlay> Overlays => overlays;

        private void OnDrawGizmosSelected()
        {
            if (!ShouldDraw()) return;

            foreach (var overlay in overlays)
            {
                if (overlay.isEnabled)
                    overlay.DrawGizmos();
            }
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            if (!ShouldDraw()) return;

            foreach (var overlay in overlays)
            {
                if (overlay.isEnabled)
                    overlay.DrawHandles();
            }
        }

        private bool ShouldDraw()
        {
            if (!drawInEditMode && !Application.isPlaying) return false;
            return true;
        }
    }
}

#endif