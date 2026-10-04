using UnityEngine;

namespace GhostOfTsushima.Runtime
{
    // Attach to any GameObject that should push grass when it moves fast:
    // swords, axes, arrows, rolling boulders, large animals.
    //
    // Uses the two-frame average velocity approach from GoW's wake motor:
    //   "Motion as wind, with some spread, two frame average motion."
    //   — Sean Feeley, GDC 2019
    //
    // Why two frames: a single-frame velocity spike (from a fast swing that
    // starts and ends in one frame) would produce a very brief vorticle.
    // Averaging over two frames smooths the peak and gives more overlap
    // between successive vorticles on a fast swing.
    //
    // The component talks to GrassWindSystem.Instance — no direct reference
    // needed. If GrassWindSystem.Instance is null (system not in scene),
    // the component silently does nothing.
    public class WeaponWindInteraction : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════════════════════
        // INSPECTOR
        // ══════════════════════════════════════════════════════════════════════

        [Header("Wake Settings")]
        [Tooltip("Speed in m/s the object must exceed to spawn a vorticle. " +
                 "Prevents standing-still grass bending from minor jitter.")]
        [SerializeField] private float speedThreshold = 4f;

        [Tooltip("World-space radius of grass influence per vorticle. " +
                 "Should match the physical size of the object. " +
                 "Sword: 1.5–2.5m. Boulder: 4–6m.")]
        [SerializeField] private float vorticleRadius = 2f;

        [Tooltip("How long each wake vorticle persists in seconds. " +
                 "Short objects (arrows): 0.2–0.3s. " +
                 "Slow heavy objects (boulder): 0.6–1.0s.")]
        [SerializeField] private float vorticleLifetime = 0.45f;

        [Tooltip("Minimum time between spawned vorticles in seconds. " +
                 "Prevents flooding the vorticle buffer on a very fast swing. " +
                 "0.05 = one vorticle every 50ms maximum.")]
        [SerializeField] private float spawnInterval = 0.05f;

        [Tooltip("Spawn a vorticle at the tip position rather than this " +
                 "transform's position. Leave null to use transform.position.")]
        [SerializeField] private Transform tipOverride;

        [Header("Debug")]
        [Tooltip("Log vorticle spawns to console — disable in builds.")]
        [SerializeField] private bool debugLog = false;

        // ══════════════════════════════════════════════════════════════════════
        // PRIVATE
        // ══════════════════════════════════════════════════════════════════════
        private Vector3 _positionThisFrame;
        private Vector3 _positionLastFrame;
        private float _timeSinceLastSpawn;
        private bool _firstFrame = true;

        // ══════════════════════════════════════════════════════════════════════
        // LIFECYCLE
        // ══════════════════════════════════════════════════════════════════════
        private void OnEnable()
        {
            // Initialise both history positions to current so the first
            // velocity sample is zero, not a large jump from the origin.
            _positionThisFrame = GetSamplePosition();
            _positionLastFrame = _positionThisFrame;
            _firstFrame = true;
        }

        private void LateUpdate()
        {
            _timeSinceLastSpawn += Time.deltaTime;

            // Skip the very first frame — no valid history yet.
            // _firstFrame prevents a spurious large velocity on enable.
            if (_firstFrame)
            {
                _firstFrame = false;
                _positionLastFrame = _positionThisFrame;
                _positionThisFrame = GetSamplePosition();
                return;
            }

            // Two-frame average velocity — GoW wake motor approach.
            // positionThisFrame is t-1, positionLastFrame is t-2.
            // Dividing by deltaTime * 2 gives average velocity over two frames.
            float doubleDt = Time.deltaTime * 2f;
            Vector3 currentPos = GetSamplePosition();
            Vector3 velocity = doubleDt > 0.0001f
                ? (currentPos - _positionLastFrame) / doubleDt
                : Vector3.zero;

            // Slide the history window forward
            _positionLastFrame = _positionThisFrame;
            _positionThisFrame = currentPos;

            // Speed check and spawn interval check
            float speed = velocity.magnitude;

            if (speed > speedThreshold && _timeSinceLastSpawn >= spawnInterval)
            {
                if (GrassWindSystem.Instance != null)
                {
                    GrassWindSystem.Instance.SpawnWakeVorticle(
                        currentPos,
                        velocity,
                        vorticleRadius,
                        vorticleLifetime);

                    if (debugLog)
                        Debug.Log($"[WeaponWindInteraction] Wake spawned at {currentPos} " +
                                  $"speed={speed:F1} m/s");
                }

                _timeSinceLastSpawn = 0f;
            }
        }

        // ── Utilities ──────────────────────────────────────────────────────────
        private Vector3 GetSamplePosition()
        {
            return tipOverride != null
                ? tipOverride.position
                : transform.position;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Vector3 pos = GetSamplePosition();
            Gizmos.color = new Color(1f, 0.6f, 0f, 0.3f);
            Gizmos.DrawSphere(pos, vorticleRadius);
            Gizmos.color = new Color(1f, 0.6f, 0f, 1f);
            Gizmos.DrawWireSphere(pos, vorticleRadius);
        }
#endif
    }
}