using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Occlusionn.MeshColliderOptimizer.Runtime
{
    /// <summary>
    /// Moves each vertex along its normal by <see cref="Amount"/>.
    /// </summary>
    [BurstCompile]
    public struct InflationJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> Vertices;
        [ReadOnly] public NativeArray<float3> Normals;
        public NativeArray<float3> Results;
        public float Amount;

        public void Execute(int index)
        {
            float3 n = Normals[index];
            float3 normal = math.lengthsq(n) > 1e-10f ? math.normalize(n) : float3.zero;
            Results[index] = Vertices[index] + normal * Amount;
        }
    }
}

