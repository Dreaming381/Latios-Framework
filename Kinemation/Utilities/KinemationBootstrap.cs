using Latios.Kinemation.Systems;
using Unity.Entities;
using Unity.Rendering;

namespace Latios.Kinemation
{
    public static class KinemationBootstrap
    {
        /// <summary>
        /// Installs the Kinemation renderer and additional Kinemation systems, and disables some Entities.Graphics systems which Kinemation replaces.
        /// This must be installed in a LatiosWorldUnmanaged, but can be safely installed in ICustomEditorBootstrap.
        /// This should be installed in both the Editor and runtime worlds.
        /// </summary>
        /// <param name="world">The World to install Kinemation into. Must be a LatiosWorld.</param>
        public static void InstallKinemation(World world)
        {
            RenderMeshUtilityReplacer.PatchRenderMeshUtility();

            var unityRenderer = world.GetExistingSystemManaged<EntitiesGraphicsSystem>();
            if (unityRenderer != null)
                unityRenderer.Enabled = false;
            var unitySkinning         = world.GetExistingSystemManaged<DeformationsInPresentation>();
            if (unitySkinning != null)
                unitySkinning.Enabled = false;
            var unityMatrixPrev       = world.GetExistingSystemManaged<MatrixPreviousSystem>();
            if (unityMatrixPrev != null)
                unityMatrixPrev.Enabled = false;
            var unityLODRequirements    = world.GetExistingSystemManaged<LODRequirementsUpdateSystem>();
            if (unityLODRequirements != null)
                unityLODRequirements.Enabled = false;
            var unityChunkStructure          = world.GetExistingSystemManaged<UpdateHybridChunksStructure>();
            if (unityChunkStructure != null)
                unityChunkStructure.Enabled = false;
            var unityLightProbe             = world.GetExistingSystemManaged<LightProbeUpdateSystem>();
            if (unityLightProbe != null)
                unityLightProbe.Enabled = false;
            var unityAddBounds          = world.GetExistingSystemManaged<AddWorldAndChunkRenderBounds>();
            if (unityAddBounds != null)
                unityAddBounds.Enabled = false;
            var unityUpdateBounds      = world.GetExistingSystemManaged<RenderBoundsUpdateSystem>();
            if (unityUpdateBounds != null)
                unityUpdateBounds.Enabled = false;

            BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<UpdateGraphicsBufferBrokerSystem>(),                     world);
            BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<KinemationRenderUpdateSuperSystem>(),                    world);
            BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<KinemationRenderSyncPointSuperSystem>(),                 world);
            BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<KinemationFrameSyncPointSuperSystem>(),                  world);
            BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<LatiosEntitiesGraphicsSystem>(),                         world);
            BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<KinemationPostRenderSuperSystem>(),                      world);
            BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<LatiosUpdateEntitiesGraphicsChunkStructureSystem>(),     world);

            BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<ForceInitializeUninitializedOptimizedSkeletonsSystem>(), world);
            BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<UpdateSocketsSystem>(),                                  world);
            BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<InitializeAnimatedBuffersSystem>(),                      world);
            BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<RotateAnimatedBuffersSystem>(),                          world);
            BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<UpdateMatrixPreviousSystem>(),                           world);

#if !LATIOS_TRANSFORMS_UNITY
            BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<InitializeMatrixPreviousSystem>(),                       world);
#endif
        }

        /// <summary>
        /// Installs a system that causes all dirty unique meshes to be uploaded within the custom graphics GPU dispatch systems.
        /// This is a workaround for Unity versions that internally delay the mesh upload and usage to the following frame if the
        /// mesh is uploaded during a BatchRendererGroup callback.
        /// This is typically only installed in runtime worlds.
        /// </summary>
        /// <param name="world">The World to install the system into. Must be a LatiosWorld.</param>
        public static void InstallUniqueMeshesEarlyUploader(LatiosWorld world)
        {
            world.worldBlackboardEntity.AddComponent<EnableCustomGraphicsTag>();
            BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<EnableUniqueMeshCustomGraphicsUploadSystem>(), world);
        }
    }
}

