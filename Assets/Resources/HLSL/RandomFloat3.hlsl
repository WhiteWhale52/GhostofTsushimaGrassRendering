float Hash(float3 p)
{
    p = frac(p * 0.3183099 + float3(0.1, 0.2, 0.5));
    p *= 17.0;
    return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float3 Hash3(float3 p)
{
    return float3(
        Hash(p + float3(1.0, 0.0, 0.0)),
        Hash(p + float3(0.0, 1.0, 0.0)),
        Hash(p + float3(0.0, 0.0, 1.0))
    );
}

float3 RandomFloat3(float3 seed, float3 min, float3 max)
{
    float3 result = Hash3(seed);
    return lerp(min, max, result);
}