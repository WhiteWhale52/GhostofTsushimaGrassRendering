using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;

namespace GhostOfTsushima.Runtime
{

    public struct GrassWindState
    {
        public float2 direction;
        public float strength;
        public float turbulence;
        public float time;

    }

    // ══════════════════════════════════════════════════════════════════════
    // GPU STRUCT
    // Must match the HLSL Vorticle struct in GrassBlades.shader exactly.
    // Packed into float4s for guaranteed alignment.
    //
    // Layout:
    //   positionAndRadius.xyz  = world position
    //   positionAndRadius.w    = radius
    //   directionAndStrength.xyz = normalised push direction
    //   directionAndStrength.w   = peak strength at center
    //   decayAndPad.x          = decayT [1=alive, 0=gone]
    //   decayAndPad.yzw        = padding
    // Total: 48 bytes
    // ══════════════════════════════════════════════════════════════════════
    [StructLayout(LayoutKind.Sequential)]
    struct GPUVorticle
    {
        public Vector4 positionAndRadius;      // xyz=pos, w=radius
        public Vector4 directionAndStrength;   // xyz=dir, w=strength
        public Vector4 decayAndPad;            // x=decayT, yzw=unused
    }

    struct VorticleData
    {
        public Vector3 worldPosition;
        public Vector3 direction;
        public float decayT; 
        public float radius;
        public float strength;

        public float decayRate; // How quickly the vorticle decays over time
    }

    [DefaultExecutionOrder(-100)]
    public class GrassWindSystem : MonoBehaviour
    {
        
        public static GrassWindSystem Instance { get; private set; }

        private static readonly int ID_WindDirection = Shader.PropertyToID("_WindDirection");
        private static readonly int ID_WindStrength = Shader.PropertyToID("_WindStrength");
        private static readonly int ID_WindSpeed = Shader.PropertyToID("_WindSpeed");
        private static readonly int ID_WindTime = Shader.PropertyToID("_WindTime");
        private static readonly int ID_NoiseScrollSpeed = Shader.PropertyToID("_NoiseScrollSpeed");
        private static readonly int ID_NoiseScale = Shader.PropertyToID("_NoiseScale");
        private static readonly int ID_NoiseFBMOctaves = Shader.PropertyToID("_NoiseFBMOctaves");
        private static readonly int ID_GustStrength = Shader.PropertyToID("_GustStrength");
        private static readonly int ID_Vorticles = Shader.PropertyToID("_Vorticles");
        private static readonly int ID_VorticleCount = Shader.PropertyToID("_VorticleCount");



        [Header("Base Wind Settings")]
        [Tooltip("Primary wind directtion in XZ plane.")]
        [SerializeField] private Vector2 baseDirection = new Vector2(1, 0.2f);
        [SerializeField] private float baseStrength = 1.0f;


        [Tooltip("How fast the wind direction slowly rotates over time. " +
               "Simulates large weather systems shifting direction. " +
               "0 = constant direction.")]
        [SerializeField] private float driftSpeed = 0.04f;

        [Tooltip("Maximum angle the wind direction drifts from base, in radians.")]
        [SerializeField] private float driftAmplitude = 0.4f;

        [Header("Turbulence")]
        [Tooltip("High-frequency small-scale variation layered on top of gusts.")]
        [SerializeField] private float turbulence = 0.15f;

        [Tooltip("Additional strength added at gust peaks. " +
                "Total strength = baseStrength + gustFrequency * gustStrength.")]
        [SerializeField] private float gustStrength = 0.45f;

        [Tooltip("How many gust cycles per second. " +
                "0.2 = one gust approximately every 5 seconds.")]
        [SerializeField] private float gustFrequency = 0.2f;



        [Header("Perlin Noise (shader-side)")]
        [Tooltip("How fast the noise map scrolls in the wind direction. " +
                 "Higher = faster-moving turbulence patches.")]
        [SerializeField] private float noiseScrollSpeed = 0.08f;



        [Tooltip("Spatial frequency of the noise. Lower = large rolling waves. " +
                 "Higher = tight rapid variation.")]
        [SerializeField] private float noiseScale = 0.07f;



        [Tooltip("Number of fBm octaves evaluated in the vertex shader. " +
                 "More octaves = more detail but more ALU cost. " +
                 "4 is a good default. Keep <= 6.")]
        [Range(1, 6)]
        [SerializeField] private int noiseFBMOctaves = 4;



        [Header("Wind Speed")]
        [Tooltip("Controls how fast individual waves propagate across the field. " +
                 "This is not the same as wind strength — it affects the phase " +
                 "velocity of the displacement wave.")]
        [SerializeField] private float windSpeed = 1.4f;

        [Header("Vorticles")]
        [Tooltip("Maximum simultaneous vorticles. Each vorticle is one localised " +
                 "wind source: a weapon swing, a gust, an explosion, etc. " +
                 "The vertex shader loops over all active vorticles per blade.")]
        [Range(1, 32)]
        [SerializeField] private int maxVorticles = 16;

        public Vector3 CurrentDirection { get; private set; }

        // Current total wind strength (base + gust contribution).
        public float CurrentStrength { get; private set; }

        // Current gust intensity [0,1] — useful for audio or particle systems.
        public float CurrentGustIntensity { get; private set; }

        // Wind-local time, driven by Time.deltaTime.
        // Does not reset on scene reload (stays continuous).
        public float WindTime { get; private set; }

        // Number of currently active vorticles.
        public int ActiveVorticleCount => _vorticles.Count;

        private List<VorticleData> _vorticles = new List<VorticleData>();
        private ComputeBuffer _vorticleBuffer;
        private GPUVorticle[] _gpuVorticles;
        private float2 _normalizedBaseDir;

        public GrassWindState CurrentState { get; private set; }

        private void OnEnable()
        {
            _normalizedBaseDir = math.normalize(new float2(baseDirection.x, baseDirection.y));
            _vorticles = new List<VorticleData>(maxVorticles);
            _gpuVorticles = new GPUVorticle[maxVorticles];

            _vorticleBuffer = new ComputeBuffer(maxVorticles, Marshal.SizeOf(typeof(GPUVorticle)), ComputeBufferType.Structured);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning($"[GrassWindSystem] Multiple instances detected on {name}. Destroying duplicate.");
                Destroy(this.gameObject);
                return;
            }
            Instance = this;
        }

        private void Update()
        {
            WindTime += Time.deltaTime;

            ComputeWindState();
            TickVorticles();
            UploadToShader();


        }

        private void ComputeWindState()
        {
            // Direction: slowly drifts from base direction using a sine wave.
            // This simulates large-scale weather pattern shifts without any
            // simulation cost — a single trig call per frame.
            float drift = Mathf.Sin(WindTime * driftSpeed) * driftAmplitude;
            float baseAngle = Mathf.Atan2(_normalizedBaseDir.y, _normalizedBaseDir.x);
            float totalAngle = baseAngle + drift;

            CurrentDirection = new Vector3(Mathf.Cos(totalAngle), 0f, Mathf.Sin(totalAngle));

            //Gust: It is the product of two sine waves at slightly different frquencies.
            // They inerfere periodically resulting in gust peaks and 
            // lulls. The result is a smooth, continuous gust pattern that is easy to control.
            float gustA = Mathf.Sin(WindTime * gustFrequency * 2f * Mathf.PI);
            float gustB = Mathf.Sin(WindTime * gustFrequency * 2.3f * Mathf.PI * 0.8f);
            float gust = Mathf.Max(gustA * gustB, 0f);

            CurrentGustIntensity = gust;
            CurrentStrength= baseStrength + gust * gustStrength;
        }

        private void TickVorticles()
        {
            float dt = Time.deltaTime;       

            for (int i = _vorticles.Count - 1; i >= 0; i--)
            {
                var vorticle = _vorticles[i];
                vorticle.decayT -= vorticle.decayRate * dt;
                if (vorticle.decayT <= 0f)
                {
                    _vorticles.RemoveAt(i);
                }
                else
                {
                    _vorticles[i] = vorticle;
                }
            }
        }

        private void UploadToShader()
        {
            Shader.SetGlobalVector(ID_WindDirection, new Vector4(CurrentDirection.x, 0f, CurrentDirection.z, 0f));
            Shader.SetGlobalFloat(ID_WindTime, WindTime);
            Shader.SetGlobalFloat(ID_WindStrength, CurrentStrength);
            Shader.SetGlobalFloat(ID_WindSpeed, windSpeed);
            Shader.SetGlobalFloat(ID_NoiseScrollSpeed, noiseScrollSpeed);
            Shader.SetGlobalFloat(ID_NoiseScale, noiseScale);
            Shader.SetGlobalInt(ID_NoiseFBMOctaves, noiseFBMOctaves);
            Shader.SetGlobalFloat(ID_GustStrength, gustStrength);

            int count = _vorticles.Count;

            for (int i = 0; i < count; i++)
            {
                var v = _vorticles[i];
                _gpuVorticles[i] = new GPUVorticle
                {
                    positionAndRadius = new Vector4(v.worldPosition.x, v.worldPosition.y, v.worldPosition.z, v.radius),
                    directionAndStrength = new Vector4(v.direction.x, v.direction.y, v.direction.z, v.strength),
                    decayAndPad = new Vector4(v.decayT, 0f, 0f, 0f)
                };
            }

            for (int i = count; i < maxVorticles; i++)
                _gpuVorticles[i] = default;

            _vorticleBuffer.SetData(_gpuVorticles);
            Shader.SetGlobalBuffer(ID_Vorticles, _vorticleBuffer);
            Shader.SetGlobalInt(ID_VorticleCount, count);
        }

        // Spawn a wake vorticle at the given position.
        // Inspired directly by GoW's wake motor:
        //   "Motion as wind, with some spread, two frame average motion."
        //
        // position:     world position of the wake source
        // velocity:     world-space velocity of the object (magnitude drives strength)
        // radius:       world-space radius of grass influence
        // lifetimeSecs: how long the wake persists (0.3–0.8s is natural for weapons)
        public void SpawnWakeVorticle(Vector3 position, Vector3 velocity, float radius = 2.5f, float lifetimeSecs = 0.5f)
        {
            if (_vorticles.Count >= maxVorticles)
                return;

            float speed = velocity.magnitude;

            if (speed < 0.1f)
                return;

            float strength = Mathf.Clamp(speed * 0.18f, 0.05f, 1.8f);

            if (lifetimeSecs <= 0f)
                return;
            _vorticles.Add(new VorticleData
            {
                worldPosition = position,
                direction = velocity.normalized,
                radius = radius,
                strength = strength,
                decayT = 1f,
                decayRate = 1f / Mathf.Max(lifetimeSecs, 0.05f)
            });
        }

        public void SpawnGustVorticle(Vector3 position, Vector3 direction, float radius = 3f, float strength = 1.0f, float lifetimeSecs = 0.5f)
        {
            if (_vorticles.Count >= maxVorticles)
                return;
            if (lifetimeSecs <= 0f)
                return;
            if (direction.sqrMagnitude < 0.001f)
            {
                Debug.LogWarning("[GrassWindSystem] SpawnGustVorticle called with " +
                                 "zero direction — skipped.");
                return;
            }
            _vorticles.Add(new VorticleData
            {
                worldPosition = position,
                direction = direction.normalized,
                radius = radius,
                strength = strength,
                decayT = 1f,
                decayRate = 1f / Mathf.Max(lifetimeSecs, 0.05f)
            });
        }

        public void SpawnOmniVorticle(Vector3 position, float radius = 3f, float strength = 1.0f, float lifetimeSecs = 0.5f)
        {
            if (_vorticles.Count >= maxVorticles)
                return;
            if (lifetimeSecs <= 0f)
                return;
            _vorticles.Add(new VorticleData
            {
                worldPosition = position,
                direction = Vector3.zero,
                radius = radius,
                strength = strength,
                decayT = 1f,
                decayRate = 1f / Mathf.Max(lifetimeSecs, 0.05f)
            });
        }

        public void ClearVorticles()
        {
            _vorticles.Clear();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void OnDisable()
        {
            _vorticleBuffer?.Release();
            _vorticleBuffer = null;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying) return;

            // Wind direction arrow
            Vector3 origin = transform.position + Vector3.up * 1.5f;
            UnityEditor.Handles.color = Color.cyan;
            UnityEditor.Handles.ArrowHandleCap(
                0,
                origin,
                Quaternion.LookRotation(CurrentDirection),
                CurrentStrength * 4f,
                EventType.Repaint);

            UnityEditor.Handles.Label(
                origin + Vector3.up * 0.5f,
                $"Wind  dir:({CurrentDirection.x:F2}, {CurrentDirection.z:F2})\n" +
                $"str:{CurrentStrength:F2}  gust:{CurrentGustIntensity:F2}\n" +
                $"vorticles:{_vorticles.Count}/{maxVorticles}");

            // Active vorticles
            foreach (VorticleData v in _vorticles)
            {
                // Sphere showing influence radius, alpha driven by decayT
                Color c = new Color(1f, 0.5f, 0f, v.decayT * 0.25f);
                Gizmos.color = c;
                Gizmos.DrawSphere(v.worldPosition, v.radius);

                // Direction arrow
                bool isOmni = v.direction.sqrMagnitude < 0.001f;
                if (!isOmni)
                {
                    UnityEditor.Handles.color = new Color(1f, 0.5f, 0f, v.decayT);
                    UnityEditor.Handles.ArrowHandleCap(
                        0,
                        v.worldPosition,
                        Quaternion.LookRotation(v.direction),
                        v.radius * 0.5f,
                        EventType.Repaint);
                }
                else
                {
                    // Omni: draw outward rings
                    Gizmos.color = new Color(1f, 0.8f, 0f, v.decayT * 0.6f);
                    Gizmos.DrawWireSphere(v.worldPosition, v.radius * v.decayT);
                }
            }
        }
#endif
    }
}
