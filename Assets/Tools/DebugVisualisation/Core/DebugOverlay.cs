
#if UNITY_EDITOR
using UnityEngine;
// DebugOverlay.cs
// Base class for all debug overlays.
// Each overlay is one visual concern: wind field, chunk bounds, etc.

namespace DebugVisualisation.Core
{
    [System.Serializable]
    public abstract class DebugOverlay
    {
        [SerializeField] private bool enabled = true;

        public abstract string Name { get; }

        public bool isEnabled { get => enabled; set => enabled = value; }

        // Called from OnDrawGizmosSelected on the DebugOverlayStack GameObject.
        // Use Gizmos.* calls here.
        public virtual void DrawGizmos() { }

        // Called from OnSceneGUI. Use Handles.* calls here.
        // Handles allow filled shapes and more complex rendering than Gizmos.
        public virtual void DrawHandles() { }

        // Called once when the overlay is first activated.
        // Use to cache references or pre-compute data.
        public virtual void OnActivate() { }

        // Called when the overlay is deactivated or the stack is destroyed.
        public virtual void OnDeactivate() { }
    }
}
#endif