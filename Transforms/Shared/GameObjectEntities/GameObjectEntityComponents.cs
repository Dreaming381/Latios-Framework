using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Latios.Transforms
{
    #region Components
    // Todo: Change GameObjectEntity to an ICleanupComponentData. Avoiding now due to risk and infrequent use.

    /// <summary>
    /// Contains a reference to the GameObject's Transform, which is a general-purpose access
    /// to the GameObject.
    /// Usage: ReadOnly
    /// </summary>
    public partial struct GameObjectEntity : IManagedStructComponent
    {
        public UnityEngine.Transform gameObjectTransform;

        public void Dispose()
        {
            if (gameObjectTransform != null)
            {
                var mono = gameObjectTransform.GetComponent<GameObjectEntityAuthoring>();
                if (mono != null)
                    mono.entityManager = default;
                UnityEngine.Object.Destroy(gameObjectTransform.gameObject);
            }
        }
    }

    /// <summary>
    /// When this component is present, the GameObject's world transform is copied to the entity's world transform.
    ///
    /// QVVS Transforms: When useUniformScale is set, the GameObject's world-space scale is applied to the uniform scale
    /// of the entity. Otherwise, the GameObject's local-space non-uniform scale is applied to the stretch of the the entity.
    ///
    /// Unity Transforms: When useUniformScale is set, the GameObject's position, rotation, and uniform scale are extracted and converted
    /// into a new LocalToWorld matrix. Otherwise, the GameObject's localToWorldMatrix is copied directly to the LocalToWorld of the entity.
    ///
    /// Usage: Add/Remove and modify as needed
    /// </summary>
    public struct CopyTransformToEntity : IComponentData
    {
        public bool useUniformScale;
    }

    /// <summary>
    /// When this component is present, the entity's world transform is copied to the GameObject's world transform.
    ///
    /// QVVS Transforms: The entity's world-space position and rotation are copied, and scale is multiplied by stretch and
    /// set as the GameObject's local-space scale.
    ///
    /// Unity Transforms: The entity's world-space position and rotation are copied, and scale is set to identity.
    ///
    /// Usage: Add/Remove as needed
    /// </summary>
    public struct CopyTransformFromEntityTag : IComponentData { }
    #endregion

    #region Interfaces
    public interface IInitializeGameObjectEntity
    {
        public void Initialize(LatiosWorld latiosWorld, Entity gameObjectEntity);
    }
    #endregion

    #region Internal
    internal struct GameObjectEntityBindClient : IComponentData
    {
        public Unity.Entities.Hash128 guid;
    }

    internal struct GameObjectEntityHost : IComponentData
    {
        public Unity.Entities.Hash128 guid;
    }

    internal struct CopyTransformToEntityCleanupTag : ICleanupComponentData { }

    internal struct CopyTransformFromEntityCleanupTag : ICleanupComponentData { }

    internal partial struct CopyTransformToEntityMapping : ICollectionComponent
    {
        public NativeHashMap<Entity, int>            entityToIndexMap;
        public NativeHashMap<int, Entity>            indexToEntityMap;
        public UnityEngine.Jobs.TransformAccessArray transformAccessArray;

        public JobHandle TryDispose(JobHandle handle)
        {
            if (!entityToIndexMap.IsCreated)
                return handle;
            handle.Complete();
            transformAccessArray.Dispose();
            return JobHandle.CombineDependencies(entityToIndexMap.Dispose(default), indexToEntityMap.Dispose(default));
        }
    }

    internal partial struct CopyTransformFromEntityMapping : ICollectionComponent
    {
        public NativeHashMap<Entity, int>            entityToIndexMap;
        public NativeHashMap<int, Entity>            indexToEntityMap;
        public UnityEngine.Jobs.TransformAccessArray transformAccessArray;

        public JobHandle TryDispose(JobHandle handle)
        {
            if (!entityToIndexMap.IsCreated)
                return handle;
            handle.Complete();
            transformAccessArray.Dispose();
            return JobHandle.CombineDependencies(entityToIndexMap.Dispose(default), indexToEntityMap.Dispose(default));
        }
    }

    internal struct RemoveDontDestroyOnSceneChangeTag : IComponentData { }
    #endregion
}

