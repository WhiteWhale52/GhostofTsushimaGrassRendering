// PerlinNoise.hlsl
// Classic Perlin noise — Ken Perlin's improved algorithm (2002).
// Include this file in any shader that needs spatial noise.
// No Unity built-ins or external dependencies.
#ifndef PERLIN_NOISE_INCLUDED
#define PERLIN_NOISE_INCLUDED


// ── Permutation table ──────────────────────────────────────────────────────
// 256 values from Perlin's original paper, repeated twice.
// The repetition allows indexing with (x + y) without modulo on the second access.
// These specific values are the standard reference implementation.
static const int _Perm[512] =
{
    151, 160, 137, 91, 90, 15, 131, 13, 201, 95, 96, 53, 194, 233, 7, 225,
    140, 36, 103, 30, 69, 142, 8, 99, 37, 240, 21, 10, 23, 190, 6, 148,
    247, 120, 234, 75, 0, 26, 197, 62, 94, 252, 219, 203, 117, 35, 11, 32,
     57, 177, 33, 88, 237, 149, 56, 87, 174, 20, 125, 136, 171, 168, 68, 175,
     74, 165, 71, 134, 139, 48, 27, 166, 77, 146, 158, 231, 83, 111, 229, 122,
     60, 211, 133, 230, 220, 105, 92, 41, 55, 46, 245, 40, 244, 102, 143, 54,
     65, 25, 63, 161, 1, 216, 80, 73, 209, 76, 132, 187, 208, 89, 18, 169,
    200, 196, 135, 130, 116, 188, 159, 86, 164, 100, 109, 198, 173, 186, 3, 64,
     52, 217, 226, 250, 124, 123, 5, 202, 38, 147, 118, 126, 255, 82, 85, 212,
    207, 206, 59, 227, 47, 16, 58, 17, 182, 189, 28, 42, 223, 183, 170, 213,
    119, 248, 152, 2, 44, 154, 163, 70, 221, 153, 101, 155, 167, 43, 172, 9,
    129, 22, 39, 253, 19, 98, 108, 110, 79, 113, 224, 232, 178, 185, 112, 104,
    218, 246, 97, 228, 251, 34, 242, 193, 238, 210, 144, 12, 191, 179, 162, 241,
     81, 51, 145, 235, 249, 14, 239, 107, 49, 192, 214, 31, 181, 199, 106, 157,
    184, 84, 204, 176, 115, 121, 50, 45, 127, 4, 150, 254, 138, 236, 205, 93,
    222, 114, 67, 29, 24, 72, 243, 141, 128, 195, 78, 66, 215, 61, 156, 180,
    // Repeated:
    151, 160, 137, 91, 90, 15, 131, 13, 201, 95, 96, 53, 194, 233, 7, 225,
    140, 36, 103, 30, 69, 142, 8, 99, 37, 240, 21, 10, 23, 190, 6, 148,
    247, 120, 234, 75, 0, 26, 197, 62, 94, 252, 219, 203, 117, 35, 11, 32,
     57, 177, 33, 88, 237, 149, 56, 87, 174, 20, 125, 136, 171, 168, 68, 175,
     74, 165, 71, 134, 139, 48, 27, 166, 77, 146, 158, 231, 83, 111, 229, 122,
     60, 211, 133, 230, 220, 105, 92, 41, 55, 46, 245, 40, 244, 102, 143, 54,
     65, 25, 63, 161, 1, 216, 80, 73, 209, 76, 132, 187, 208, 89, 18, 169,
    200, 196, 135, 130, 116, 188, 159, 86, 164, 100, 109, 198, 173, 186, 3, 64,
     52, 217, 226, 250, 124, 123, 5, 202, 38, 147, 118, 126, 255, 82, 85, 212,
    207, 206, 59, 227, 47, 16, 58, 17, 182, 189, 28, 42, 223, 183, 170, 213,
    119, 248, 152, 2, 44, 154, 163, 70, 221, 153, 101, 155, 167, 43, 172, 9,
    129, 22, 39, 253, 19, 98, 108, 110, 79, 113, 224, 232, 178, 185, 112, 104,
    218, 246, 97, 228, 251, 34, 242, 193, 238, 210, 144, 12, 191, 179, 162, 241,
     81, 51, 145, 235, 249, 14, 239, 107, 49, 192, 214, 31, 181, 199, 106, 157,
    184, 84, 204, 176, 115, 121, 50, 45, 127, 4, 150, 254, 138, 236, 205, 93,
    222, 114, 67, 29, 24, 72, 243, 141, 128, 195, 78, 66, 215, 61, 156, 180
};

// ── Fade ───────────────────────────────────────────────────────────────────
// Smoothing curve: 6t^5 - 15t^4 + 10t^3
// Perlin's 2002 improvement over the original 3t^2 - 2t^3.
// The higher-degree polynomial makes the first and second derivatives
// zero at t=0 and t=1, eliminating visible gradient discontinuities
// at cell boundaries. You would see a grid pattern without this.
float PerlinFade(float t)
{
    return t * t * t * (t * (t * 6.0 - 15.0) + 10.0);
}

// ── Gradient ───────────────────────────────────────────────────────────────
// Maps a hash value to one of 12 gradient directions.
// The 12 directions are the midpoints of the 12 edges of a unit cube:
//   (±1,±1, 0), (±1, 0,±1), (0,±1,±1)
// Distributing gradient directions along cube edges gives better
// isotropy than using 8 cube corners, which was Perlin's original approach.
// The bitwise ops are equivalent to the lookup table but branch-free.
float PerlinGrad(int hash, float x, float y, float z)
{
    int h = hash & 15;
    float u = h < 8 ? x : y;
    float v = h < 4 ? y : (h == 12 || h == 14 ? x : z);
    return ((h & 1) == 0 ? u : -u)
         + ((h & 2) == 0 ? v : -v);
}

// ── 3D Perlin noise ────────────────────────────────────────────────────────
// Returns a value in approximately [-1, 1].
// The actual range is [-sqrt(3/4), sqrt(3/4)] ≈ [-0.866, 0.866]
// due to the geometry of the gradient directions.
// Remap to [0,1] with: value * 0.5 + 0.5
// p: world-space position. Scale before passing in:
//    Perlin3D(worldPos * 0.05) gives large gentle waves.
//    Perlin3D(worldPos * 0.3)  gives tight fast variation.
float Perlin3D(float3 p)
{
    // Integer cell
    int X = (int) floor(p.x) & 255;
    int Y = (int) floor(p.y) & 255;
    int Z = (int) floor(p.z) & 255;

    // Fractional position within cell
    float x = p.x - floor(p.x);
    float y = p.y - floor(p.y);
    float z = p.z - floor(p.z);

    // Fade curves for each axis
    float u = PerlinFade(x);
    float v = PerlinFade(y);
    float w = PerlinFade(z);

    // Hash the 8 corners of the unit cube surrounding this point.
    // Each corner gets a unique pseudo-random gradient direction
    // derived from the permutation table.
    int A = _Perm[X] + Y;
    int AA = _Perm[A] + Z;
    int AB = _Perm[A + 1] + Z;
    int B = _Perm[X + 1] + Y;
    int BA = _Perm[B] + Z;
    int BB = _Perm[B + 1] + Z;

    // Dot each corner's gradient with the vector from that corner to p,
    // then trilinearly interpolate using the fade curves.
    // The lerp order is: along x (u), then y (v), then z (w).
    return lerp(
        lerp(
            lerp(PerlinGrad(_Perm[AA], x, y, z),
                 PerlinGrad(_Perm[BA], x - 1, y, z), u),
            lerp(PerlinGrad(_Perm[AB], x, y - 1, z),
                 PerlinGrad(_Perm[BB], x - 1, y - 1, z), u),
            v),
        lerp(
            lerp(PerlinGrad(_Perm[AA + 1], x, y, z - 1),
                 PerlinGrad(_Perm[BA + 1], x - 1, y, z - 1), u),
            lerp(PerlinGrad(_Perm[AB + 1], x, y - 1, z - 1),
                 PerlinGrad(_Perm[BB + 1], x - 1, y - 1, z - 1), u),
            v),
        w);
}

// ── 2D Perlin noise ────────────────────────────────────────────────────────
// Samples the 3D function at z = 0.
// Slightly cheaper than full 3D because z gradients collapse.
// Use this for the scrolling wind noise map.
float Perlin2D(float2 p)
{
    return Perlin3D(float3(p.x, p.y, 0.0));
}

// ── Fractal Brownian Motion (fBm) ──────────────────────────────────────────
// Layers multiple octaves of Perlin noise at increasing frequency
// and decreasing amplitude. Creates natural-looking turbulence.
//
// octaves:     number of layers. 4 is enough for wind.
//              More octaves add fine detail but cost more.
// lacunarity:  frequency multiplier per octave. 2.0 is standard.
//              Higher = tighter detail in each successive layer.
// gain:        amplitude multiplier per octave. 0.5 is standard.
//              Higher = higher-frequency layers contribute more.
//
// At octaves=4, lacunarity=2, gain=0.5:
//   Layer 0: frequency=1.0, amplitude=0.5
//   Layer 1: frequency=2.0, amplitude=0.25
//   Layer 2: frequency=4.0, amplitude=0.125
//   Layer 3: frequency=8.0, amplitude=0.0625
// Sum range ≈ [-0.87, 0.87] — similar to single-octave Perlin.
float FBM2D(float2 p, int octaves, float lacunarity, float gain)
{
    float value = 0.0;
    float amplitude = 0.5;
    float frequency = 1.0;

    // [loop] tells the compiler not to unroll — important when octave count
    // could vary or when register pressure is a concern.
    [loop]
    for (int i = 0; i < octaves; i++)
    {
        value += amplitude * Perlin2D(p * frequency);
        frequency *= lacunarity;
        amplitude *= gain;
    }

    return value;
}


#endif