#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY

using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Transforms.Abstract
{
    public readonly partial struct WorldTransformReadOnlyAspect : IAspect
    {
        readonly RefRO<WorldTransform> worldTransform;

        public TransformQvvs worldTransformQvvs => worldTransform.ValueRO.worldTransform;

        public quaternion rotation => worldTransform.ValueRO.rotation;
        public float3 position => worldTransform.ValueRO.position;

        public bool isNativeQvvs => true;
        public float4x4 matrix4x4 => worldTransform.ValueRO.worldTransform.ToMatrix4x4();
    }

    public struct WorldTransformReadOnlyTypeHandle
    {
        ComponentTypeHandle<WorldTransform> worldTransformHandle;

        public WorldTransformReadOnlyTypeHandle(ref SystemState state)
        {
            worldTransformHandle = state.GetComponentTypeHandle<WorldTransform>(true);
        }

        public void Update(ref SystemState state)
        {
            worldTransformHandle.Update(ref state);
        }

        public bool DidChange(in ArchetypeChunk chunk, uint version)
        {
            return chunk.DidChange(ref worldTransformHandle, version);
        }

        public bool isNativeQvvs => true;

        public WorldTransformReadOnlyAspect.ResolvedChunk Resolve(in ArchetypeChunk chunk)
        {
            var result                                            = new WorldTransformReadOnlyAspect.ResolvedChunk();
            result.WorldTransformReadOnlyAspect_worldTransformNaC = chunk.GetNativeArray(ref worldTransformHandle);
            result.Length                                         = chunk.Count;
            return result;
        }
    }
}

#elif !LATIOS_TRANSFORMS_UNCACHED_QVVS && LATIOS_TRANSFORMS_UNITY
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Latios.Transforms.Abstract
{
    public readonly partial struct WorldTransformReadOnlyAspect : IAspect
    {
        readonly RefRO<LocalToWorld> localToWorld;

        public TransformQvvs worldTransformQvvs
        {
            get
            {
                ref readonly float4x4 ltw = ref localToWorld.ValueRO.Value;
                return new TransformQvvs(ltw.Translation(), ltw.Rotation(), ltw.Scale().x, 1f);
            }
        }

        public quaternion rotation => localToWorld.ValueRO.Rotation;
        public float3 position => localToWorld.ValueRO.Position;

        public bool isNativeQvvs => false;
        public float4x4 matrix4x4 => localToWorld.ValueRO.Value;
    }

    public struct WorldTransformReadOnlyTypeHandle
    {
        ComponentTypeHandle<LocalToWorld> ltwHandle;

        public WorldTransformReadOnlyTypeHandle(ref SystemState state)
        {
            ltwHandle = state.GetComponentTypeHandle<LocalToWorld>(true);
        }

        public void Update(ref SystemState state)
        {
            ltwHandle.Update(ref state);
        }

        public bool DidChange(in ArchetypeChunk chunk, uint version)
        {
            return chunk.DidChange(ref ltwHandle, version);
        }

        public bool isNativeQvvs => false;

        public WorldTransformReadOnlyAspect.ResolvedChunk Resolve(in ArchetypeChunk chunk)
        {
            var result = new WorldTransformReadOnlyAspect.ResolvedChunk();
            result.WorldTransformReadOnlyAspect_localToWorldNaC = chunk.GetNativeArray(ref ltwHandle);
            result.Length                                       = chunk.Count;
            return result;
        }
    }
}
#endif

namespace Latios.Transforms.Abstract
{
    public static class QueryExtensions
    {
        public static FluentQuery WithWorldTransformReadOnly(this FluentQuery query)
        {
#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
            return query.With<WorldTransform>(true);
#elif !LATIOS_TRANSFORMS_UNCACHED_QVVS && LATIOS_TRANSFORMS_UNITY
            return query.With<LocalToWorld>(true);
#else
            throw new System.NotImplementedException();
#endif
        }

        public static void AddWorldTranformChangeFilter(this EntityQuery query)
        {
#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
            query.AddChangedVersionFilter(ComponentType.ReadOnly<WorldTransform>());
#elif !LATIOS_TRANSFORMS_UNCACHED_QVVS && LATIOS_TRANSFORMS_UNITY
            query.AddChangedVersionFilter(ComponentType.ReadOnly<LocalToWorld>());
#else
            throw new System.NotImplementedException();
#endif
        }

        public static ComponentType GetAbstractWorldTransformROComponentType()
        {
#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
            return ComponentType.ReadOnly<WorldTransform>();
#elif !LATIOS_TRANSFORMS_UNCACHED_QVVS && LATIOS_TRANSFORMS_UNITY
            return ComponentType.ReadOnly<LocalToWorld>();
#else
            throw new System.NotImplementedException();
#endif
        }

        public static ComponentType GetAbstractWorldTransformRWComponentType()
        {
#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
            return ComponentType.ReadWrite<WorldTransform>();
#elif !LATIOS_TRANSFORMS_UNCACHED_QVVS && LATIOS_TRANSFORMS_UNITY
            return ComponentType.ReadWrite<LocalToWorld>();
#else
            throw new System.NotImplementedException();
#endif
        }
    }
}

