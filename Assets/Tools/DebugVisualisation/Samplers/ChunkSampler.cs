using System.Collections.Generic;
using UnityEngine;
using GhostOfTsushima.Runtime;
using DebugVisualisation.Samplers;

namespace GhostOfTsushima.Debug
{
    public class ChunkSampler : IChunkSampler
    {
        private readonly GrassBlades _grassBlades;
        private readonly List<ChunkInfo> _chunkCache = new List<ChunkInfo>();

        public ChunkSampler(GrassBlades grassBlades)
        {
            _grassBlades = grassBlades;
        }

        public IReadOnlyList<ChunkInfo> ActiveChunks
        {
            get
            {
                _chunkCache.Clear();

                var manager = _grassBlades?.ChunkSystemManager;
                if (manager == null) return _chunkCache;

                foreach (var kvp in manager.GetActiveChunks())
                {
                    var chunk = kvp.Value;
                    _chunkCache.Add(new ChunkInfo
                    {
                        bounds = chunk.bounds,
                        lodLevel = chunk.currentLODLevel,
                        instanceCount = chunk.instanceCount,
                        coordinate_x = chunk.coordinate.x,
                        coordinate_z = chunk.coordinate.z
                    });
                }

                return _chunkCache;
            }
        }
    }
}