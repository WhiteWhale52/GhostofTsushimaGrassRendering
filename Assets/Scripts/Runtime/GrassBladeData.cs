using Unity.Mathematics;

namespace GhostOfTsushima.Runtime
{
    public partial class GrassBlades
    {
        private struct GrassBladeData
        {

            public float3 Position;

            public float2 FacingDirection;

            public float WindStrength;

            public float ClumpColor;
            
            public float2 ClumpFacingDirection;

            public float Height;

            public float Width;

            public float Tilt;

            public float Bend;

            public int BladeHash;

            public int GrassType;

            public float Midpoint;

        }
    }
}