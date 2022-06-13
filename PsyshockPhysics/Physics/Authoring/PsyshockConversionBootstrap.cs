using Latios.Psyshock.Authoring.Systems;
using Unity.Entities;

namespace Latios.Psyshock.Authoring
{
    /// <summary>
    /// Static class containing installers for optional authoring time features in the Psyshock module
    /// </summary>
    public static class PsyshockConversionBootstrap
    {
        /// <summary>
        /// Enables conversion of Unity's legacy collider components into Psyshock colliders
        /// by installing the appropriate conversion systems into the conversion world
        /// </summary>
        /// <param name="world">The conversion world in which to install the legacy conversion systems</param>
        public static void InstallLegacyColliderConversion(World world)
        {
            if (!world.Flags.HasFlag(WorldFlags.Conversion))
                throw new System.InvalidOperationException("Psyshock Legacy Collider Conversion must be installed in a conversion world.");

            BootstrapTools.InjectSystem(typeof(LegacyConvexColliderPreConversionSystem), world);
            BootstrapTools.InjectSystem(typeof(LegacyColliderConversionSystem),          world);
        }
    }
}

