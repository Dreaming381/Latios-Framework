using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.LowLevel.Unsafe;
using Unity.Entities.Serialization;
using Unity.Mathematics;

namespace Latios.Unika
{
    /// <summary>
    /// A static class which contains a collection of utility methods for serializing and deserializing scripts.
    /// Since scripts store their data in untyped DynamicBuffer arrays intermingled, Unity does not know how to naturally serialize
    /// and deserialize them. If you do not do any custom serialization (save systems or networking), then you only need to worry about
    /// entity serialization, and only if you instantiate non-prefabs. Otherwise, Unika automatically handles this for baking and subscene loading.
    /// </summary>
    public static class ScriptSerialization
    {
        /// <summary>
        /// Gets a unique hash for the sequence of scripts stored on the entity, to ensure serialized data can be deserialized correctly
        /// </summary>
        /// <param name="scripts">The scripts buffer to compute the hash for. The field values of the script do not matter,
        /// only the script types and order matters, and is evaluated using a method that is safe across sessions</param>
        /// <returns>A 128-bit hashcode of the script sequence</returns>
        public static uint4 ComputeStructureHash(NativeArray<UnikaScripts> scripts)
        {
            if (scripts.Length == 0)
                return default;

            var                    scriptCount = scripts[0].header.instanceCount;
            xxHash3.StreamingState state       = new xxHash3.StreamingState(false, 0);
            state.Update(scriptCount);
            for (int i = 0; i < scriptCount; i++)
            {
                var script = scripts[i + 1];
                var hash   = ScriptTypeInfoManager.GetStableHash((short)script.header.scriptType);
                state.Update(hash);
            }
            return state.DigestHash128();
        }

        /// <summary>
        /// Serialize entities in all of the scripts for saving, moving between worlds, or instantiating,
        /// and setup the serialization controller for automatic deserialization. Call this if you need to perform serialization in the middle of a system
        /// making structural changes, such as within the LatiosWorldSyncGroup.
        /// </summary>
        /// <param name="entityWithScripts">The current entity containing the scripts. This entity will not be deserialized.</param>
        /// <param name="entityManager">The EntityManager the entity belongs to</param>
        public static void SerializeEntities(Entity entityWithScripts, EntityManager entityManager)
        {
            entityManager.SetComponentData(entityWithScripts, new UnikaEntitySerializationController
            {
                originalIndex   = entityWithScripts.Index,
                originalVersion = entityWithScripts.Version
            });
            entityManager.SetComponentEnabled<UnikaEntitySerializationController>(entityWithScripts, true);
            var scripts    = entityManager.GetBuffer<UnikaScripts>(entityWithScripts, true);
            var entityRefs = entityManager.GetBuffer<UnikaSerializedEntityReference>(entityWithScripts, false);
            SerializeEntities(scripts.AsNativeArray(), ref entityRefs);
        }

        /// <summary>
        /// Serialize entities in all of the scripts for saving, moving between worlds, or instantiating
        /// </summary>
        /// <param name="scripts">The scripts to serialize</param>
        /// <param name="entityRefs">The buffer to store the serialized entities which can withstand remapping</param>
        public static unsafe void SerializeEntities(in NativeArray<UnikaScripts> scripts, ref DynamicBuffer<UnikaSerializedEntityReference> entityRefs)
        {
            entityRefs.Clear();
            if (scripts.Length == 0)
                return;

            foreach (var script in scripts.AllScripts(default))
            {
                var scriptByteOffset = script.m_byteOffset;
                var ptr              = script.GetUnsafeROPtrAsBytePtr();
                var entityOffsets    = ScriptTypeInfoManager.GetEntityRemap((short)script.m_headerRO.scriptType);

                foreach (var entityOffset in entityOffsets)
                {
                    entityRefs.Add(new UnikaSerializedEntityReference
                    {
                        byteOffset = scriptByteOffset + entityOffset.Offset,
                        entity     = UnsafeUtility.AsRef<Entity>(ptr + entityOffset.Offset)
                    });
                }
            }
        }

        /// <summary>
        /// Deserialize entities in all of the scripts after loading, moving between worlds, or instantiating,
        /// and clear the serialization controller state. Call this if you need to perform deserialization in the middle of a system
        /// making structural changes, such as within the LatiosWorldSyncGroup.
        /// </summary>
        /// <param name="entityWithScripts">The current entity containing the scripts. This entity will not be deserialized.</param>
        /// <param name="entityManager">The EntityManager the entity belongs to</param>
        public static void DeserializeEntities(Entity entityWithScripts, EntityManager entityManager)
        {
            entityManager.SetComponentData(entityWithScripts, new UnikaEntitySerializationController
            {
                originalIndex   = entityWithScripts.Index,
                originalVersion = entityWithScripts.Version
            });
            entityManager.SetComponentEnabled<UnikaEntitySerializationController>(entityWithScripts, false);
            var scripts    = entityManager.GetBuffer<UnikaScripts>(entityWithScripts, false).AsNativeArray();
            var entityRefs = entityManager.GetBuffer<UnikaSerializedEntityReference>(entityWithScripts, true);
            DeserializeEntities(ref scripts, entityRefs.AsNativeArray());
        }

        /// <summary>
        /// Deserialize entities for all the scripts upon loading, after moving between worlds, or after instantiating
        /// </summary>
        /// <param name="scripts">The scripts to deserialize, which must maintain the same script type sequence as when it was serialized</param>
        /// <param name="entityRefs">The buffer that contains the serialized entities</param>
        public static unsafe void DeserializeEntities(ref NativeArray<UnikaScripts> scripts, in NativeArray<UnikaSerializedEntityReference> entityRefs)
        {
            if (scripts.Length == 0)
                return;

            var ptr = (byte*)scripts.GetUnsafePtr();
            foreach (var entity in entityRefs)
            {
                UnsafeUtility.AsRef<Entity>(ptr + entity.byteOffset) = entity.entity;
            }
        }

        /// <summary>
        /// Serialize blob asset references in all the scripts for saving
        /// </summary>
        /// <param name="scripts">The scripts to serialize</param>
        /// <param name="blobRefs">The buffer to store the serialized blob asset references</param>
        public static unsafe void SerializeBlobs(in NativeArray<UnikaScripts> scripts, ref DynamicBuffer<UnikaSerializedBlobReference> blobRefs)
        {
            blobRefs.Clear();
            if (scripts.Length == 0)
                return;

            foreach (var script in scripts.AllScripts(default))
            {
                var scriptByteOffset = script.m_byteOffset;
                var ptr              = script.GetUnsafeROPtrAsBytePtr();
                var blobOffsets      = ScriptTypeInfoManager.GetBlobRemap((short)script.m_headerRO.scriptType);

                foreach (var blobOffset in blobOffsets)
                {
                    blobRefs.Add(new UnikaSerializedBlobReference
                    {
                        byteOffset = scriptByteOffset + blobOffset.Offset,
                        blob       = UnsafeUtility.AsRef<UnsafeUntypedBlobAssetReference>(ptr + blobOffset.Offset)
                    });
                }
            }
        }

        /// <summary>
        /// Deserialize blob asset references in all the scripts after loading
        /// </summary>
        /// <param name="scripts">The scripts to deserialize, which must maintain the same script type sequence as when it was serialized</param>
        /// <param name="blobRefs">The buffer that contains the serialized blob asset references</param>
        public static unsafe void DeserializeBlobs(ref NativeArray<UnikaScripts> scripts, in NativeArray<UnikaSerializedBlobReference> blobRefs)
        {
            if (scripts.Length == 0)
                return;

            var ptr = (byte*)scripts.GetUnsafePtr();
            foreach (var blob in blobRefs)
            {
                UnsafeUtility.AsRef<UnsafeUntypedBlobAssetReference>(ptr + blob.byteOffset) = blob.blob;
            }
        }

        /// <summary>
        /// Serialize asset references in all the scripts for saving
        /// </summary>
        /// <param name="scripts">The scripts to serialize</param>
        /// <param name="assetRefs">The buffer to store the serialized asset references</param>
        public static unsafe void SerializeAssets(in NativeArray<UnikaScripts> scripts, ref DynamicBuffer<UnikaSerializedAssetReference> assetRefs)
        {
            assetRefs.Clear();
            if (scripts.Length == 0)
                return;

            foreach (var script in scripts.AllScripts(default))
            {
                var scriptByteOffset = script.m_byteOffset;
                var ptr              = script.GetUnsafeROPtrAsBytePtr();
                var assetOffsets     = ScriptTypeInfoManager.GetAssetRemap((short)script.m_headerRO.scriptType);

                foreach (var assetOffset in assetOffsets)
                {
                    assetRefs.Add(new UnikaSerializedAssetReference
                    {
                        byteOffset = scriptByteOffset + assetOffset.Offset,
                        asset      = UnsafeUtility.AsRef<UntypedWeakReferenceId>(ptr + assetOffset.Offset)
                    });
                }
            }
        }

        /// <summary>
        /// Deserialize asset references in all the scripts after loading
        /// </summary>
        /// <param name="scripts">The scripts to deserialize, which must maintain the same script type sequence as when it was serialized</param>
        /// <param name="assetRefs">The buffer that contains the serialized asset references</param>
        public static unsafe void DeserializeAssets(ref NativeArray<UnikaScripts> scripts, in NativeArray<UnikaSerializedAssetReference> assetRefs)
        {
            if (scripts.Length == 0)
                return;

            var ptr = (byte*)scripts.GetUnsafePtr();
            foreach (var asset in assetRefs)
            {
                UnsafeUtility.AsRef<UntypedWeakReferenceId>(ptr + asset.byteOffset) = asset.asset;
            }
        }

        /// <summary>
        /// Serialize UnityEngine.Object references in all the scripts for saving
        /// </summary>
        /// <param name="scripts">The scripts to serialize</param>
        /// <param name="objRefs">The buffer to store the serialized UnityEngine.Object references</param>
        public static unsafe void SerializeObjects(in NativeArray<UnikaScripts> scripts, ref DynamicBuffer<UnikaSerializedObjectReference> objRefs)
        {
            objRefs.Clear();
            if (scripts.Length == 0)
                return;

            foreach (var script in scripts.AllScripts(default))
            {
                var scriptByteOffset = script.m_byteOffset;
                var ptr              = script.GetUnsafeROPtrAsBytePtr();
                var objOffsets       = ScriptTypeInfoManager.GetObjectRemap((short)script.m_headerRO.scriptType);

                foreach (var objOffset in objOffsets)
                {
                    objRefs.Add(new UnikaSerializedObjectReference
                    {
                        byteOffset = scriptByteOffset + objOffset.Offset,
                        obj        = UnsafeUtility.AsRef<UnityObjectRef<UnityEngine.Object> >(ptr + objOffset.Offset)
                    });
                }
            }
        }

        /// <summary>
        /// Deserialize UnityEngine.Object references in all the scripts after loading
        /// </summary>
        /// <param name="scripts">The scripts to deserialize, which must maintain the same script type sequence as when it was serialized</param>
        /// <param name="objRefs">The buffer that contains the serialized UnityEngine.Object references</param>
        public static unsafe void DeserializeObjects(ref NativeArray<UnikaScripts> scripts, in NativeArray<UnikaSerializedObjectReference> objRefs)
        {
            if (scripts.Length == 0)
                return;

            var ptr = (byte*)scripts.GetUnsafePtr();
            foreach (var obj in objRefs)
            {
                UnsafeUtility.AsRef<UnityObjectRef<UnityEngine.Object> >(ptr + obj.byteOffset) = obj.obj;
            }
        }

        /// <summary>
        /// Deserialize Unika script types that were created in a different session, and thus had different runtime ID values associated with each script type
        /// </summary>
        /// <param name="scripts">The scripts to deserialize</param>
        /// <param name="serializedTypeIds">The stable hash of each runtime ID from the previous session the scripts were saved from</param>
        /// <exception cref="System.InvalidOperationException"></exception>
        public static unsafe void DeserializeScriptTypes(ref NativeArray<UnikaScripts> scripts, in UnikaSerializedTypeIds serializedTypeIds)
        {
            ref var hashes = ref serializedTypeIds.blob.Value.stableHashBySerializedTypeId;

            if (scripts.Length == 0)
                return;

            ulong combinedMask = 0;
            var   scriptCount  = scripts[0].header.instanceCount;
            for (int i = 0; i < scriptCount; i++)
            {
                var header = scripts[i + 1];
                var hash   = hashes[header.header.scriptType];
                if (!ScriptTypeInfoManager.TryGetRuntimeIdAndMask(hash, out var idAndMask))
                    throw new System.InvalidOperationException(
                        "Unika serialized stable hash was not found in this runtime. Did you fail to load an assembly? Or perhaps the runtime representation does not match the baked representation?");

                combinedMask             |= idAndMask.bloomMask;
                header.header.bloomMask   = idAndMask.bloomMask;
                header.header.scriptType  = idAndMask.runtimeId;
                scripts[i + 1]            = header;
            }

            var master              = scripts[0];
            master.header.bloomMask = combinedMask;
            scripts[0]              = master;
        }
    }
}

