using UnityEngine;

namespace GhostOfTsushima.Runtime
{
    public static class TerrainSampler
    {
        public static float SampleHeight(float worldX, float worldZ, Color[] heightPixels, int texWidth, int texHeight, float terrainWidth, float terrainDepth, float terrainAmpltiude)
        {
            float u = worldX / terrainWidth;
            float v = worldZ / terrainDepth;
            return SampleBilinearR(heightPixels, u, v, texWidth, texHeight) * terrainAmpltiude;
        }

        // ── Slope (degrees) ───────────────────────────────────────────────────
        // Samples height at four neighbors and returns the steepest angle.
        public static float SampleSlope(float worldX, float worldZ, Color[] heightPixels, int texWidth, int texHeight, float terrainWidth, float terrainDepth, float terrainAmpltiude, float sampleOffset = 0.5f)
        {
            float h = SampleHeight(worldX, worldZ, heightPixels, texWidth, texHeight, terrainWidth, terrainDepth, terrainAmpltiude);
            float hR = SampleHeight(worldX + sampleOffset, worldZ, heightPixels, texWidth, texHeight, terrainWidth, terrainDepth, terrainAmpltiude); // Right
            float hF = SampleHeight(worldX, worldZ + sampleOffset, heightPixels, texWidth, texHeight, terrainWidth, terrainDepth, terrainAmpltiude); // Forward

            Vector3 dx = new Vector3(sampleOffset, hR - h, 0f);
            Vector3 dz = new Vector3(0f, hF - h, sampleOffset);
            Vector3 normal = Vector3.Cross(dz, dx).normalized;

            return Vector3.Angle(Vector3.up, normal);
        }

        public static Vector3 SampleNormal(float worldX, float worldZ, Color[] heightPixels, int texWidth, int texHeight, float terrainWidth, float terrainDepth, float terrainAmpltiude, float sampleOffset = 0.5f)
        {
            float h = SampleHeight(worldX, worldZ, heightPixels, texWidth, texHeight, terrainWidth, terrainDepth, terrainAmpltiude);
            float hR = SampleHeight(worldX + sampleOffset, worldZ, heightPixels, texWidth, texHeight, terrainWidth, terrainDepth, terrainAmpltiude); // Right
            float hF = SampleHeight(worldX, worldZ + sampleOffset, heightPixels, texWidth, texHeight, terrainWidth, terrainDepth, terrainAmpltiude); // Forward

            Vector3 dx = new Vector3(sampleOffset, hR - h, 0f);
            Vector3 dz = new Vector3(0f, hF - h, sampleOffset);
            return Vector3.Cross(dz, dx).normalized;
        }

        public static float SampleMap(float worldX, float worldZ, Color[] pixels, int texWidth, int texHeight, float terrainWidth, float terrainDepth)
        {
            float u = worldX / terrainWidth;
            float v = worldZ / terrainDepth;
            return SampleBilinearR(pixels, u, v, texWidth, texHeight);
        }


        // ── Biome ID (nearest, no interpolation) ──────────────────────────────
        public static int SampleBiome(float worldX, float worldZ, Color[] pixels, int texWidth, int texHeight, float terrainWidth, float terrainDepth)
        {
            int x = Mathf.Clamp(Mathf.RoundToInt((worldX / terrainWidth) * (texWidth - 1)), 0, texWidth - 1);
            int z = Mathf.Clamp(Mathf.RoundToInt((worldZ / terrainDepth) * (texHeight - 1)), 0, texHeight - 1);
            return Mathf.RoundToInt(pixels[z * texWidth + x].r * 10f);
        }

        private static float SampleBilinearR(Color[] pixels, float u, float v, int w, int h)
        {
            float px = Mathf.Clamp(u * (w - 1), 0f, w - 1f);
            float pz = Mathf.Clamp(v * (h - 1), 0f, h - 1f);

            int x0 = Mathf.FloorToInt(px), x1 = Mathf.Min(x0 + 1, w - 1);
            int z0 = Mathf.FloorToInt(pz), z1 = Mathf.Min(z0 + 1, h - 1);

            float fx = px - x0, fz = pz - z0;

            float v00 = pixels[z0 * w + x0].r;
            float v10 = pixels[z0 * w + x1].r;
            float v01 = pixels[z1 * w + x0].r;
            float v11 = pixels[z1 * w + x1].r;

            return Mathf.Lerp(Mathf.Lerp(v00, v10, fx), Mathf.Lerp(v01, v11, fx), fz);

        }
    }
}
