using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Core;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.Profiling;

using UnityCollider = UnityEngine.Collider;

namespace Latios.Psyshock.Authoring.Systems
{
    [ConverterVersion("latios", 2)]
    public class ColliderConversionSystem : GameObjectConversionSystem
    {
        protected override void OnUpdate()
        {
            ConvertCompoundColliders();
        }

        #region CompoundColliders
        private struct CompoundColliderComputationData
        {
            public Hash128                                  hash;
            public int                                      index;
            public BlobAssetReference<CompoundColliderBlob> blob;
        }

        private struct ColliderTransformHashPair
        {
            public ulong colliderHash;
            public ulong transformHash;
        }

        private List<UnityCollider>     m_unityColliders = new List<UnityCollider>();
        private List<ColliderAuthoring> m_authorings     = new List<ColliderAuthoring>();

        private NativeList<Collider>       m_nativeColliders;
        private NativeList<RigidTransform> m_nativeTransforms;
        private NativeList<int2>           m_compoundRanges;

        private void ConvertCompoundColliders()
        {
            Profiler.BeginSample("ConvertCompoundColliders");

            m_nativeColliders  = new NativeList<Collider>(128, Allocator.TempJob);
            m_nativeTransforms = new NativeList<RigidTransform>(128, Allocator.TempJob);
            m_compoundRanges   = new NativeList<int2>(128, Allocator.TempJob);

            //Step 1: Find all colliders and construct parallel arrays
            m_authorings.Clear();
            Entities.WithNone<DontConvertColliderTag>().ForEach((ColliderAuthoring colliderAuthoring) =>
            {
                //Do stuff
                if (colliderAuthoring.colliderType == AuthoringColliderTypes.None)
                    return;
                if (colliderAuthoring.generateFromChildren)
                {
                    m_unityColliders.Clear();
                    colliderAuthoring.GetComponentsInChildren(m_unityColliders);
                    try
                    {
                        CreateChildrenColliders(colliderAuthoring, m_unityColliders, m_nativeColliders, m_nativeTransforms, m_compoundRanges);
                    }
                    catch (Exception e)
                    {
                        DisposeAndThrow(e);
                    }
                }
                else
                {
                    try
                    {
                        CreateChildrenColliders(colliderAuthoring, colliderAuthoring.colliders, m_nativeColliders, m_nativeTransforms, m_compoundRanges);
                    }
                    catch (Exception e)
                    {
                        DisposeAndThrow(e);
                    }
                }
                m_authorings.Add(colliderAuthoring);
            });

            if (m_authorings.Count > 0)
            {
                using (var computationContext = new BlobAssetComputationContext<CompoundColliderComputationData, CompoundColliderBlob>(BlobAssetStore, 128, Allocator.Temp))
                {
                    //Step 2: Compute hashes
                    var hashes = new NativeArray<ColliderTransformHashPair>(m_compoundRanges.Length, Allocator.TempJob);
                    new ComputeCompoundHashesJob
                    {
                        colliders  = m_nativeColliders,
                        transforms = m_nativeTransforms,
                        ranges     = m_compoundRanges,
                        hashes     = hashes
                    }.ScheduleParallel(m_compoundRanges.Length, 1, default).Complete();

                    //Step 3: Check hashes against computationContext to see if blobs need to be built
                    for (int i = 0; i < m_authorings.Count; i++)
                    {
                        var hash = hashes.ReinterpretLoad<Hash128>(i);
                        computationContext.AssociateBlobAssetWithUnityObject(hash, m_authorings[i].gameObject);
                        if (computationContext.NeedToComputeBlobAsset(hash))
                        {
                            computationContext.AddBlobAssetToCompute(hash, new CompoundColliderComputationData
                            {
                                hash  = hash,
                                index = i
                            });
                        }
                    }

                    //Step 4: Dispatch builder job
                    using (var computationData = computationContext.GetSettings(Allocator.TempJob))
                    {
                        new ComputeCompoundBlobs
                        {
                            colliders       = m_nativeColliders,
                            transforms      = m_nativeTransforms,
                            ranges          = m_compoundRanges,
                            computationData = computationData
                        }.ScheduleParallel(computationData.Length, 1, default).Complete();

                        foreach (var data in computationData)
                        {
                            computationContext.AddComputedBlobAsset(data.hash, data.blob);
                        }
                    }

                    //Step 5: Build Collider component
                    var index = 0;
                    Entities.ForEach((ColliderAuthoring colliderAuthoring) =>
                    {
                        computationContext.GetBlobAsset(hashes.ReinterpretLoad<Hash128>(index++), out var blob);

                        var targetEntity = GetPrimaryEntity(colliderAuthoring);

                        DeclareDependency(colliderAuthoring, colliderAuthoring.transform);
                        float3 scale = colliderAuthoring.transform.lossyScale;
                        if (scale.x != scale.y || scale.x != scale.z)
                        {
                            throw new InvalidOperationException(
                                $"GameObject Conversion Error: Failed to convert {colliderAuthoring}. Only uniform scale is permitted on Compound colliders.");
                        }

                        Collider icdCompound = new CompoundCollider
                        {
                            compoundColliderBlob = blob,
                            scale                = scale.x
                        };
                        DstEntityManager.AddComponentData(targetEntity, icdCompound);
                    });

                    hashes.Dispose();
                }
            }

            m_nativeColliders.Dispose();
            m_nativeTransforms.Dispose();
            m_compoundRanges.Dispose();

            Profiler.EndSample();
        }

        private void CreateChildrenColliders(ColliderAuthoring root,
                                             List<UnityCollider>        unityColliders,
                                             NativeList<Collider>       colliders,
                                             NativeList<RigidTransform> transforms,
                                             NativeList<int2>           ranges)
        {
            int2 newRange = new int2(colliders.Length, 0);
            foreach (var unityCollider in unityColliders)
            {
                DeclareDependency(root, unityCollider);
                DeclareDependency(root, unityCollider.transform);
                if ((unityCollider is UnityEngine.SphereCollider || unityCollider is UnityEngine.CapsuleCollider || unityCollider is UnityEngine.BoxCollider) == false)
                {
                    throw new InvalidOperationException(
                        $"GameObject Conversion Error: Failed to convert {unityCollider}. Compound Colliders may only be composed of Sphere, Capsule, and Box Colliders. Other collider types may be supported in a future version.");
                }
                if (!unityCollider.transform.IsChildOf(root.transform))
                {
                    throw new InvalidOperationException($"GameObject Conversion Error: Failed to convert {root}. Compound Colliders may only reference children colliders.");
                }

                Entity entity = TryGetPrimaryEntity(unityCollider);
                if (entity == Entity.Null)
                {
                    //The child GameObject must be a child of the StopConversion. Skip
                    continue;
                }

                //Calculate transform

                float3 scale = (float3)unityCollider.transform.lossyScale / root.transform.lossyScale;
                if ((unityCollider is UnityEngine.SphereCollider || unityCollider is UnityEngine.CapsuleCollider) &&
                    math.cmax(scale) - math.cmin(scale) > 1.0E-5f)
                {
                    throw new InvalidOperationException(
                        $"GameObject Conversion Error: Failed to convert {unityCollider}. Only uniform scale is permitted on Sphere and Capsule colliders.");
                }

                float4x4                      localToRoot = root.transform.localToWorldMatrix.inverse * unityCollider.transform.localToWorldMatrix;
                Unity.Transforms.LocalToWorld ltr         = new Unity.Transforms.LocalToWorld { Value = localToRoot };
                var                           rotation    = quaternion.LookRotationSafe(ltr.Forward, ltr.Up);
                var                           position    = ltr.Position;
                transforms.Add(new RigidTransform(rotation, position));

                //Calculate collider
                if (unityCollider is UnityEngine.SphereCollider unitySphere)
                {
                    colliders.Add(new SphereCollider
                    {
                        center = unitySphere.center,
                        radius = unitySphere.radius * scale.x
                    });
                }
                else if (unityCollider is UnityEngine.CapsuleCollider unityCapsule)
                {
                    float3 dir;
                    if (unityCapsule.direction == 0)
                    {
                        dir = new float3(1f, 0f, 0f);
                    }
                    else if (unityCapsule.direction == 1)
                    {
                        dir = new float3(0f, 1f, 0f);
                    }
                    else
                    {
                        dir = new float3(0f, 0f, 1f);
                    }
                    colliders.Add(new CapsuleCollider
                    {
                        pointB = (float3)unityCapsule.center + ((unityCapsule.height / 2f - unityCapsule.radius) * unityCapsule.transform.lossyScale.x * dir),
                        pointA = (float3)unityCapsule.center - ((unityCapsule.height / 2f - unityCapsule.radius) * unityCapsule.transform.lossyScale.x * dir),
                        radius = unityCapsule.radius * scale.x
                    });
                }
                else if (unityCollider is UnityEngine.BoxCollider unityBox)
                {
                    colliders.Add(new BoxCollider
                    {
                        center   = unityBox.center,
                        halfSize = unityBox.size * scale / 2f
                    });
                }
                newRange.y++;
            }
            ranges.Add(newRange);
        }

        private void DisposeAndThrow(Exception e)
        {
            m_nativeColliders.Dispose();
            m_nativeTransforms.Dispose();
            m_compoundRanges.Dispose();

            throw e;
        }

        [BurstCompile]
        private unsafe struct ComputeCompoundHashesJob : IJobFor
        {
            [ReadOnly] public NativeArray<Collider>       colliders;
            [ReadOnly] public NativeArray<RigidTransform> transforms;
            [ReadOnly] public NativeArray<int2>           ranges;
            public NativeArray<ColliderTransformHashPair> hashes;

            public void Execute(int i)
            {
                int2  range        = ranges[i];
                byte* colliderPtr  = (byte*)colliders.GetSubArray(range.x, range.y).GetUnsafeReadOnlyPtr();
                ulong colliderHash = XXHash.Hash64(colliderPtr, UnsafeUtility.SizeOf<Collider>() * range.y);

                byte* transformPtr  = (byte*)transforms.GetSubArray(range.x, range.y).GetUnsafeReadOnlyPtr();
                ulong transformHash = XXHash.Hash64(transformPtr, sizeof(RigidTransform) * range.y);

                hashes[i] = new ColliderTransformHashPair { colliderHash = colliderHash, transformHash = transformHash };
            }
        }

        [BurstCompile]
        private struct ComputeCompoundBlobs : IJobFor
        {
            [ReadOnly] public NativeArray<Collider>             colliders;
            [ReadOnly] public NativeArray<RigidTransform>       transforms;
            [ReadOnly] public NativeArray<int2>                 ranges;
            public NativeArray<CompoundColliderComputationData> computationData;

            public void Execute(int i)
            {
                var data  = computationData[i];
                var range = ranges[data.index];

                var     builder        = new BlobBuilder(Allocator.Temp);
                ref var root           = ref builder.ConstructRoot<CompoundColliderBlob>();
                var     blobColliders  = builder.Allocate(ref root.colliders, range.y);
                var     blobTransforms = builder.Allocate(ref root.transforms, range.y);

                Aabb aabb = new Aabb(float.MaxValue, float.MinValue);
                for (int j = 0; j < range.y; j++)
                {
                    var c             = colliders[j + range.x];
                    var t             = transforms[j + range.x];
                    blobColliders[j]  = c;
                    blobTransforms[j] = t;
                    var newbox        = Physics.AabbFrom(c, t);
                    aabb.min          = math.min(aabb.min, newbox.min);
                    aabb.max          = math.max(aabb.max, newbox.max);
                }

                root.localAabb = aabb;
                data.blob      = builder.CreateBlobAssetReference<CompoundColliderBlob>(Allocator.Persistent);
                builder.Dispose();
                computationData[i] = data;
            }
        }
        #endregion
    }
}

