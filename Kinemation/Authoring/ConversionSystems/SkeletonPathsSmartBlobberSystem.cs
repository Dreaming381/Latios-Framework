using System.Collections.Generic;
using Latios.Authoring;
using Latios.Authoring.Systems;
using Latios.Unsafe;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace Latios.Kinemation.Authoring.Systems
{
    [ConverterVersion("Latios", 1)]
    [DisableAutoCreation]
    public class SkeletonPathsSmartBlobberSystem : SmartBlobberConversionSystem<SkeletonBindingPathsBlob, SkeletonPathsBakeData, SkeletonPathsConverter>
    {
        struct AuthoringHandlePair
        {
            public SkeletonConversionContext                    authoring;
            public SmartBlobberHandle<SkeletonBindingPathsBlob> handle;
        }

        List<AuthoringHandlePair> m_contextList = new List<AuthoringHandlePair>();

        protected override void GatherInputs()
        {
            m_contextList.Clear();
            Entities.ForEach((SkeletonConversionContext context) =>
            {
                var handle = AddToConvert(context.animator.gameObject, new SkeletonPathsBakeData { bones = context.skeleton });
                m_contextList.Add(new AuthoringHandlePair { authoring                                    = context, handle = handle });
            });
        }

        protected override void FinalizeOutputs()
        {
            foreach (var pair in m_contextList)
            {
                var context                                                                            = pair.authoring;
                var go                                                                                 = context.animator.gameObject;
                var entity                                                                             = GetPrimaryEntity(go);
                DstEntityManager.AddComponentData(entity, new SkeletonBindingPathsBlobReference { blob = pair.handle.Resolve() });
            }
        }

        protected override unsafe bool Filter(in SkeletonPathsBakeData input, UnityEngine.GameObject gameObject, out SkeletonPathsConverter converter)
        {
            var allocator   = World.UpdateAllocator.ToAllocator;
            converter.paths = new UnsafeList<UnsafeList<byte> >(input.bones.Length, allocator);
            FixedString4096Bytes cache;
            for (int i = 0; i < input.bones.Length; i++)
            {
                cache    = input.bones[i].hierarchyReversePath;
                var path = new UnsafeList<byte>(cache.Length, allocator);
                path.AddRange(cache.GetUnsafePtr(), cache.Length);
                converter.paths.Add(path);
            }
            return true;
        }
    }

    public struct SkeletonPathsBakeData
    {
        public BoneTransformData[] bones;
    }

    public struct SkeletonPathsConverter : ISmartBlobberSimpleBuilder<SkeletonBindingPathsBlob>
    {
        public UnsafeList<UnsafeList<byte> > paths;

        public unsafe BlobAssetReference<SkeletonBindingPathsBlob> BuildBlob()
        {
            var builder = new BlobBuilder(Allocator.Temp);

            ref var root       = ref builder.ConstructRoot<SkeletonBindingPathsBlob>();
            var     pathsOuter = builder.Allocate(ref root.pathsInReversedNotation, paths.Length);
            for (int i = 0; i < paths.Length; i++)
            {
                builder.ConstructFromNativeArray(ref pathsOuter[i], paths[i].Ptr, paths[i].Length);
            }
            return builder.CreateBlobAssetReference<SkeletonBindingPathsBlob>(Allocator.Persistent);
        }
    }
}

