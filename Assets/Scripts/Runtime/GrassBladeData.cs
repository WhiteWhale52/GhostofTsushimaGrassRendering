using Unity.Mathematics;

namespace GhostOfTsushima.Runtime
{
    struct GrassBladeInstanceData
    {

        public float3 Position;

        public float2 FacingDirection;

        public float Height;

        public float Width;

        public float TiltAngle;

        public float CurvatureStrength;

        public float BladeHash;

        public int GrassTypeProfile;

        public float WindStrength;

        public float WindPhaseOffset;

    }

    struct HermiteCurveParams {
        public float3 controlPoint0;
        public float3 controlPoint1;

        public float3 rootTangent;
        public float3 tipTangent;

    }
}