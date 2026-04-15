using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Mathematics;

#if LATIOS_TRANSFORMS_UNITY
using Unity.Transforms;
using TransformComponent = Unity.Transforms.LocalToWorld;
#else
using TransformComponent = Latios.Transforms.WorldTransform;
#endif

namespace Latios.Transforms.Abstract
{
    public struct WorldTransformReadOnlyAspect : IAspect
    {
        RefRO<TransformComponent> worldTransform;

#if LATIOS_TRANSFORMS_UNITY
        public TransformQvvs worldTransformQvvs
        {
            get
            {
                ref readonly float4x4 ltw = ref worldTransform.ValueRO.Value;
                return new TransformQvvs(ltw.Translation(), ltw.Rotation(), ltw.Scale().x, 1f);
            }
        }

        public quaternion rotation => worldTransform.ValueRO.Rotation;
        public float3 position => worldTransform.ValueRO.Position;

        public bool isNativeQvvs => false;
        public float4x4 matrix4x4 => worldTransform.ValueRO.Value;
#else
        public TransformQvvs worldTransformQvvs => worldTransform.ValueRO.worldTransform;

        public quaternion rotation => worldTransform.ValueRO.rotation;
        public float3 position => worldTransform.ValueRO.position;

        public bool isNativeQvvs => true;
        public float4x4 matrix4x4 => worldTransform.ValueRO.worldTransform.ToMatrix4x4();
#endif

        public WorldTransformReadOnlyAspect(RefRO<TransformComponent> worldTransformRefRO)
        {
            worldTransform = worldTransformRefRO;
        }

        /// <summary>
        /// A container type that provides access to instances of the enclosing Aspect type, indexed by <see cref="Entity"/>.
        /// Equivalent to <see cref="ComponentLookup{T}"/> but for aspect types.
        /// Constructed from an system state via its constructor.
        /// </summary>
        /// <remarks> Using this in an IJobEntity is not supported. </remarks>
        public struct Lookup
        {
            [Unity.Collections.ReadOnly]
            ComponentLookup<TransformComponent> transformLookup;

            /// <summary>
            /// Create the aspect lookup from an system state.
            /// </summary>
            /// <param name="state">The system state to create the aspect lookup from.</param>
            public Lookup(ref SystemState state)
            {
                transformLookup = state.GetComponentLookup<TransformComponent>(true);
            }

            /// <summary>
            /// Update the lookup container.
            /// Must be called every frames before using the lookup.
            /// </summary>
            /// <param name="state">The system state the aspect lookup was created from.</param>
            public void Update(ref SystemState state)
            {
                transformLookup.Update(ref state);
            }

            /// <summary>
            /// Get an aspect instance pointing at a specific entity's components data.
            /// </summary>
            /// <param name="entity">The entity to create the aspect struct from.</param>
            /// <returns>Instance of the aspect struct pointing at a specific entity's components data.</returns>
            public WorldTransformReadOnlyAspect this[Entity entity] => new WorldTransformReadOnlyAspect(transformLookup.GetRefRO(entity));
        }

        /// <summary>
        /// Chunk of the enclosing aspect instances.
        /// the aspect struct itself is instantiated from multiple component data chunks.
        /// </summary>
        public struct ResolvedChunk
        {
            /// <summary>
            /// Chunk data for aspect field 'WorldTransformReadOnlyAspect.worldTransform'
            /// </summary>
            public Unity.Collections.NativeArray<TransformComponent> transformArray;

            /// <summary>
            /// Get an aspect instance pointing at a specific entity's component data in the chunk index.
            /// </summary>
            /// <param name="index"></param>
            /// <returns>Aspect for the entity in the chunk at the given index.</returns>
            public WorldTransformReadOnlyAspect this[int index] => new WorldTransformReadOnlyAspect(new RefRO<TransformComponent>(transformArray, index));

            /// <summary>
            /// Number of entities in this chunk.
            /// </summary>
            public int Length;
        }

        /// <summary>
        /// A handle to the enclosing aspect type, used to access a <see cref="ResolvedChunk"/>'s components data in a job.
        /// Equivalent to <see cref="ComponentTypeHandle{T}"/> but for aspect types.
        /// Constructed from an system state via its constructor.
        /// </summary>
        public struct TypeHandle
        {
            [Unity.Collections.ReadOnly]
            ComponentTypeHandle<TransformComponent> transformHandle;

            /// <summary>
            /// Create the aspect type handle from an system state.
            /// </summary>
            /// <param name="state">System state to create the type handle from.</param>
            public TypeHandle(ref SystemState state)
            {
                transformHandle = state.GetComponentTypeHandle<TransformComponent>(true);
            }

            /// <summary>
            /// Update the type handle container.
            /// Must be called every frames before using the type handle.
            /// </summary>
            /// <param name="state">The system state the aspect type handle was created from.</param>
            public void Update(ref SystemState state)
            {
                transformHandle.Update(ref state);
            }

            /// <summary>
            /// Get the enclosing aspect's <see cref="ResolvedChunk"/> from an <see cref="ArchetypeChunk"/>.
            /// </summary>
            /// <param name="chunk">The ArchetypeChunk to extract the aspect's ResolvedChunk from.</param>
            /// <returns>A ResolvedChunk representing all instances of the aspect in the chunk.</returns>
            public ResolvedChunk Resolve(in ArchetypeChunk chunk)
            {
                ResolvedChunk resolved;
                resolved.transformArray = chunk.GetNativeArray(ref transformHandle);
                resolved.Length         = chunk.Count;
                return resolved;
            }

            public bool DidChange(in ArchetypeChunk chunk, uint version)
            {
                return chunk.DidChange(ref transformHandle, version);
            }

            public bool Has(in ArchetypeChunk chunk) => chunk.Has(ref transformHandle);

            public bool isNativeQvvs => true;
        }

        public struct HasChecker
        {
            HasChecker<TransformComponent> checker;

            public bool this[ArchetypeChunk chunk] => checker[chunk];
        }

        void IAspect.Initialize(EntityManager entityManager, Entity entity)
        {
            entityManager.CompleteDependencyBeforeRO<TransformComponent>();
            worldTransform = entityManager.GetComponentLookup<TransformComponent>(true).GetRefRO(entity);
        }
    }
}

namespace Latios.Transforms.Abstract
{
    public static class QueryExtensions
    {
        public static FluentQuery WithoutWorldTransform(this FluentQuery query)
        {
            return query.Without<TransformComponent>();
        }

        public static FluentQuery WithWorldTransformReadOnly(this FluentQuery query)
        {
            return query.With<TransformComponent>(true);
        }

        public static void AddWorldTranformChangeFilter(this EntityQuery query)
        {
            query.AddChangedVersionFilter(ComponentType.ReadOnly<TransformComponent>());
        }

        public static ComponentType GetAbstractWorldTransformROComponentType()
        {
            return ComponentType.ReadOnly<TransformComponent>();
        }

        public static ComponentType GetAbstractWorldTransformRWComponentType()
        {
            return ComponentType.ReadWrite<TransformComponent>();
        }
    }
}

