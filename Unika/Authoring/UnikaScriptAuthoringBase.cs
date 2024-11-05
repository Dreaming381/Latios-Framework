using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Latios.Unika.Authoring
{
    [RequireComponent(typeof(UnikaScriptBufferAuthoring))]
    public abstract class UnikaScriptAuthoringBase : MonoBehaviour
    {
        private protected static List<UnikaScriptAuthoringBase> m_scriptsCache;

        public abstract bool IsValid();

        public abstract void Bake(IBaker baker, ref AuthoredScriptAssignment toAssign, Entity smartPostProcessTarget);

        public ScriptRef GetScriptRef(IBaker baker, TransformUsageFlags transformUsageFlags = TransformUsageFlags.None)
        {
            if (!enabled)
                return default;

            m_scriptsCache.Clear();
            baker.GetComponents(this, m_scriptsCache);
            for (int i = 0, validBefore = 0; i < m_scriptsCache.Count; i++)
            {
                var s = m_scriptsCache[i];
                if (s == this)
                {
                    if (IsValid())
                    {
                        return new ScriptRef
                        {
                            m_entity            = baker.GetEntity(this, transformUsageFlags),
                            m_instanceId        = validBefore + 1,
                            m_cachedHeaderIndex = i
                        };
                    }
                    else
                        return default;
                }
                else if (s.enabled && s.IsValid())
                    validBefore++;
            }
            return default;
        }
    }

    public struct AuthoredScriptAssignment
    {
        internal NativeArray<byte> scriptPayload;
        internal int               scriptType;
        public byte                userByte;
        public bool                userFlagA;
        public bool                userFlagB;

        public unsafe void Assign<T>(ref T script) where T : unmanaged, IUnikaScript, IUnikaScriptGen
        {
            var scriptSize = UnsafeUtility.SizeOf<T>();
            scriptType     = ScriptTypeInfoManager.GetScriptRuntimeId<T>().runtimeId;
            scriptPayload  = new NativeArray<byte>(scriptSize, Allocator.Temp);
            UnsafeUtility.MemCpy(scriptPayload.GetUnsafePtr(), UnsafeUtility.AddressOf(ref script), scriptSize);
        }
    }

    public static class UnikaBakingUtilities
    {
        public static unsafe Entity AddScript<T>(this IBaker baker,
                                                 UnikaScriptBufferAuthoring targetBuffer,
                                                 ref T script,
                                                 byte userByte = 0,
                                                 bool userFlagA = false,
                                                 bool userFlagB = false) where T : unmanaged, IUnikaScript, IUnikaScriptGen
        {
            var entity = baker.CreateAdditionalEntity(TransformUsageFlags.None, true);
            var size   = UnsafeUtility.SizeOf<T>();
            baker.AddComponent(entity, new BakedScriptMetadata
            {
                scriptRef = new ScriptRef
                {
                    m_entity            = baker.GetEntity(targetBuffer, TransformUsageFlags.None),
                    m_cachedHeaderIndex = -1,
                    m_instanceId        = 0
                },
                scriptType = ScriptTypeInfoManager.GetScriptRuntimeId<T>().runtimeId,
                userFlagA  = userFlagA,
                userByte   = userByte,
                userFlagB  = userFlagB
            });
            var bytes = baker.AddBuffer<BakedScriptByte>(entity);
            bytes.ResizeUninitialized(size);
            UnsafeUtility.MemCpy(bytes.GetUnsafePtr(), UnsafeUtility.AddressOf(ref script), size);
            return entity;
        }

        public static unsafe ref T GetBakedScriptInPostProcess<T>(this EntityManager entityManager, Entity bakingOnlyEntity) where T : unmanaged, IUnikaScript, IUnikaScriptGen
        {
            var bytes = entityManager.GetBuffer<BakedScriptByte>(bakingOnlyEntity);
            return ref UnsafeUtility.AsRef<T>(bytes.GetUnsafePtr());
        }
    }

    [BakeDerivedTypes]
    public class UnikaScriptAuthoringBaseBaker : Baker<UnikaScriptAuthoringBase>
    {
        public override void Bake(UnikaScriptAuthoringBase authoring)
        {
            if (!authoring.IsValid())
                return;

            var                      entity     = CreateAdditionalEntity(TransformUsageFlags.None, true);
            AuthoredScriptAssignment assignment = default;
            authoring.Bake(this, ref assignment, entity);
            if (assignment.scriptPayload.Length == 0)
                throw new System.InvalidOperationException(
                    $"The AuthoredScriptAssignment was left unassigned for object {authoring.name}. Please fix this issue before attempting to enter play mode, or else Unity may crash.");

            var bytes = AddBuffer<BakedScriptByte>(entity).Reinterpret<byte>();
            bytes.AddRange(assignment.scriptPayload);
            AddComponent(entity, new BakedScriptMetadata
            {
                scriptRef  = authoring.GetScriptRef(this),
                userFlagA  = assignment.userFlagA,
                scriptType = assignment.scriptType,
                userByte   = assignment.userByte,
                userFlagB  = assignment.userFlagB,
            });
        }
    }
}

