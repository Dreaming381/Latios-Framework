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
        /// This also installs live baking deserialization (see InstallUnikaLiveBakingDeserialization()).
        /// This must be installed in a LatiosWorldUnmanaged, but can be safely installed in ICustomEditorBootstrap.
        /// Installing in the Editor world is optional.
        /// </summary>
        /// <param name="world">The World to install Unika Entity Serialization into. Must be a LatiosWorld.</param>
        public static void InstallUnikaEntitySerialization(World world)
        {
            BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<SerializeScriptEntitiesSystem>(),   world);
            BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<DeserializeScriptEntitiesSystem>(), world);
            InstallUnikaLiveBakingDeserialization(world);
        }

        /// <summary>
        /// Installs the system which restores entity and blob references inside scripts of open subscenes after each live bake.
        /// InstallUnikaEntitySerialization() already installs this, so you only need to call this if you use your own custom systems for serialization
        /// and deserialization instead. This method is a no-op in builds.
        /// </summary>
        /// <param name="world">The World to install Unika Live Baking Deserialization into. Must be a LatiosWorld.</param>
        public static void InstallUnikaLiveBakingDeserialization(World world)
        {
#if UNITY_EDITOR
            BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<LiveBakingDeserializeScriptsSystem>(), world);
#endif
        }
    }
}

