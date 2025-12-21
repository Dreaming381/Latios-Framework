#region Header
// Entities Graphics is disabled if SRP 10 is not found, unless an override define is present
// It is also disabled if -nographics is given from the command line.
#if !(SRP_10_0_0_OR_NEWER || HYBRID_RENDERER_ENABLE_WITHOUT_SRP)
#define HYBRID_RENDERER_DISABLED
#endif

using System;
using Unity.Assertions;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
#endregion

#if !UNITY_BURST_EXPERIMENTAL_ATOMIC_INTRINSICS
#error Latios Framework requires UNITY_BURST_EXPERIMENTAL_ATOMIC_INTRINSICS to be defined in your scripting define symbols.
#endif

namespace Latios.Kinemation.Systems
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(UpdatePresentationSystemGroup))]
    [UpdateAfter(typeof(EntitiesGraphicsSystem))]
    [DisableAutoCreation]
    [BurstCompile]
    public unsafe partial class LatiosEntitiesGraphicsSystem : SubSystem
    {
        #region Managed API
        /// <summary>
        /// Toggles the activation of EntitiesGraphicsSystem.
        /// </summary>
        /// <remarks>
        /// To disable this system, use the HYBRID_RENDERER_DISABLED define.
        /// </remarks>
#if HYBRID_RENDERER_DISABLED
        public static bool EntitiesGraphicsEnabled => false;
#else
        public static bool EntitiesGraphicsEnabled => EntitiesGraphicsUtils.IsEntitiesGraphicsSupportedOnSystem();
#endif

        /// <summary>
        /// The maximum GPU buffer size (in bytes) that a batch can access.
        /// </summary>
        public static int MaxBytesPerBatch => UseConstantBuffers ? MaxBytesPerCBuffer : kMaxBytesPerBatchRawBuffer;

        /// <summary>
        /// Registers a material property type with the given name.
        /// </summary>
        /// <param name="type">The type of material property to register.</param>
        /// <param name="propertyName">The name of the property.</param>
        /// <param name="overrideTypeSizeGPU">An optional size of the type on the GPU.</param>
        public static void RegisterMaterialPropertyType(Type type, string propertyName, short overrideTypeSizeGPU = -1)
        {
            Assert.IsTrue(type != null,                        "type must be non-null");
            Assert.IsTrue(!string.IsNullOrEmpty(propertyName), "Property name must be valid");

            short typeSizeCPU = (short)UnsafeUtility.SizeOf(type);
            if (overrideTypeSizeGPU == -1)
                overrideTypeSizeGPU = typeSizeCPU;

            // For now, we only support overriding one material property with one type.
            // Several types can override one property, but not the other way around.
            // If necessary, this restriction can be lifted in the future.
            if (s_TypeToPropertyMappings.ContainsKey(type))
            {
                string prevPropertyName = s_TypeToPropertyMappings[type].Name;
                Assert.IsTrue(propertyName.Equals(
                                  prevPropertyName),
                              $"Attempted to register type {type.Name} with multiple different property names. Registered with \"{propertyName}\", previously registered with \"{prevPropertyName}\".");
            }
            else
            {
                var pm                         = new NamedPropertyMapping();
                pm.Name                        = propertyName;
                pm.SizeCPU                     = typeSizeCPU;
                pm.SizeGPU                     = overrideTypeSizeGPU;
                s_TypeToPropertyMappings[type] = pm;
            }
        }

        /// <summary>
        /// A templated version of the material type registration method.
        /// </summary>
        /// <typeparam name="T">The type of material property to register.</typeparam>
        /// <param name="propertyName">The name of the property.</param>
        /// <param name="overrideTypeSizeGPU">An optional size of the type on the GPU.</param>
        public static void RegisterMaterialPropertyType<T>(string propertyName, short overrideTypeSizeGPU = -1)
            where T : IComponentData
        {
            RegisterMaterialPropertyType(typeof(T), propertyName, overrideTypeSizeGPU);
        }

        /// <summary>
        /// Registers a material with the Entities Graphics System.
        /// </summary>
        /// <param name="material">The material instance to register</param>
        /// <returns>Returns the batch material ID</returns>
        public BatchMaterialID RegisterMaterial(Material material) => m_unityEntitiesGraphicsSystem.RegisterMaterial(material);

        /// <summary>
        /// Registers a mesh with the Entities Graphics System.
        /// </summary>
        /// <param name="mesh">Mesh instance to register</param>
        /// <returns>Returns the batch mesh ID</returns>
        public BatchMeshID RegisterMesh(Mesh mesh) => m_unityEntitiesGraphicsSystem.RegisterMesh(mesh);

        /// <summary>
        /// Unregisters a material from the Entities Graphics System.
        /// </summary>
        /// <param name="material">Material ID received from <see cref="RegisterMaterial"/></param>
        public void UnregisterMaterial(BatchMaterialID material) => m_unityEntitiesGraphicsSystem.UnregisterMaterial(material);

        /// <summary>
        /// Unregisters a mesh from the Entities Graphics System.
        /// </summary>
        /// <param name="mesh">A mesh ID received from <see cref="RegisterMesh"/>.</param>
        public void UnregisterMesh(BatchMeshID mesh) => m_unityEntitiesGraphicsSystem.UnregisterMesh(mesh);

        /// <summary>
        /// Returns the <see cref="Mesh"/> that corresponds to the given registered mesh ID, or <c>null</c> if no such mesh exists.
        /// </summary>
        /// <param name="mesh">A mesh ID received from <see cref="RegisterMesh"/>.</param>
        /// <returns>The <see cref="Mesh"/> object corresponding to the given mesh ID if the ID is valid, or <c>null</c> if it's not valid.</returns>
        public Mesh GetMesh(BatchMeshID mesh) => m_BatchRendererGroup.GetRegisteredMesh(mesh);

        /// <summary>
        /// Returns the <see cref="Material"/> that corresponds to the given registered material ID, or <c>null</c> if no such material exists.
        /// </summary>
        /// <param name="material">A material ID received from <see cref="RegisterMaterial"/>.</param>
        /// <returns>The <see cref="Material"/> object corresponding to the given material ID if the ID is valid, or <c>null</c> if it's not valid.</returns>
        public Material GetMaterial(BatchMaterialID material) => m_BatchRendererGroup.GetRegisteredMaterial(material);
        #endregion
    }
}

