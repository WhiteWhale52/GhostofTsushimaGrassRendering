using System.Collections.Generic;
using UnityEngine;
using GhostOfTsushima.Runtime;
using DebugVisualisation.Samplers;

namespace GhostOfTsushima.Debug
{
    public class WindSampler : IWindSampler
    {
        private readonly List<VorticleInfo> _vorticleCache = new List<VorticleInfo>();

        public Vector3 SampleDirection(Vector3 worldPos)
        {
            if (GrassWindSystem.Instance == null)
                return Vector3.zero;

            
            return GrassWindSystem.Instance.CurrentDirection;
        }

        public float SampleStrength(Vector3 worldPos)
        {
            if (GrassWindSystem.Instance == null)
                return 0f;

            return GrassWindSystem.Instance.CurrentStrength;
        }

        public float GustIntensity =>
            GrassWindSystem.Instance?.CurrentGustIntensity ?? 0f;

        public IReadOnlyList<VorticleInfo> ActiveVorticles
        {
            get
            {
                _vorticleCache.Clear();

                if (GrassWindSystem.Instance == null)
                    return _vorticleCache;

                // GrassWindSystem exposes vorticles for debug use.
                // Add a public property to GrassWindSystem:
                //   public IReadOnlyList<VorticleData> DebugVorticles => _vorticles;
                foreach (var v in GrassWindSystem.Instance.DebugVorticles)
                {
                    _vorticleCache.Add(new VorticleInfo
                    {
                        position = v.worldPosition,
                        direction = v.direction,
                        radius = v.radius,
                        strength = v.strength,
                        decayT = v.decayT
                    });
                }

                return _vorticleCache;
            }
        }
    }
}