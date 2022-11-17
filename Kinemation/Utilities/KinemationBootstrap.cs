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
        /// </summary>
        /// <param name="world">The World to install Kinemation into. Must be a LatiosWorldUnmanaged.</param>
        public static void InstallKinemation(World world)
        {
            if (!UnityEngine.SystemInfo.supportsAsyncGPUReadback)
            {
                throw new System.InvalidOperationException("Kinemation only works on platforms which support Async GPU Readback.");
            }

            var unityRenderer = world.GetExistingSystemManaged<EntitiesGraphicsSystem>();
            if (unityRenderer != null)
                unityRenderer.Enabled = false;
            var unitySkinning         = world.GetExistingSystemManaged<DeformationsInPresentation>();
            if (unitySkinning != null)
                unitySkinning.Enabled = false;
            var unityMatrixPrev       = world.GetExistingSystemManaged<MatrixPreviousSystem>();
            if (unityMatrixPrev != null)
                unityMatrixPrev.Enabled = false;

            BootstrapTools.InjectSystem(typeof(KinemationRenderUpdateSuperSystem),    world);
            BootstrapTools.InjectSystem(typeof(KinemationRenderSyncPointSuperSystem), world);
            BootstrapTools.InjectSystem(typeof(KinemationFrameSyncPointSuperSystem),  world);
            BootstrapTools.InjectSystem(typeof(LatiosEntitiesGraphicsSystem),         world);
            BootstrapTools.InjectSystem(typeof(KinemationPostRenderSuperSystem),      world);
            BootstrapTools.InjectSystem(typeof(CopyTransformFromBoneSystem),          world);
        }
    }
}

