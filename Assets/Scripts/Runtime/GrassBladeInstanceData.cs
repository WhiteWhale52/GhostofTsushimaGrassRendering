using System.Runtime.InteropServices;
using Unity.Mathematics;

namespace GhostOfTsushima.Runtime
{
	[StructLayout(LayoutKind.Sequential)]
	struct GrassBladeInstanceData
    {

        public float3 position;
        public float facingAngle;

        public float height;
        public float width;
        public float lean;
        public float curvatureStrength;

        public int shapeProfileID;
        public float colorVariationSeed;
        public float bladeHash;
        public float stiffness;


        public float windStrength;
        public float windPhaseOffset;
        public float padding01;
        public float padding02;

    }

    struct HermiteCurveParams {
        public float3 controlPoint0;
        public float3 controlPoint1;

        public float3 rootTangent;
        public float3 tipTangent;

    }

    struct BladeVertex {
   
        public float3 position;
        public float3 normal;
        public float2 uv;

        public int paramAlongBlade;
        public int sideOfBlade;
    }
}