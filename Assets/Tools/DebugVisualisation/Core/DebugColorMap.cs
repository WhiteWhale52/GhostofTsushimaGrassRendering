using UnityEngine;

namespace DebugVisualisation.Core
{
    [System.Serializable]
    public class ColorRamp
    {
        public enum Preset
        {
            Heat,
            Cool,
            Diverging,
            Viridis,
            Grayscale
        }

        [SerializeField] private Preset preset = Preset.Heat;

        [SerializeField, Range(0f, 1f)] private float alpha = 0.7f;

        public Color Evaluate(float t)
        {
            t = Mathf.Clamp01(t);
            Color c = preset switch
            {
                Preset.Heat => EvalHeat(t),
                Preset.Cool => EvalCool(t),
                Preset.Diverging => EvalDiverging(t),
                Preset.Viridis => EvalViridis(t),
                Preset.Grayscale => new Color(t, t, t),
                _ => Color.magenta
            };
            c.a = alpha;
            return c;
        }

        private static Color EvalHeat(float t)
        {
            if (t < 0.33f) return Color.Lerp(Color.black, new Color(1f, 0.1f, 0f), t / 0.33f);
            if (t < 0.66f) return Color.Lerp(new Color(1f, 0.1f, 0f), new Color(1f, 0.6f, 0f), (t - 0.33f) / 0.33f);
            return Color.Lerp(new Color(1f, 0.6f, 0f), Color.yellow, (t - 0.66f) / 0.34f);
        }

        private static Color EvalCool(float t)
        {
            if (t < 0.5f) return Color.Lerp(Color.black, Color.blue, t / 0.5f);
            return Color.Lerp(Color.blue, Color.cyan, (t - 0.5f) / 0.5f);
        }

        private static Color EvalDiverging(float t)
        {
            if (t < 0.5f) return Color.Lerp(Color.blue, Color.white, t / 0.5f);
            return Color.Lerp(Color.white, Color.red, (t - 0.5f) / 0.5f);
        }

        // Viridis approximation — perceptually uniform
        private static Color EvalViridis(float t)
        {
            float r = Mathf.Clamp01(0.280f + t * (0.124f + t * (-0.278f + t * 0.874f)));
            float g = Mathf.Clamp01(0.002f + t * (1.451f + t * (-1.384f + t * 0.493f)));
            float b = Mathf.Clamp01(0.330f + t * (1.101f + t * (-1.802f + t * 0.833f)));
            return new Color(r, g, b);
        }
    }
}
