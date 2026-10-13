
#if UNITY_EDITOR
using DebugVisualisation.Core;
using DebugVisualisation.Samplers;
using UnityEngine;

namespace DebugVisualization.Overlays
{
    [System.Serializable]
    public class VoronoiOverlay : DebugOverlay
    {
        public override string Name => "Voronoi Clustering";

        public enum DisplayMode
        {
            Density,
            Humidity,
            Fertility,
            CellID
        }

        [SerializeField] private Vector3 gridOrigin = Vector3.zero;
        [SerializeField] private float gridWidth = 50f;
        [SerializeField] private float gridDepth = 50f;
        [SerializeField] private int resolution = 24;
        [SerializeField] private DisplayMode displayMode = DisplayMode.Density;
        [SerializeField] private ColorRamp colorRamp = new ColorRamp();
        [SerializeField] private bool showCellLabels = true;

        public IVoronoiSampler Sampler { get; set; }

        public override void DrawHandles()
        {
            if (Sampler == null) return;

            DrawHeatMap();

            if (showCellLabels)
                DrawCellCenters();
        }

        private void DrawHeatMap()
        {
            float cellSize = gridWidth / resolution;

            DebugDraw.ForEachGridCell(
                gridOrigin, gridWidth, gridDepth, resolution, resolution,
                (worldPos, nx, nz) =>
                {
                    float value = displayMode switch
                    {
                        DisplayMode.Density => Mathf.InverseLerp(0.5f, 1.5f,
                                                    Sampler.SampleDensity(worldPos)),
                        DisplayMode.Humidity => Sampler.SampleHumidity(worldPos),
                        DisplayMode.Fertility => Sampler.SampleFertility(worldPos),
                        DisplayMode.CellID => (Sampler.SampleCellID(worldPos) % 8)
                                                    / 8f,
                        _ => 0f
                    };

                    DebugDraw.HeatCell(worldPos, cellSize, value, colorRamp);
                });
        }

        private void DrawCellCenters()
        {
            int idx = 0;
            foreach (var center in Sampler.CellCenters)
            {
                DebugDraw.Sphere(center, 0.4f, Color.white);
                DebugDraw.Label(
                    center + Vector3.up * 0.6f,
                    $"Cell {idx}\n" +
                    $"d:{Sampler.SampleDensity(center):F2} " +
                    $"h:{Sampler.SampleHumidity(center):F2}",
                    Color.white);
                idx++;
            }
        }
    }
}
#endif