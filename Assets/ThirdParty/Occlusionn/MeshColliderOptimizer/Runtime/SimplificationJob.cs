using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Occlusionn.MeshColliderOptimizer.Runtime
{
    /// <summary>
    /// Grid-based vertex welding simplification job (Burst-compatible).
    /// </summary>
    [BurstCompile]
    public struct SimplificationJob : IJob
    {
        [ReadOnly] public NativeArray<float3> InputVerts;
        [ReadOnly] public NativeArray<int> InputTris;
        public float CellSize;

        public NativeList<float3> OutputVerts;
        public NativeList<int> OutputTris;

        public void Execute()
        {
            int vCount = InputVerts.Length;

            var spatialMap = new NativeHashMap<int3, int>(vCount, Allocator.Temp);
            var oldToNewMap = new NativeArray<int>(vCount, Allocator.Temp);
            var cellSums = new NativeList<float3>(vCount, Allocator.Temp);
            var cellCounts = new NativeList<int>(vCount, Allocator.Temp);

            float inverseCell = 1.0f / math.max(CellSize, 0.0001f);

            for (int i = 0; i < vCount; i++)
            {
                float3 pos = InputVerts[i];

                int3 gridKey = new int3(
                    (int)math.floor(pos.x * inverseCell),
                    (int)math.floor(pos.y * inverseCell),
                    (int)math.floor(pos.z * inverseCell)
                );

                if (spatialMap.TryGetValue(gridKey, out int existingIndex))
                {
                    cellSums[existingIndex] = cellSums[existingIndex] + pos;
                    cellCounts[existingIndex] = cellCounts[existingIndex] + 1;
                    oldToNewMap[i] = existingIndex;
                }
                else
                {
                    int newIndex = OutputVerts.Length;
                    OutputVerts.Add(pos);
                    cellSums.Add(pos);
                    cellCounts.Add(1);

                    spatialMap.Add(gridKey, newIndex);
                    oldToNewMap[i] = newIndex;
                }
            }

            for (int i = 0; i < OutputVerts.Length; i++)
            {
                int count = math.max(1, cellCounts[i]);
                OutputVerts[i] = cellSums[i] / count;
            }

            int tCount = InputTris.Length;
            for (int i = 0; i < tCount; i += 3)
            {
                int old1 = InputTris[i];
                int old2 = InputTris[i + 1];
                int old3 = InputTris[i + 2];

                int new1 = oldToNewMap[old1];
                int new2 = oldToNewMap[old2];
                int new3 = oldToNewMap[old3];

                if (new1 != new2 && new1 != new3 && new2 != new3)
                {
                    OutputTris.Add(new1);
                    OutputTris.Add(new2);
                    OutputTris.Add(new3);
                }
            }

            spatialMap.Dispose();
            oldToNewMap.Dispose();
            cellSums.Dispose();
            cellCounts.Dispose();
        }
    }
}

