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
            // The rendering systems load compute shaders and allocate GraphicsBuffers during
            // OnCreate, which cannot work without a graphics device. Publish that fact on the
            // worldBlackboardEntity so every other installer and SuperSystem can skip its own
            // graphics systems, then install only the animation half here.
            var noGraphics = !LatiosEntitiesGraphicsSystem.EntitiesGraphicsEnabled;
            if (noGraphics)
                (world as LatiosWorld).worldBlackboardEntity.AddComponent<NoGraphicsTag>();

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

            if (!noGraphics)
            {
                BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<UpdateGraphicsBufferBrokerSystem>(),                 world);
                BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<KinemationRenderSyncPointSuperSystem>(),             world);
                BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<KinemationFrameSyncPointSuperSystem>(),              world);
                BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<LatiosEntitiesGraphicsSystem>(),                     world);
                BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<KinemationPostRenderSuperSystem>(),                  world);
                BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<LatiosUpdateEntitiesGraphicsChunkStructureSystem>(), world);
            }

            BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<ForceInitializeUninitializedOptimizedSkeletonsSystem>(), world);
            BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<InitializeAnimatedBuffersSystem>(),                      world);
            BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<RotateAnimatedBuffersSystem>(),                          world);
            BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<UpdateMatrixPreviousSystem>(),                           world);

#if !LATIOS_TRANSFORMS_UNITY
            BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<InitializeMatrixPreviousSystem>(),                       world);
#else
            BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<UpdateSocketsSystem>(),                                  world);
#endif

            if (world.GetExistingSystemManaged<Latios.Systems.TickedArchetypeCorrectionSystemGroup>() != null)
            {
                BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<TickedOptimizedSkeletonHistorySystem>(), world);
                BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<InterpolateOptimizedSkeletonsSystem>(),  world);
            }

#if UNITY_EDITOR
            if (!noGraphics)
                BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<KinemationAfterLiveBakingSuperSystem>(), world);
#endif
        }

        /// <summary>
        /// Install a created CPU occlusion culling system into Kinemation's culling loop.
        /// This is installed after frustum culling and LODs have been processed, and before draw commands are generated.
        /// </summary>
        /// <param name="world">The world Kinemation is installed in</param>
        /// <param name="system">The system that should be inserted into the culling loop</param>
        public static void InstallOcclusionCullingSystem(World world, SystemHandle system)
        {
            var cullingSuperSystem = world.GetExistingSystemManaged<KinemationCullingSuperSystem>();
            cullingSuperSystem.InstallOcclusionCullingSystem(system);
        }
    }
}

