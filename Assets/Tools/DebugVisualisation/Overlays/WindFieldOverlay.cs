// WindFieldOverlay.cs
// Draws a grid of arrows showing wind direction and strength.
// Optionally draws vorticle influence spheres.

#if UNITY_EDITOR
using DebugVisualisation.Core;
using DebugVisualisation.Samplers;
using UnityEngine;

namespace DebugVisualization.Overlays
{
    [System.Serializable]
    public class WindFieldOverlay : DebugOverlay
    {
        public override string Name => "Wind Field";

        [Header("Grid")]
        [SerializeField] private Vector3 gridOrigin = Vector3.zero;
        [SerializeField] private float gridWidth = 40f;
        [SerializeField] private float gridDepth = 40f;
        [SerializeField] private int resolutionX = 16;
        [SerializeField] private int resolutionZ = 16;

        [Header("Arrows")]
        [SerializeField] private float arrowLength = 1.5f;
        [SerializeField] private bool scaleByStrength = true;

        [Header("Vorticles")]
        [SerializeField] private bool showVorticles = true;
        [SerializeField] private ColorRamp vorticleRamp = new ColorRamp();

        // Injected by the bootstrapper
        public IWindSampler Sampler { get; set; }

        public override void DrawHandles()
        {
            if (Sampler == null) return;

            DrawWindGrid();

            if (showVorticles)
                DrawVorticles();
        }

        private void DrawWindGrid()
        {
            // Follow the scene view camera so the grid is always visible
            Vector3 origin = gridOrigin;

            DebugDraw.ForEachGridCell(
                origin, gridWidth, gridDepth, resolutionX, resolutionZ,
                (worldPos, nx, nz) =>
                {
                    Vector3 dir = Sampler.SampleDirection(worldPos);
                    float strength = Sampler.SampleStrength(worldPos);

                    if (dir.sqrMagnitude < 0.001f) return;

                    float len = scaleByStrength
                        ? arrowLength * strength
                        : arrowLength;

                    // Color: cyan at low strength, white at full strength
                    Color arrowColor = Color.Lerp(Color.cyan, Color.white,
                        Mathf.Clamp01(strength));

                    DebugDraw.Arrow2D(worldPos, new Vector2(dir.x, dir.z),
                        len, arrowColor);
                });
        }

        private void DrawVorticles()
        {
            foreach (var v in Sampler.ActiveVorticles)
            {
                // Filled disc showing influence radius, alpha = decayT
                Color c = new Color(1f, 0.4f, 0f, v.decayT * 0.2f);
                DebugDraw.Disc(v.position, v.radius, c);

                // Wire sphere outline
                Color outline = new Color(1f, 0.4f, 0f, v.decayT * 0.8f);
                DebugDraw.Sphere(v.position, v.radius, outline);

                // Direction arrow for directional vorticles
                if (!v.IsOmni)
                {
                    DebugDraw.Arrow(
                        v.position,
                        v.direction.normalized * v.radius * 0.6f,
                        new Color(1f, 0.6f, 0f, v.decayT));
                }

                // Strength label
                DebugDraw.Label(
                    v.position + Vector3.up * (v.radius + 0.3f),
                    $"str:{v.strength:F2} t:{v.decayT:F2}",
                    new Color(1f, 0.8f, 0.2f));
            }
        }
    }
}
#endif