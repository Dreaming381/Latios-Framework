using Latios.Authoring;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.Transforms.Authoring
{
    [BakeDerivedTypes]
    public class TransformOrderBakerBaker : Baker<Transform>
    {
        public override void Bake(Transform authoring)
        {
            if (authoring.parent == null)
                return;

            int index                                              = authoring.GetSiblingIndex();
            var entity                                             = GetEntityWithoutDependency();
            AddComponent(entity, new AuthoringSiblingIndex { index = index });
        }
    }
}

