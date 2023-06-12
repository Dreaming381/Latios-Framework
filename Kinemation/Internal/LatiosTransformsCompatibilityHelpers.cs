using System.Collections.Generic;
using System.Reflection;
using Latios.Transforms.Authoring;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;

namespace Latios.Kinemation
{
    internal static class RenderMeshUtilityReplacer
    {
#if !LATIOS_TRANSFORMS_UNCACHED_QVVS && !LATIOS_TRANSFORMS_UNITY
        static ComponentTypeSet ReplaceTransforms(in ComponentTypeSet original)
        {
            FixedList128Bytes<ComponentType> newTypes = default;
            for (int i = 0; i < original.Length; i++)
            {
                var t = original.GetComponentType(i);
                if (t.TypeIndex == TypeManager.GetTypeIndex<Unity.Transforms.LocalToWorld>())
                    newTypes.Add(ComponentType.ReadWrite<Latios.Transforms.WorldTransform>());
                else if (t.TypeIndex == TypeManager.GetTypeIndex<BuiltinMaterialPropertyUnity_MatrixPreviousM>())
                    newTypes.Add(ComponentType.ReadWrite<Latios.Transforms.PreviousTransform>());
                else
                    newTypes.Add(t);
            }
            return new ComponentTypeSet(in newTypes);
        }

        static bool s_initialized = false;

        public static void PatchRenderMeshUtility()
        {
            if (s_initialized)
                return;

            s_initialized = true;

            try
            {
                var egct  = RenderMeshUtility.s_EntitiesGraphicsComponentTypes;
                var field = egct.GetType().GetField("m_ComponentTypePermutations", BindingFlags.Instance | BindingFlags.NonPublic);
                var array = field.GetValue(egct) as ComponentTypeSet[];

                for (int i = 0; i < array.Length; i++)
                {
                    var original = array[i];
                    array[i]     = ReplaceTransforms(in original);
                }
            }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogException(e);
            }
        }
#elif !LATIOS_TRANSFORMS_UNCACHED_QVVS && LATIOS_TRANSFORMS_UNITY
        public static void PatchRenderMeshUtility()
        {
        }
#endif
    }
}

