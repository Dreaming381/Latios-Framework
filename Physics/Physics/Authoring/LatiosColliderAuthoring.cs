using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

using UnityCollider = UnityEngine.Collider;

namespace Latios.PhysicsEngine.Authoring
{
    public enum AuthoringColliderTypes
    {
        None,
        Compound
    }

    [DisallowMultipleComponent]
    public class LatiosColliderAuthoring : MonoBehaviour
    {
        public AuthoringColliderTypes colliderType = AuthoringColliderTypes.None;

        //Compound Data
        public bool generateFromChildren;

        public List<UnityCollider> colliders;
    }
}

