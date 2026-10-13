// GrassDebugBootstrapper.cs
// Add this to the same GameObject as DebugOverlayStack.
// It creates samplers and registers overlays.
// This is the only file that couples the debug system to the grass system.

#if UNITY_EDITOR
using UnityEngine;
using DebugVisualisation.Core;
using DebugVisualisation.Overlays;
using GhostOfTsushima.Debug;

namespace GhostOfTsushima.Runtime
{
    [ExecuteInEditMode]
    [RequireComponent(typeof(DebugOverlayStack))]
    public class GrassDebugBootstrapper : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private GrassBlades grassBlades;

        private DebugOverlayStack    _stack;
        private WindFieldOverlay     _windOverlay;
        private VoronoiOverlay       _voronoiOverlay;
        private ChunkBoundsOverlay   _chunkOverlay;

        private void OnEnable()
        {
            _stack = GetComponent<DebugOverlayStack>();

            // Create samplers — one per system
            var windSampler   = new WindSampler();
            var chunkSampler  = new ChunkSampler(grassBlades);

            // Create overlays and inject samplers
            _windOverlay = new WindFieldOverlay
            {
                Sampler    = windSampler,
                IsEnabled  = true
            };

            _voronoiOverlay = new VoronoiOverlay
            {
                // VoronoiSampler not shown here but follows the same pattern
                IsEnabled = false
            };

            _chunkOverlay = new ChunkBoundsOverlay
            {
                Sampler   = chunkSampler,
                IsEnabled = true
            };

            // Register with the stack
            _stack.Register(_windOverlay);
            _stack.Register(_voronoiOverlay);
            _stack.Register(_chunkOverlay);
        }

        private void OnDisable()
        {
            if (_stack == null) return;
            _stack.Unregister(_windOverlay);
            _stack.Unregister(_voronoiOverlay);
            _stack.Unregister(_chunkOverlay);
        }
    }
}
#endif