using Unity.Entities;

namespace Latios.Myri
{
    /// <summary>
    /// Static class containing installers for optional runtime features in the Myri module
    /// </summary>
    public static class MyriBootstrap
    {
        /// <summary>
        /// Installs Myri into the World at its default location
        /// </summary>
        /// <param name="world">The runtime world in which Myri should be installed</param>
        public static void InstallMyri(World world)
        {
            if (world.Flags.HasFlag(WorldFlags.Conversion))
                throw new System.InvalidOperationException("Cannot install Myri runtime in a conversion world.");

            BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<Systems.AudioSystem>(), world);
        }
    }
}

