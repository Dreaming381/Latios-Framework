using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;

namespace Latios.Kinemation
{
    public static class BindingUtilities
    {
        //public struct PathBindingWorkspace : INativeDisposable
        //{
        //    internal NativeList<int> indices;
        //
        //    public PathBindingWorkspace(Allocator allocator) => indices = new NativeList<int>(allocator);
        //    public void Dispose() => indices.Dispose();
        //    public JobHandle Dispose(JobHandle inputDeps) => indices.Dispose(inputDeps);
        //}

        public static unsafe bool TrySolveBindings(BlobAssetReference<MeshBindingPathsBlob>     meshPaths,
                                                   BlobAssetReference<SkeletonBindingPathsBlob> skeletonPaths,
                                                   NativeList<short>                            outSolvedBindings,
                                                   out int failedMeshIndex)
        {
            // Todo: Use sort or some replacement so that we can use something better than O(n^2)
            outSolvedBindings.Clear();
            for (int i = 0; i < meshPaths.Value.pathsInReversedNotation.Length; i++)
            {
                bool found = false;
                for (short j = 0; j < skeletonPaths.Value.pathsInReversedNotation.Length; j++)
                {
                    var meshPtr     = meshPaths.Value.pathsInReversedNotation[i].GetUnsafePtr();
                    var skeletonPtr = skeletonPaths.Value.pathsInReversedNotation[j].GetUnsafePtr();
                    var length      = meshPaths.Value.pathsInReversedNotation[i].Length;

                    if (length > skeletonPaths.Value.pathsInReversedNotation[j].Length)
                        continue;

                    if (UnsafeUtility.MemCmp(meshPtr, skeletonPtr, length) == 0)
                    {
                        outSolvedBindings.Add(j);
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    failedMeshIndex = outSolvedBindings.Length;
                    return false;
                }
            }
            failedMeshIndex = -1;
            return true;
        }

        //unsafe struct PathComparer : IComparer<int>
        //{
        //    public BlobAssetReference<MeshBindingPathsBlob>     meshPaths;
        //    public BlobAssetReference<SkeletonBindingPathsBlob> skeletonPaths;
        //
        //    public int Compare(int x, int y)
        //    {
        //        throw new NotImplementedException();
        //    }
        //}
    }
}

