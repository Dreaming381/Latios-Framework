using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace Latios.Kinemation.Authoring.Systems
{
    [RequireMatchingQueriesForUpdate]
    [DisableAutoCreation]
    public partial class GatherMeshBindingPathsFromShadowHierarchySystem : SystemBase
    {
        List<UnityEngine.SkinnedMeshRenderer> m_smrCache;

        protected unsafe override void OnUpdate()
        {
            if (m_smrCache == null)
                m_smrCache = new List<UnityEngine.SkinnedMeshRenderer>();

            var textCache = new NativeText(Allocator.Temp);

            Entities.ForEach((ref DynamicBuffer<MeshPathByte> meshPathBytes, ref DynamicBuffer<MeshPathByteOffsetForPath> offsets,
                              in SkinnedMeshRenderererReferenceForMeshPaths smrRef, in ShadowHierarchyReference shadowRef) =>
            {
                m_smrCache.Clear();
                shadowRef.shadowHierarchyRoot.Value.GetComponentsInChildren(true,
                                                                            m_smrCache);
                UnityEngine.SkinnedMeshRenderer shadowSmr = null;
                var                             sourceSmr = smrRef.skinnedMeshRenderer.Value.gameObject;
                foreach (var smr in m_smrCache)
                {
                    var link = smr.GetComponent<HideThis.ShadowCloneSkinnedMeshTracker>();
                    if (link == null)
                        continue;

                    if (link.source == sourceSmr)
                    {
                        shadowSmr = smr;
                        break;
                    }
                }

                if (shadowSmr == null)
                {
                    UnityEngine.Debug.LogError(
                        $"Kinemation failed to bake mesh binding paths for {sourceSmr.gameObject.name}. The SkinnedMeshRenderer could not be found in the shadow hierarchy. This is an internal Kinemation bug. Please report!");
                    return;
                }

                var bones = shadowSmr.bones;
                if (bones == null)
                {
                    UnityEngine.Debug.LogWarning(
                        $"Kinemation failed to bake mesh binding paths for {sourceSmr.gameObject.name}. The bones array could not be extracted. Please ensure your avatar is configured correctly.");
                    return;
                }

                foreach (var bone in bones)
                {
                    if (bone == null)
                    {
                        UnityEngine.Debug.LogWarning(
                            $"Kinemation failed to bake mesh binding paths for {sourceSmr.gameObject.name}. One of the bones was null. Please ensure your avatar is configured correctly.");
                        return;
                    }
                }

                offsets.ResizeUninitialized(bones.Length);
                textCache.Clear();
                int i = 0;
                foreach (var bone in bones)
                {
                    offsets[i] = new MeshPathByteOffsetForPath { offset = textCache.Length };

                    var tf = bone;
                    while (tf.parent != null)
                    {
                        textCache.Append(tf.gameObject.name);
                        textCache.Append('/');
                        tf = tf.parent;
                    }

                    i++;
                }

                meshPathBytes.ResizeUninitialized(textCache.Length);
                UnsafeUtility.MemCpy(meshPathBytes.GetUnsafePtr(), textCache.GetUnsafePtr(), textCache.Length);
                textCache.Clear();
            }).WithEntityQueryOptions(EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities).WithoutBurst().Run();
            m_smrCache.Clear();
        }
    }
}

