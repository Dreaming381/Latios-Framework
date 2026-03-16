using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios.Systems
{
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor | WorldSystemFilterFlags.ThinClientSimulation)]
    [DisableAutoCreation]
    public partial class BeforeLiveBakingSuperSystem : SuperSystem
    {
        internal bool liveBakeTriggered = false;

        protected override void CreateSystems()
        {
            EnableSystemSorting = true;
            worldBlackboardEntity.AddComponent<SystemVersionBeforeLiveBake>();
        }

        protected override void OnUpdate()
        {
            if (liveBakeTriggered)
                return; // We already procesed once this frame

            liveBakeTriggered = true;
            var unmanaged     = latiosWorldUnmanaged;
            base.OnUpdate();
            worldBlackboardEntity.SetComponentData(new SystemVersionBeforeLiveBake
            {
                version = GlobalSystemVersion
            });
        }
    }

    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor | WorldSystemFilterFlags.ThinClientSimulation)]
#if UNITY_EDITOR
    [UpdateInGroup(typeof(Unity.Scenes.Editor.LiveConversionEditorSystemGroup), OrderLast = true)]
#endif
    [DisableAutoCreation]
    public partial class AfterLiveBakingSuperSystem : SuperSystem
    {
        BeforeLiveBakingSuperSystem beforeSystem;

        protected override void CreateSystems()
        {
            beforeSystem = World.GetOrCreateSystemManaged<BeforeLiveBakingSuperSystem>();

            EnableSystemSorting = true;
        }

        protected override void OnUpdate()
        {
            var latiosWorld                = latiosWorldUnmanaged;
            latiosWorld.liveBakedThisFrame = beforeSystem.liveBakeTriggered;
            if (!latiosWorldUnmanaged.liveBakedThisFrame)
                return;

            base.OnUpdate();
            beforeSystem.liveBakeTriggered = false;
        }
    }
}

