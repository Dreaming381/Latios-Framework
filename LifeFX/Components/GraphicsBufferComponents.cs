using System;
using System.Runtime.InteropServices;
using Latios.Kinemation;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios.LifeFX
{
    internal partial struct ShaderPropertyToGlobalBufferMap : ICollectionComponent
    {
        public NativeHashMap<int, GraphicsBufferUnmanaged> shaderPropertyToGlobalBufferMap;

        public JobHandle TryDispose(JobHandle inputDeps) => shaderPropertyToGlobalBufferMap.IsCreated ? shaderPropertyToGlobalBufferMap.Dispose(inputDeps) : inputDeps;
    }
}

