using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.Unika.Authoring
{
    public abstract class UnikaScriptAuthoring<T> : UnikaScriptAuthoringBase where T : unmanaged, IUnikaScript, IUnikaScriptGen
    {
        new public ScriptRef<T> GetScriptRef(IBaker baker, TransformUsageFlags transformUsageFlags = TransformUsageFlags.None)
        {
            var untypedRef = base.GetScriptRef(baker, transformUsageFlags);
            return new ScriptRef<T>
            {
                m_cachedHeaderIndex = untypedRef.m_cachedHeaderIndex,
                m_entity            = untypedRef.m_entity,
                m_instanceId        = untypedRef.m_instanceId,
            };
        }
    }
}

