using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.Kinemation.Authoring
{
    /// <summary>
    /// Excludes the GameObject from becoming a bone in the skeleton, whether exposed or optimized (exported).
    /// This does not prevent skinned meshes from being bound to the skeleton if the mesh has bindposes.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Latios/Kinemation/Exclude From Skeleton")]
    public class ExcludeFromSkeletonAuthoring : MonoBehaviour
    {
    }
}

