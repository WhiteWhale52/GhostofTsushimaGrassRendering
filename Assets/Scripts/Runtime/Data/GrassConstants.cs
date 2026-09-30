
namespace GhostOfTsushima.Runtime
{
    public static class GrassConstants 
    {
        public const int BytesPerVertex = 44;
        public const int BytesPerInstance = 64;

        public const int BRGZeroBlockBytes = 64;

        public const int DefaultSegments = 7;

        public const float UVLeft = 0f;
        public const float UVRight = 1f;
        public const float UVRoot = 0f;
        public const float UVTip = 1f;

        //Growth
        public const float BaseGrowthRate = 0.3f;
        public const float OptimalHumidity = 0.5f;
        public const float OptimalTempC = 0.5f;

        public const float FootstepRadius = 2f;
        public const float FootstepRecoveryRate = 0.1f;
        public const float FootstepDecayDuration = 30f;

    }
}