using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

using UnityCollider = UnityEngine.Collider;

namespace Latios.Psyshock.Authoring
{
    public enum AuthoringColliderTypes
    {
        None,
        Compound
    }

    [DisallowMultipleComponent]
    [AddComponentMenu("Latios/Physics (Psyshock)/Collider")]
    public class ColliderAuthoring : MonoBehaviour
    {
        public AuthoringColliderTypes colliderType = AuthoringColliderTypes.None;

        //Compound Data
        public bool generateFromChildren;

        public List<UnityCollider> colliders;
    }
}

