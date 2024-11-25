using Unity.Collections;
using UnityEditor;
using UnityEngine;

namespace Latios.LifeFX
{
    /// <summary>
    /// Don't touch this. See GraphicsEventTunnel<T> instead.
    /// </summary>
    public abstract class GraphicsEventTunnelBase : ScriptableObject
    {
        [SerializeField, HideInInspector] Unity.Entities.Hash128 hash = default;

        internal abstract TypeInfo GetEventType();

        internal abstract int GetEventIndex();

        internal struct TypeInfo
        {
            public System.Type type;
            public int         size;
            public int         alignment;
        }

        private void OnEnable()
        {
            if (GraphicsEventTypeRegistry.IsInitializing())
                return; // This is a temporary object.

            GraphicsEventTypeRegistry.Init();
            GraphicsEventTypeRegistry.s_eventHashManager.Data.Add(this, hash);
        }

#if UNITY_EDITOR
        private unsafe void Awake()
        {
            if (GraphicsEventTypeRegistry.IsInitializing())
                return; // This is a temporary object.

            var sa              = stackalloc Unity.Entities.Hash128[2];
            var array           = CollectionHelper.ConvertExistingDataToNativeArray<Unity.Entities.Hash128>(sa, 2, Allocator.None, true);
            var guidArray       = array.GetSubArray(0, 1).Reinterpret<UnityEditor.GUID>();
            var instanceIdArray = array.GetSubArray(1, 1).Reinterpret<int>(16).GetSubArray(0, 1);
            guidArray[0]       = default;
            instanceIdArray[0] = GetInstanceID();
            AssetDatabase.InstanceIDsToGUIDs(instanceIdArray, guidArray);
            if (hash != array[0])
            {
                hash = array[0];
                EditorUtility.SetDirty(this);
            }
        }
#endif
    }
}

