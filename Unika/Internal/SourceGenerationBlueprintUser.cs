#if false
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Unika.SGB
{
    struct Context
    {
        public LookupScriptResolver resolver;
    }

    partial interface IUpdate : IUnikaInterface
    {
        public void Update(ref Context context);
    }

    partial interface IStartUpdate : IUnikaInterface, IUpdate
    {
        public void Start(ref Context context);
    }

    partial struct UserScript : IUnikaScript, IStartUpdate
    {
        public void Start(ref Context context)
        {
            UnityEngine.Debug.Log("Starting");
        }

        public void Update(ref Context context)
        {
            UnityEngine.Debug.Log("Updating");
        }
    }

    static class Test
    {
        public static void DoTest()
        {
            Script<UserScript>    su    = default;
            Script s     = su;
            IUpdate.Interface i     = su.ToInterface();
            IUpdate.InterfaceRef ir    = su.ToInterface();
            ScriptRef<UserScript> srefu = su;
            ScriptRef sref  = su;
            s.TryCastScript<UserScript>(out var suOut);
            i.TryCastScript(out suOut);
            ir.Resolve(default).TryCastScript(out suOut);
            srefu.Resolve(default).TryCastScript(out suOut);
            sref.Resolve(default).TryCastScript(out suOut);
            s.TryCast(out IUpdate.Interface iOut);
            i.ToScript().TryCast(out suOut);
            Context context = default;
            i.Update(ref context);
        }
    }
}
#endif

