using Latios.Authoring;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Unika.Authoring
{
    /// <summary>
    /// Static class containing installers for optional authoring time features in the Unika module
    /// </summary>
    public static class UnikaBakingBootstrap
    {
        /// <summary>
        /// Adds a Unika baker into the baking world which adds the runtime components for the built-in entity serialization system.
        /// Don't install this if you plan to use your own entity serialization triggering mechanism.
        /// </summary>
        /// <param name="context">The baking context in which to install the Unika baker</param>
        public static void InstallUnikaEntitySerialization(ref CustomBakingBootstrapContext context)
        {
            context.filteredBakerTypes.Add(typeof(UnikaScriptBufferEntitySerializationBaker));
        }
    }
}

