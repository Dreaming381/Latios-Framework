using Unity.Entities;

namespace Latios.Authoring.Systems
{
    //There is an issue where Unity does not inject conversion systems into nested ComponentSystemGroups
    //In the meantime, subclass GameObjectConversionConfigurationSystem

    [UpdateInGroup(typeof(GameObjectDeclareReferencedObjectsGroup), OrderFirst = true)]
    [WorldSystemFilter(WorldSystemFilterFlags.GameObjectConversion)]
    public class GameObjectConversionConfigurationSystemGroup : ComponentSystemGroup
    {
        protected override void OnUpdate()
        {
            //base.OnUpdate();
            foreach (var sys in World.Systems)
            {
                if (sys is GameObjectConversionConfigurationSystem config)
                {
                    config.Enabled = false;
                }
            }
            World.GetExistingSystem<GameObjectDeclareReferencedObjectsGroup>().SortSystems();
            Enabled = false;
        }
    }

    //This may be overkill, but I do not understand incremental conversion, so this is here to gaurd against recycling of the conversion world.
    public class ResetGameObjectConversionConfigurationSystem : GameObjectConversionSystem
    {
        protected override void OnUpdate()
        {
            World.GetExistingSystem<GameObjectConversionConfigurationSystemGroup>().Enabled = true;

            foreach (var sys in World.Systems)
            {
                if (sys is GameObjectConversionConfigurationSystem config)
                {
#if UNITY_EDITOR
                    config.Enabled = config.ShouldRunConversionSystem();
#else
                    config.Enabled = true;
#endif
                }
            }
        }
    }

    [UpdateInGroup(typeof(GameObjectDeclareReferencedObjectsGroup), OrderFirst = true)]
    [UpdateBefore(typeof(GameObjectConversionConfigurationSystemGroup))]
    public abstract class GameObjectConversionConfigurationSystem : GameObjectConversionSystem
    {
    }
}

