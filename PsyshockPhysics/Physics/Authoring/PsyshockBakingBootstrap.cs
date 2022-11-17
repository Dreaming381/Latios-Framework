using Latios.Authoring;
using Latios.Psyshock.Authoring.Systems;
using Unity.Entities;

namespace Latios.Psyshock.Authoring
{
    /// <summary>
    /// Static class containing installers for optional authoring time features in the Psyshock module
    /// </summary>
    public static class PsyshockBakingBootstrap
    {
        /// <summary>
        /// Enables baking of Unity's legacy collider components into Psyshock colliders
        /// by installing the appropriate bakers into the baking world.
        /// Smart Blobber Baking Systems and bakers for Psyshock authoring components are always installed.
        /// </summary>
        /// <param name="context">The custom context passed into ICustomBakingBootstrap</param>
        public static void InstallLegacyColliderBakers(ref CustomBakingBootstrapContext context)
        {
            context.filteredBakerTypes.Add(typeof(LegacySphereColliderBaker));
            context.filteredBakerTypes.Add(typeof(LegacyCapsuleColliderBaker));
            context.filteredBakerTypes.Add(typeof(LegacyBoxColliderBaker));
            context.filteredBakerTypes.Add(typeof(LegacyConvexColliderBaker));
            context.filteredBakerTypes.Add(typeof(LegacyCompoundColliderBaker));
        }
    }
}

