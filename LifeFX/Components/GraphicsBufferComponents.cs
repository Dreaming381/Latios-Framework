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
    /// <summary>
    /// A collection component that lives on the worldBlackboardEntity.
    /// Retrieve it with Read-Only access to assign global resource graphics buffers mapped to shader properties.
    /// </summary>
    public partial struct ShaderPropertyToGlobalBufferMap : ICollectionComponent
    {
        internal NativeHashMap<int, GraphicsBufferUnmanaged> shaderPropertyToGlobalBufferMap;

        public JobHandle TryDispose(JobHandle inputDeps) => shaderPropertyToGlobalBufferMap.IsCreated ? shaderPropertyToGlobalBufferMap.Dispose(inputDeps) : inputDeps;

        /// <summary>
        /// Adds or sets the graphics buffer associated with the global shader property
        /// </summary>
        /// <param name="shaderID">The ID of the global shader property obtains by Shader.PropertyToID()</param>
        /// <param name="globalBuffer">The global graphics buffer bound to the global shader property</param>
        public void AddOrReplace(int shaderID, GraphicsBufferUnmanaged globalBuffer) => shaderPropertyToGlobalBufferMap[shaderID] = globalBuffer;
    }
}

