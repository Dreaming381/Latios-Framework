using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

namespace Latios.Authoring.Systems
{
    /// <summary>
    /// Conversion System Group when all the Smart Blobber Systems execute.
    /// The UpdateInGroup attribute is inherited from the base classes.
    /// </summary>
    [UpdateInGroup(typeof(GameObjectBeforeConversionGroup), OrderLast = true)]
    [WorldSystemFilter(WorldSystemFilterFlags.GameObjectConversion)]
    public class SmartBlobberConversionGroup : ComponentSystemGroup
    {
    }

    /// <summary>
    /// Conversion System used to invoke RequestBlobAssets after the DeclareReferencedPrefabs stage
    /// but before SmartBlobberConversion.
    /// </summary>
    [UpdateInGroup(typeof(GameObjectBeforeConversionGroup))]
    public class RequestSmartBlobAssetsConversionSystem : GameObjectConversionSystem
    {
        void Convert(Transform transform, List<IRequestBlobAssets> convertibles)
        {
            try
            {
                transform.GetComponents(convertibles);

                foreach (var c in convertibles)
                {
                    var behaviour = c as Behaviour;
                    if (behaviour != null && !behaviour.enabled)
                        continue;

#if UNITY_EDITOR
                    if (!ShouldRunConversionSystem(c.GetType()))
                        continue;
#endif

                    var entity = GetPrimaryEntity((Component)c);
                    c.RequestBlobAssets(entity, DstEntityManager, this);
                }
            }
            catch (Exception x)
            {
                Debug.LogException(x, transform);
            }
        }

        protected override void OnUpdate()
        {
            var convertibles = new List<IRequestBlobAssets>();

            Entities.ForEach((Transform transform) => Convert(transform, convertibles));
            convertibles.Clear();

            Entities.ForEach((RectTransform transform) => Convert(transform, convertibles));
        }
    }
}

