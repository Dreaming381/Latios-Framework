using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace Latios.Myri
{
    [CreateAssetMenu(fileName = "New AudioChannel", menuName = "Latios/Myri (Audio)/Audio Channel")]
    public class AudioChannelAsset : ScriptableObject
    {
        public unsafe AudioSourceChannelID GetChannelID(IBaker baker)
        {
            baker.DependsOn(this);

            var sa        = stackalloc Unity.Entities.Hash128[2];
            var array     = CollectionHelper.ConvertExistingDataToNativeArray<Unity.Entities.Hash128>(sa, 2, Allocator.None, true);
            var guidArray = array.GetSubArray(0, 1);
            guidArray[0]  = default;
#if UNITY_6000_3_OR_NEWER
            var entityIdArray = array.GetSubArray(1, 1).Reinterpret<EntityId>(16).GetSubArray(0, 1);
            entityIdArray[0] = GetInstanceID();
#if UNITY_EDITOR
            UnityEditor.AssetDatabase.EntityIdsToGUIDs(entityIdArray, guidArray.Reinterpret<UnityEditor.GUID>());
#endif
#else
            var instanceIdArray = array.GetSubArray(1, 1).Reinterpret<int>(16).GetSubArray(0, 1);
            instanceIdArray[0]  = GetInstanceID();
#if UNITY_EDITOR
            UnityEditor.AssetDatabase.InstanceIDsToGUIDs(instanceIdArray, guidArray.Reinterpret<UnityEditor.GUID>());
#endif
#endif
            return new AudioSourceChannelID { guid = guidArray[0] };
        }
    }
}

