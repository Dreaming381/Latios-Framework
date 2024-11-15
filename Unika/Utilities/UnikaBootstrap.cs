using Latios.Unika.Systems;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Unika
{
    public static class UnikaBootstrap
    {
        /// <summary>
        /// Installs the Unika entity serialization systems to support entity remapping after instantiation.
        /// Serialization is installed at the beginning of the frame, while deserialization is installed at the end of InitializationSystemGroup.
        /// This must be installed in a LatiosWorldUnmanaged, but can be safely installed in ICustomEditorBootstrap.
        /// Installing in the Editor world is optional.
        /// </summary>
        /// <param name="world">The World to install Unika Entity Serialization into. Must be a LatiosWorld.</param>
        public static void InstallUnikaEntitySerialization(World world)
        {
            BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<SerializeScriptEntitiesSystem>(),   world);
            BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<DeserializeScriptEntitiesSystem>(), world);
        }
    }
}

