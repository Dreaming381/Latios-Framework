#if UNITY_NETCODE && !LATIOS_TRANSFORMS_UNITY
using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEditor;
using UnityEngine.Scripting;

namespace Latios.Transforms.Compatibility.UnityNetCode
{
    /// <summary>
    /// The default serialization strategy for the <see cref="WorldTransform"/> components.
    /// </summary>
    [Preserve]
    [GhostComponentVariation(typeof(Transforms.WorldTransform), "Transform QVVS - 3D")]
    [GhostComponent(PrefabType = GhostPrefabType.All, SendTypeOptimization = GhostSendType.AllClients)]
    public struct WorldTransformDefaultVariant
    {
        /// <summary>
        /// The rotation quaternion is replicated and the resulting floating point data use for replication the rotation is quantized with good precision (10 or more bits per component)
        /// </summary>
        [GhostField(Quantization = 1000, Smoothing = SmoothingAction.InterpolateAndExtrapolate)]
        public quaternion __rotation;

        /// <summary>
        /// The position value is replicated with a default quantization unit of 1000 (so roughly 1mm precision per component).
        /// The replicated position value support both interpolation and extrapolation
        /// </summary>
        [GhostField(Quantization = 1000, Smoothing = SmoothingAction.InterpolateAndExtrapolate)]
        public float3 __position;

        /// <summary>
        /// The scale value is replicated with a default quantization unit of 1000.
        /// The replicated scale value support both interpolation and extrapolation
        /// </summary>
        [GhostField(Quantization = 1000, Smoothing = SmoothingAction.InterpolateAndExtrapolate)]
        public float __scale;

        /// <summary>
        /// The stretch value is replicated with a default quantization unit of 1000.
        /// The replicated stretch value support both interpolation and extrapolation
        /// </summary>
        [GhostField(Quantization = 1000, Smoothing = SmoothingAction.InterpolateAndExtrapolate)]
        public float3 __stretch;
    }

    /// <summary>
    /// The default prediction error <see cref="SmoothingAction"/> function for the <see cref="WorldTransform"/> component.
    /// Supports the user data that lets you customize the clamping and snapping of the WorldTransform component (any time the worldTransform prediction error is too large).
    /// </summary>
    [BurstCompile]
    public unsafe struct DefaultWorldTransformSmoothingAction
    {
        public static void Register(LatiosWorld world)
        {
            //world.worldBlackboardEntity.GetComponentData<GhostPredictionSmoothing>().RegisterSmoothingAction<WorldTransform>(world.EntityManager, Action);
            using var query = world.EntityManager.CreateEntityQuery(new EntityQueryBuilder(Allocator.Temp).WithAll<GhostPredictionSmoothing>());
            query.GetSingleton<GhostPredictionSmoothing>().RegisterSmoothingAction<WorldTransform>(world.EntityManager, Action);
        }

        /// <summary>
        /// The default value for the <see cref="DefaultSmoothingActionUserParams"/> if the no user data is passed to the function.
        /// Position is corrected if the prediction error is at least 1 unit (usually mt) and less than 10 unit (usually mt)
        /// </summary>
        public sealed class DefaultStaticUserParams
        {
            /// <summary>
            /// If the prediction error is larger than this value, the entity position is snapped to the new value.
            /// The default threshold is 10 units.
            /// </summary>
            public static readonly SharedStatic<float> maxDist = SharedStatic<float>.GetOrCreate<DefaultStaticUserParams, MaxDistKey>();
            /// <summary>
            /// If the prediction error is smaller than this value, the entity position is snapped to the new value.
            /// The default threshold is 1 units.
            /// </summary>
            public static readonly SharedStatic<float> delta = SharedStatic<float>.GetOrCreate<DefaultStaticUserParams, DeltaKey>();

            static DefaultStaticUserParams()
            {
                maxDist.Data = 10;
                delta.Data   = 0.01f;
            }
            class MaxDistKey
            {
            }
            class DeltaKey
            {
            }
        }

        /// <summary>
        /// Return a the burst compatible function pointer that can be used to register the smoothing action to the
        /// <see cref="GhostPredictionSmoothing"/> singleton.
        /// </summary>
        public static readonly PortableFunctionPointer<GhostPredictionSmoothing.SmoothingActionDelegate> Action =
            new PortableFunctionPointer<GhostPredictionSmoothing.SmoothingActionDelegate>(SmoothingAction);

        [BurstCompile(DisableDirectCall = true)]
        [AOT.MonoPInvokeCallback(typeof(GhostPredictionSmoothing.SmoothingActionDelegate))]
        private static void SmoothingAction(IntPtr currentData, IntPtr previousData, IntPtr usrData)
        {
            ref var trans  = ref UnsafeUtility.AsRef<WorldTransform>((void*)currentData);
            ref var backup = ref UnsafeUtility.AsRef<WorldTransform>((void*)previousData);

            float maxDist = DefaultStaticUserParams.maxDist.Data;
            float delta   = DefaultStaticUserParams.delta.Data;

            if (usrData.ToPointer() != null)
            {
                ref var userParam = ref UnsafeUtility.AsRef<DefaultSmoothingActionUserParams>(usrData.ToPointer());
                maxDist = userParam.maxDist;
                delta   = userParam.delta;
            }

            var dist = math.distance(trans.position, backup.position);
            if (dist < maxDist && dist > delta && dist > 0)
            {
                trans.worldTransform.position = backup.worldTransform.position + (trans.worldTransform.position - backup.worldTransform.position) * delta / dist;
            }
        }
    }
}
#endif

