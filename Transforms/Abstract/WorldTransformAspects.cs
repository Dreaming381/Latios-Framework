using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Mathematics;

#if LATIOS_TRANSFORMS_UNITY
using TransformComponent = Unity.Transforms.LocalToWorld;
using Unity.Transforms;
#else
using TransformComponent = Latios.Transforms.WorldTransform;
#endif

namespace Latios.Transforms.Abstract
{
    [IJobEach.ParameterHandle(typeof(WorldTransformReadOnlyAspectParameterHandle), IJobEach.ScheduleModeMask.All)]
    public struct WorldTransformReadOnlyAspect : IAspect, IJobEach.IParameter
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

        /// <summary>
        /// Transforms a point from local space to world space, including scale and stretch.
        /// </summary>
        public float3 TransformPoint(float3 localPoint)
        {
#if LATIOS_TRANSFORMS_UNITY
            return math.transform(worldTransform.ValueRO.Value, localPoint);
#else
            return qvvs.TransformPoint(in worldTransform.ValueRO.worldTransform, localPoint);
#endif
        }

        /// <summary>
        /// Transforms a point from world space to local space, including scale and stretch.
        /// </summary>
        public float3 InverseTransformPoint(float3 worldPoint)
        {
#if LATIOS_TRANSFORMS_UNITY
            ref readonly float4x4 ltw = ref worldTransform.ValueRO.Value;
            return math.mul(math.inverse(new float3x3(ltw)), worldPoint - ltw.c3.xyz);
#else
            return qvvs.InverseTransformPoint(in worldTransform.ValueRO.worldTransform, worldPoint);
#endif
        }

        /// <summary>
        /// Rotates a direction from local space to world space, ignoring scale and stretch.
        /// </summary>
        public float3 TransformDirection(float3 localDirection) => math.rotate(rotation, localDirection);

        /// <summary>
        /// Rotates a direction from world space to local space, ignoring scale and stretch.
        /// </summary>
        public float3 InverseTransformDirection(float3 worldDirection) => math.rotate(math.conjugate(rotation), worldDirection);

        /// <summary>
        /// Transforms a surface normal from local space to world space, so that it stays perpendicular to the surface under
        /// non-uniform scale or stretch. The result is not normalized.
        /// </summary>
        public float3 TransformNormalUnnormalized(float3 localNormal)
        {
#if LATIOS_TRANSFORMS_UNITY
            return math.mul(math.transpose(math.inverse(new float3x3(worldTransform.ValueRO.Value))), localNormal);
#else
            ref readonly var transform = ref worldTransform.ValueRO.worldTransform;
            return qvvs.TransformNormalUnnormalized(in transform, localNormal);
#endif
        }

        /// <summary>
        /// Transforms a surface normal from world space to local space, so that it stays perpendicular to the surface under
        /// non-uniform scale or stretch. The result is not normalized.
        /// </summary>
        public float3 InverseTransformNormalUnnormalized(float3 worldNormal)
        {
#if LATIOS_TRANSFORMS_UNITY
            return math.mul(math.transpose(new float3x3(worldTransform.ValueRO.Value)), worldNormal);
#else
            ref readonly var transform = ref worldTransform.ValueRO.worldTransform;
            return qvvs.InverseTransformNormalUnnormalized(in transform, worldNormal);
#endif
        }

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
        public struct Lookup : ILatiosApiGettable
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

            void ILatiosApiGettable.CreateForApi(ref SystemState state) => this = new Lookup(ref state);

            void ILatiosApiGettable.UpdateForApi(ref SystemState state) => Update(ref state);
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
        public struct TypeHandle : ILatiosApiGettable
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
            public void Update(ref SystemState state) => transformHandle.Update(ref state);

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

            public bool DidChange(in ArchetypeChunk chunk, uint version) => chunk.DidChange(ref transformHandle, version);

            public bool Has(in ArchetypeChunk chunk) => chunk.Has(ref transformHandle);

#if LATIOS_TRANSFORMS_UNITY
            public bool isNativeQvvs => false;
#else
            public bool isNativeQvvs => true;
#endif

            void ILatiosApiGettable.CreateForApi(ref SystemState state) => this = new TypeHandle(ref state);

            void ILatiosApiGettable.UpdateForApi(ref SystemState state) => Update(ref state);
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

    /// <summary>
    /// The backing handle for a WorldTransformReadOnlyAspect Execute() parameter.
    /// </summary>
    public struct WorldTransformReadOnlyAspectParameterHandle : IJobEach.IParameterHandle<WorldTransformReadOnlyAspect>
    {
        [Unity.Collections.ReadOnly] ComponentTypeHandle<TransformComponent> m_transformHandle;
        // Job structs can't hold an unassigned NativeArray, so the chunk's array lives behind a pointer.
        Latios.Unsafe.ThreadCache<Unity.Collections.NativeArray<TransformComponent> > m_chunkTransforms;

        /// <inheritdoc />
        public FluentQuery AppendToQuery(FluentQuery query) => query.With<TransformComponent>(true);

        /// <inheritdoc />
        public bool OnChunkBegin(in IJobEach.JobContext context)
        {
            if (!m_chunkTransforms.isCreated)
                m_chunkTransforms   = new Latios.Unsafe.ThreadCache<Unity.Collections.NativeArray<TransformComponent> >(default);
            m_chunkTransforms.cache = context.chunk.GetNativeArray(ref m_transformHandle);
            return true;
        }

        /// <inheritdoc />
        public void OnChunkEnd(in IJobEach.JobContext context, bool chunkWasExecuted)
        {
        }

        /// <inheritdoc />
        public WorldTransformReadOnlyAspect GetParameter(in IJobEach.JobContext context)
        {
            return new WorldTransformReadOnlyAspect(new RefRO<TransformComponent>(m_chunkTransforms.cache, context.indexInChunk));
        }

        void ILatiosApiGettable.CreateForApi(ref SystemState state) => m_transformHandle = state.GetComponentTypeHandle<TransformComponent>(true);

        void ILatiosApiGettable.UpdateForApi(ref SystemState state) => m_transformHandle.Update(ref state);
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

