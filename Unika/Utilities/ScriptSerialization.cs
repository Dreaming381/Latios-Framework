using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.LowLevel.Unsafe;
using Unity.Entities.Serialization;
using Unity.Mathematics;

namespace Latios.Unika
{
    public static class ScriptSerialization
    {
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

