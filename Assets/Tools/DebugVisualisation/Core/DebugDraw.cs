#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;


namespace DebugVisualisation.Core
{
    public static class DebugDraw
    {
        // ── Arrow ──────────────────────────────────────────────────────────────
        // Draws a 3D arrow from origin in direction, with a cone head.
        // headFraction: how much of the total length is the arrowhead [0,1].
        public static void Arrow(Vector3 origin, Vector3 direction, Color color, float headFraction = 0.34f)
        {
            if (direction.sqrMagnitude < 0.0001f) return;

            float length = direction.magnitude;
            Vector3 dir = direction / length;
            Vector3 tip = origin + direction;
            Vector3 shaftEnd = origin + dir * length * (1f - headFraction);

            Handles.color = color;

            Handles.DrawLine(origin, shaftEnd);

            Handles.ConeHandleCap(0, shaftEnd + dir * (length * headFraction * 0.5f), Quaternion.LookRotation(dir),length * headFraction, EventType.Repaint);
        }

        public static void Arrow2D(Vector3 origin, Vector2 directionXZ, float length, Color color, float headAngle = 25f, float headLength = 0.3f)
        {
            if (directionXZ.sqrMagnitude < 0.0001f) return;

            Vector2 dirNormalised = directionXZ.normalized;
            Vector3 direction3D = new Vector3(dirNormalised.x, 0f, dirNormalised.y);
            Vector3 tip = origin + direction3D * length;

            Handles.color = color;
            Handles.DrawLine(origin, tip);

            float radians = headAngle * Mathf.Deg2Rad;
            Vector2 left = Rotate2D(dirNormalised, radians) * headLength;
            Vector2 right = Rotate2D(dirNormalised, -radians) * headLength;

            Handles.DrawLine(tip, tip - new Vector3(left.x, 0f, left.y));
            Handles.DrawLine(tip, tip - new Vector3(right.x, 0f, right.y));
        }

        public static void Sphere(Vector3 origin, float radius, Color color)
        {
            Handles.color = color;
            Handles.DrawWireDisc(origin, Vector3.up, radius);
            Handles.DrawWireDisc(origin, Vector3.right, radius);
            Handles.DrawWireDisc(origin, Vector3.forward, radius);
        }

        public static void Disc(Vector3 origin, float radius, Color color)
        {
            Handles.color = color;
            Handles.DrawSolidDisc(origin, Vector3.up, radius);
        }

        public static void WireBox(Bounds bounds, Color color)
        {
            Handles.color = color;
            Handles.DrawWireCube(bounds.center, bounds.size);
        }

        public static void HeatCell(Vector3 worldPos, float size, float value, ColorRamp ramp)
        {
            Color color = ramp.Evaluate(value);

            Handles.color = color;

            Vector3 halfSize = new Vector3(size * 0.5f, 0f, size * 0.5f);
            Vector3[] corners ={
                worldPos + new Vector3(-halfSize.x, 0, -halfSize.z),
                worldPos + new Vector3(halfSize.x, 0, -halfSize.z),
                worldPos + new Vector3(halfSize.x, 0, halfSize.z),
                worldPos + new Vector3(-halfSize.x, 0, halfSize.z)
            };

            Handles.DrawSolidRectangleWithOutline(corners, color, Color.clear);
        }

        public static void Label(Vector3 worldPos, string text, Color color)
        {
            GUIStyle style = new GUIStyle(GUI.skin.label)
            {
                normal = { textColor = color },
                fontSize = 10
            };
            Handles.Label(worldPos, text, style);
        }

        // ── Grid of points ─────────────────────────────────────────────────────
        // Iterates a flat XZ grid and calls action for each cell center.
        // action: (worldPos, normalizedX, normalizedZ) where normalized is [0,1]
        public static void ForEachGridCell(Vector3 origin, float totalWidth, float totalDepth,  int resolutionX, int resolutionZ, System.Action<Vector3, float, float> action)
        {
            float cellW = totalWidth / resolutionX;
            float cellD = totalDepth / resolutionZ;

            for (int z = 0; z < resolutionZ; z++)
                for (int x = 0; x < resolutionX; x++)
                {
                    Vector3 pos = origin + new Vector3(
                        (x + 0.5f) * cellW,
                        0f,
                        (z + 0.5f) * cellD);

                    float nx = x / (float)(resolutionX - 1);
                    float nz = z / (float)(resolutionZ - 1);

                    action(pos, nx, nz);
                }
        }



        private static Vector2 Rotate2D(Vector2 v, float radians)
        {
            float cos = Mathf.Cos(radians);
            float sin = Mathf.Sin(radians);
            return new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
        }
    }
}
#endif