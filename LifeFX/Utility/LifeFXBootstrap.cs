using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.LifeFX
{
    public static class LifeFXBootstrap
    {
        public static void InstallLifeFX(World world)
        {
            BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<Systems.InitializeVisualEffectMailboxesSystem>(), world);
            BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<Systems.CollectVisualEffectMailSystem>(),         world);
            BootstrapTools.InjectSystem(TypeManager.GetSystemTypeIndex<Systems.UploadVisualEffectMailSystem>(),          world);
        }
    }
}

