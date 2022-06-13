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
    public class SkeletonHierarchySmartBlobberSystem : SmartBlobberConversionSystem<OptimizedSkeletonHierarchyBlob, SkeletonHierarchyBakeData, SkeletonHierarchyConverter>
    {
        struct AuthoringHandlePair
        {
            public SkeletonConversionContext                          authoring;
            public SmartBlobberHandle<OptimizedSkeletonHierarchyBlob> handle;
        }

        List<AuthoringHandlePair> m_contextList = new List<AuthoringHandlePair>();

        protected override void GatherInputs()
        {
            m_contextList.Clear();
            Entities.ForEach((SkeletonConversionContext context) =>
            {
                if (context.isOptimized)
                {
                    var handle = AddToConvert(context.animator.gameObject, new SkeletonHierarchyBakeData { bones = context.skeleton });
                    m_contextList.Add(new AuthoringHandlePair { authoring                                        = context, handle = handle });
                }
            });
        }

        protected override void FinalizeOutputs()
        {
            foreach (var pair in m_contextList)
            {
                var context                                                                                  = pair.authoring;
                var go                                                                                       = context.animator.gameObject;
                var entity                                                                                   = GetPrimaryEntity(go);
                DstEntityManager.AddComponentData(entity, new OptimizedSkeletonHierarchyBlobReference { blob = pair.handle.Resolve() });
            }
        }

        protected override bool Filter(in SkeletonHierarchyBakeData input, UnityEngine.GameObject gameObject, out SkeletonHierarchyConverter converter)
        {
            converter.parents                = new UnsafeList<int>(input.bones.Length, World.UpdateAllocator.ToAllocator);
            converter.hasParentScaleInverses = new UnsafeList<bool>(input.bones.Length, World.UpdateAllocator.ToAllocator);
            converter.parents.Resize(input.bones.Length);
            converter.hasParentScaleInverses.Resize(input.bones.Length);
            for (int i = 0; i < input.bones.Length; i++)
            {
                converter.parents[i]                = input.bones[i].parentIndex;
                converter.hasParentScaleInverses[i] = input.bones[i].ignoreParentScale;
            }
            return true;
        }
    }

    public struct SkeletonHierarchyBakeData
    {
        public BoneTransformData[] bones;
    }

    public struct SkeletonHierarchyConverter : ISmartBlobberSimpleBuilder<OptimizedSkeletonHierarchyBlob>
    {
        public UnsafeList<int>  parents;
        public UnsafeList<bool> hasParentScaleInverses;

        public BlobAssetReference<OptimizedSkeletonHierarchyBlob> BuildBlob()
        {
            var builder = new BlobBuilder(Allocator.Temp);

            ref var root    = ref builder.ConstructRoot<OptimizedSkeletonHierarchyBlob>();
            var     indices = builder.Allocate(ref root.parentIndices, parents.Length);
            var     hasPSI  = builder.Allocate(ref root.hasParentScaleInverseBitmask, (int)math.ceil(parents.Length / 64f));  // length is max 16 bits so this division is safe in float.
            for (int i = 0; i < hasPSI.Length; i++)
                hasPSI[i] = new BitField64(0UL);
            for (int i = 0; i < parents.Length; i++)
            {
                indices[i] = (short)parents[i];

                if (hasParentScaleInverses[i])
                {
                    int index = i / 64;
                    var field = hasPSI[index];
                    field.SetBits(i % 64, true);
                    hasPSI[index] = field;
                }
            }
            return builder.CreateBlobAssetReference<OptimizedSkeletonHierarchyBlob>(Allocator.Persistent);
        }
    }
}

