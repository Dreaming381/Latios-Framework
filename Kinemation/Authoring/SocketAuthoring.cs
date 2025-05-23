using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.Kinemation.Authoring
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Latios/Kinemation/Socket (Kinemation)")]
    public class SocketAuthoring : MonoBehaviour
    {
        public short  boneIndex;
        public bool   useReversePath;
        public string reversePathStartsWith;
    }

    public class SocketAuthoringBaker : Baker<SocketAuthoring>
    {
        public override void Bake(SocketAuthoring authoring)
        {
            var parent = GetParent();
            if (parent == null)
                return;
            var skeleton = GetComponent<Animator>(parent);
            if (skeleton == null)
                return;
            if (skeleton.hasTransformHierarchy)
            {
                UnityEngine.Debug.LogError(
                    $"The socket {authoring.gameObject.name} for parent {parent.name} uses an exposed skeleton. Sockets are only supported on optimized skeletons.");
                return;
            }

            var entity                                                  = GetEntity(TransformUsageFlags.Renderable);
            var index                                                   = authoring.useReversePath ? 0 : math.max(0, authoring.boneIndex);
            AddComponent(                entity, new Socket { boneIndex = (short)index });
            AddComponent<AuthoredSocket>(entity);
            AddComponent(                entity, new BoneOwningSkeletonReference { skeletonRoot = GetEntity(skeleton, TransformUsageFlags.Renderable) });

            if (authoring.useReversePath)
            {
                var handle                                                                 = this.RequestBoneNames(skeleton, GetName(skeleton));
                AddComponent(handle.boneNamesTempEntity, new AuthoredSocketString { socket = entity, reversePathStart = authoring.reversePathStartsWith });
            }
        }
    }
}

