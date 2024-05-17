using System.Reflection;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;

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

            // Todo: We should optimize this to improve iteration time.
            for (int i = 0; i < (int)RenderMeshUtility.EntitiesGraphicsComponentFlags.PermutationCount; i++)
                RenderMeshUtility.ComputeComponentTypes((RenderMeshUtility.EntitiesGraphicsComponentFlags)i);

            try
            {
                var egctField = typeof(RenderMeshUtility).GetField("s_EntitiesGraphicsComponentTypes", BindingFlags.Static | BindingFlags.NonPublic);  //RenderMeshUtility.s_EntitiesGraphicsComponentTypes;
                var egct      = egctField.GetValue(null);
                var ctpField  = egctField.FieldType.GetField("m_ComponentTypePermutations", BindingFlags.Instance | BindingFlags.NonPublic);
                var array     = ctpField.GetValue(egct) as ComponentTypeSet[];

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

