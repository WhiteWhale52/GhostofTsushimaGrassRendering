// ChunkBoundsOverlay.cs
// Draws chunk bounds, LOD levels, and blade counts.

#if UNITY_EDITOR
using DebugVisualisation.Core;
using DebugVisualisation.Samplers;
using UnityEngine;

namespace DebugVisualization.Overlays
{
    [System.Serializable]
    public class ChunkBoundsOverlay : DebugOverlay
    {
        public override string Name => "Chunk Bounds";

        [SerializeField] private bool showLabels = true;
        [SerializeField] private bool colorByLOD = true;

        // LOD colors: green=0(close), yellow=1, orange=2, red=3(far)
        private static readonly Color[] LodColors =
        {
            new Color(0.0f, 1.0f, 0.0f, 0.4f),
            new Color(1.0f, 1.0f, 0.0f, 0.4f),
            new Color(1.0f, 0.5f, 0.0f, 0.4f),
            new Color(1.0f, 0.0f, 0.0f, 0.4f)
        };

        public IChunkSampler Sampler { get; set; }

        public override void DrawHandles()
        {
            if (Sampler == null) return;

            foreach (var chunk in Sampler.ActiveChunks)
            {
                Color c = colorByLOD
                    ? LodColors[Mathf.Clamp(chunk.lodLevel, 0, LodColors.Length - 1)]
                    : new Color(0f, 0.8f, 1f, 0.3f);

                DebugDraw.WireBox(chunk.bounds, c);

                if (showLabels)
                {
                    DebugDraw.Label(
                        chunk.bounds.center + Vector3.up * 0.5f,
                        $"({chunk.coordinate_x},{chunk.coordinate_z})\n" +
                        $"LOD:{chunk.lodLevel}  blades:{chunk.instanceCount}",
                        Color.white);
                }
            }
        }
    }
}
#endif