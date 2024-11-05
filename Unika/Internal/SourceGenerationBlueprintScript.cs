#if false
namespace Latios.Unika.SGB
{
    [global::System.Runtime.CompilerServices.CompilerGenerated]
    [global::Unity.Burst.BurstCompile]
    partial struct UserScript : global::Latios.Unika.InternalSourceGen.StaticAPI.IUnikaScriptSourceGenerated
    {
        public struct __DowncastHelper
        {
            global::Latios.Unika.Script<UserScript> m_script;

            public static implicit operator __DowncastHelper(global::Latios.Unika.Script<UserScript> script) => new __DowncastHelper
            {
                m_script = script
            };

            public static implicit operator global::Latios.Unika.SGB.IUpdate.Interface(__DowncastHelper helper)
            {
                return global::Latios.Unika.InternalSourceGen.StaticAPI.DownCast<global::Latios.Unika.SGB.IUpdate.Interface, global::Latios.Unika.SGB.IUpdate, UserScript>(
                    helper.m_script);
            }
            public static implicit operator global::Latios.Unika.SGB.IUpdate.InterfaceRef(__DowncastHelper helper)
            {
                global::Latios.Unika.ScriptRef scriptRef = helper.m_script;
                return global::Unity.Collections.LowLevel.Unsafe.UnsafeUtility.As<global::Latios.Unika.ScriptRef, global::Latios.Unika.SGB.IUpdate.InterfaceRef>(ref scriptRef);
            }
        }

        [global::AOT.MonoPInvokeCallback(typeof(global::Latios.Unika.InternalSourceGen.StaticAPI.BurstDispatchScriptDelegate))]
        [global::UnityEngine.Scripting.Preserve]
        [global::Unity.Burst.BurstCompile]
        public static void __BurstDispatch_global_Latios_Unika_SGB_IUpdate(global::Latios.Unika.InternalSourceGen.StaticAPI.ContextPtr context, int operation)
        {
            global::Latios.Unika.SGB.IUpdate.__Dispatch<UserScript>(context, operation);
        }
    }

    // Note: UserScript must be either internal or public at the global level for this to work.
    // The accessibility of this static class should be dependent on which it is.
    static class __global_Latios_Unika_SGB_UserScript_DowncastExtensions
    {
        public static global::Latios.Unika.SGB.UserScript.__DowncastHelper ToInterface(this global::Latios.Unika.Script<global::Latios.Unika.SGB.UserScript> script) => script;
    }
}
#endif

