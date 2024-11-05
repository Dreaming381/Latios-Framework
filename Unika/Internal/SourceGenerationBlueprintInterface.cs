#if false
namespace Latios.Unika.SGB
{
    [global::System.Runtime.CompilerServices.CompilerGenerated]
    partial interface IUpdate : global::Latios.Unika.InternalSourceGen.StaticAPI.IUnikaInterfaceSourceGenerated
    {
        public struct Interface : global::Latios.Unika.InternalSourceGen.StaticAPI.IInterfaceDataTyped<IUpdate, Interface>,
            IUpdate,
                                  global::System.IEquatable<Interface>,
                                  global::System.IComparable<Interface>,
                                  global::System.IEquatable<InterfaceRef>,
                                  global::System.IComparable<InterfaceRef>,
            // No base interfaces
                                  global::System.IEquatable<global::Latios.Unika.Script>,
                                  global::System.IComparable<global::Latios.Unika.Script>,
                                  global::System.IEquatable<global::Latios.Unika.ScriptRef>,
                                  global::System.IComparable<global::Latios.Unika.ScriptRef>
        {
            global::Latios.Unika.InternalSourceGen.StaticAPI.InterfaceData data;

            public global::Unity.Entities.Entity entity => data.entity;
            public global::Latios.Unika.EntityScriptCollection allScripts => data.allScripts;
            public int indexInEntity => data.indexInEntity;
            public byte userByte { get => data.userByte; set => data.userByte = value; }
            public bool userFlagA { get => data.userFlagA; set => data.userFlagA = value; }
            public bool userFlagB { get => data.userFlagB; set => data.userFlagB = value; }

            public static implicit operator InterfaceRef(Interface derived) => derived.data.ToRef<InterfaceRef>();
            public static implicit operator Script(Interface derived) => derived.data.ToScript();
            public static implicit operator ScriptRef(Interface derived) => derived.data.ToScript();
            // No base interfaces

            public static bool operator ==(Interface lhs, Interface rhs) => (Script)lhs == (Script)rhs;
            public static bool operator !=(Interface lhs, Interface rhs) => (Script)lhs != (Script)rhs;
            public static bool operator ==(Interface lhs, InterfaceRef rhs) => (ScriptRef)lhs == (ScriptRef)rhs;
            public static bool operator !=(Interface lhs, InterfaceRef rhs) => (ScriptRef)lhs != (ScriptRef)rhs;
            public static bool operator ==(Interface lhs, Script rhs) => (Script)lhs == rhs;
            public static bool operator !=(Interface lhs, Script rhs) => (Script)lhs != rhs;
            public static bool operator ==(Interface lhs, ScriptRef rhs) => (ScriptRef)lhs == rhs;
            public static bool operator !=(Interface lhs, ScriptRef rhs) => (ScriptRef)lhs != rhs;
            // No base interfaces

            public int CompareTo(Interface other) => ((Script)this).CompareTo((Script)other);
            public int CompareTo(InterfaceRef other) => ((ScriptRef)this).CompareTo((ScriptRef)other);
            public int CompareTo(Script other) => ((Script)this).CompareTo(other);
            public int CompareTo(ScriptRef other) => ((ScriptRef)this).CompareTo(other);
            // No base interfaces

            public bool Equals(Interface other) => ((Script)this).Equals((Script)other);
            public bool Equals(InterfaceRef other) => ((ScriptRef)this).Equals((ScriptRef)other);
            public bool Equals(Script other) => ((Script)this).Equals(other);
            public bool Equals(ScriptRef other) => ((ScriptRef)this).Equals(other);
            // No base interfaces

            public override bool Equals(object obj) => ((Script)this).Equals(obj);
            public override int GetHashCode() => ((Script)this).GetHashCode();
            public override string ToString() => ((Script)this).ToString();
            public global::Unity.Collections.FixedString128Bytes ToFixedString() => ((Script)this).ToFixedString();
            public static Interface Null => default;

            public global::Latios.Unika.Script ToScript() => this;
            global::Latios.Unika.ScriptRef global::Latios.Unika.IScriptExtensionsApi.ToRef() => this;
            Interface global::Latios.Unika.InternalSourceGen.StaticAPI.IInterfaceDataTyped<IUpdate, Interface>.assign { set => this = value; }

            // Interface implementation stuff goes here
            public void Update(ref global::Latios.Unika.SGB.Context context)
            {
                global::Latios.Unika.InternalSourceGen.StaticAPI.Dispatch(ref data, 0, ref context);
            }
        }

        public struct InterfaceRef : global::Latios.Unika.InternalSourceGen.StaticAPI.IInterfaceRefData,
                                     global::System.IEquatable<InterfaceRef>,
                                     global::System.IComparable<InterfaceRef>,
            // No base interfaces
                                     global::System.IEquatable<global::Latios.Unika.ScriptRef>,
                                     global::System.IComparable<global::Latios.Unika.ScriptRef>
        {
            global::Latios.Unika.InternalSourceGen.StaticAPI.InterfaceRefData data;

            public global::Unity.Entities.Entity entity => data.entity;

            public bool TryResolve(in global::Latios.Unika.EntityScriptCollection allScripts, out Interface script)
            {
                return global::Latios.Unika.InternalSourceGen.StaticAPI.TryResolve<Interface>(ref data, in allScripts, out script);
            }

            public bool TryResolve<TResolver>(ref TResolver resolver, out Interface script) where TResolver : unmanaged, global::Latios.Unika.IScriptResolverBase
            {
                return global::Latios.Unika.InternalSourceGen.StaticAPI.TryResolve<Interface, TResolver>(ref data, ref resolver, out script);
            }
            public Interface Resolve(in global::Latios.Unika.EntityScriptCollection allScripts)
            {
                return global::Latios.Unika.InternalSourceGen.StaticAPI.Resolve<Interface>(ref data, in allScripts);
            }
            public Interface Resolve<TResolver>(ref TResolver resolver) where TResolver : unmanaged, global::Latios.Unika.IScriptResolverBase
            {
                return global::Latios.Unika.InternalSourceGen.StaticAPI.Resolve<Interface, TResolver>(ref data, ref resolver);
            }
            public static implicit operator ScriptRef(InterfaceRef derived) => derived.data.ToScriptRef();
            // No base interfaces

            public static bool operator ==(InterfaceRef lhs, InterfaceRef rhs) => (ScriptRef)lhs == (ScriptRef)rhs;
            public static bool operator !=(InterfaceRef lhs, InterfaceRef rhs) => (ScriptRef)lhs != (ScriptRef)rhs;
            public static bool operator ==(InterfaceRef lhs, ScriptRef rhs) => (ScriptRef)lhs == rhs;
            public static bool operator !=(InterfaceRef lhs, ScriptRef rhs) => (ScriptRef)lhs != rhs;
            // No base interfaces

            public int CompareTo(InterfaceRef other) => ((ScriptRef)this).CompareTo((ScriptRef)other);
            public int CompareTo(ScriptRef other) => ((ScriptRef)this).CompareTo(other);
            // No base interfaces

            public bool Equals(InterfaceRef other) => ((ScriptRef)this).Equals((ScriptRef)other);
            public bool Equals(ScriptRef other) => ((ScriptRef)this).Equals(other);
            // No base interfaces

            public override bool Equals(object obj) => ((ScriptRef)this).Equals(obj);
            public override int GetHashCode() => ((ScriptRef)this).GetHashCode();
            public override string ToString() => ((ScriptRef)this).ToString();
            public global::Unity.Collections.FixedString128Bytes ToFixedString() => ((ScriptRef)this).ToFixedString();
            public static InterfaceRef Null => default;
        }

        [global::UnityEngine.Scripting.Preserve]
        public static void __Initialize() => global::Latios.Unika.InternalSourceGen.StaticAPI.InitializeInterface<IUpdate>();

        [global::UnityEngine.Scripting.Preserve]
        public static void __Dispatch<TScriptType>(global::Latios.Unika.InternalSourceGen.StaticAPI.ContextPtr context, int operation) where TScriptType : unmanaged, IUpdate,
        IUnikaScript, global::Latios.Unika.InternalSourceGen.StaticAPI.IUnikaScriptSourceGenerated
        {
            switch (operation)
            {
                case 0:
                {
                    ref var script = ref global::Latios.Unika.InternalSourceGen.StaticAPI.ExtractScript<TScriptType>(context);
                    ref var arg0   = ref global::Latios.Unika.InternalSourceGen.StaticAPI.ExtractArg0<global::Latios.Unika.SGB.Context>(context);
                    script.Update(ref arg0);
                    break;
                }
            }
        }
    }
}
#endif

