using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios.Calligraphics.Systems
{
    [UpdateInGroup(typeof(Latios.Systems.LatiosWorldSyncGroup), OrderLast = true)]
    [DisableAutoCreation]
    public partial class CalligraphicsFrameSyncPointSuperSystem : SuperSystem
    {
        protected override void CreateSystems()
        {
            GetOrCreateAndAddUnmanagedSystem<NativeFontLoaderSystem>();
            GetOrCreateAndAddUnmanagedSystem<TextRendererInitializeSystem>();
        }
    }

    [UpdateInGroup(typeof(Unity.Rendering.StructuralChangePresentationSystemGroup), OrderLast = true)]
    [DisableAutoCreation]
    public partial class CalligraphicsRenderSyncPointSuperSystem : SuperSystem
    {
        protected override void CreateSystems()
        {
            GetOrCreateAndAddUnmanagedSystem<TextRendererInitializeSystem>();
        }
    }

    [UpdateInGroup(typeof(Unity.Rendering.UpdatePresentationSystemGroup))]
    [DisableAutoCreation]
    public partial class CalligraphicsPresentationSuperSystem : SuperSystem
    {
        protected override void CreateSystems()
        {
            GetOrCreateAndAddUnmanagedSystem<GenerateGlyphsSystem>();
            GetOrCreateAndAddManagedSystem<CalligraphicsAnimationSuperSystem>();
            GetOrCreateAndAddUnmanagedSystem<UpdateGlyphsRenderersSystem>();
        }
    }

    [DisableAutoCreation]
    public partial class CalligraphicsAnimationSuperSystem : SuperSystem
    {
        protected override void CreateSystems()
        {
            EnableSystemSorting = true;
        }
    }
}

