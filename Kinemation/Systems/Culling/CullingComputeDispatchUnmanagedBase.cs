using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Kinemation
{
    /// <summary>
    /// An interface an ISystem may implement to help guide execution in the CullingRoundRobindDispatchSuperSystem.
    /// Such a system should also own an instance of CullingComputeDispatchData and call DoUpdate() on it in OnUpdate().
    /// </summary>
    /// <typeparam name="TCollect">A struct containing things from the Collect phase that should be passed on to the Write phase.
    /// It is safe for this type to hold containers allocated with WorldUpdateAllocator.</typeparam>
    /// <typeparam name="TWrite">A struct containing things from the Write phase that should be passed on to the Dispatch phase.
    /// It is safe for this type to hold containers allocated with WorldUpdateAllocator.</typeparam>
    /// <remarks>
    /// The round-robin system operates in 3 phases, with each phase being a separate OnUpdate() call.
    ///
    /// In the Collect phase, the system should schedule jobs to figure out how much GraphicsBuffer memory it needs.
    /// It is often useful to cache pointers or other metadata to make the Write phase faster.
    ///
    /// In the Write phase, the system should get mapped NativeArrays from locked GraphicsBuffers (typically fetched from GraphicsBufferBroker).
    /// Then it should schedule jobs to populate these NativeArrays.
    ///
    /// In the Dispatch phase, the system should unlock the GraphicsBuffers, dispatch ComputeShaders, and assign buffers to global shader variables.
    /// </remarks>
    public interface ICullingComputeDispatchSystem<TCollect, TWrite> where TCollect : unmanaged where TWrite : unmanaged
    {
        public TCollect Collect(ref SystemState state);
        public TWrite Write(ref SystemState state, ref TCollect collected);
        public void Dispatch(ref SystemState state, ref TWrite written);
    }

    /// <summary>
    /// The main state of an ICullingComputeDispatchSystem
    /// </summary>
    /// <typeparam name="TCollect">A struct containing things from the Collect phase that should be passed on to the Write phase.
    /// It is safe for this type to hold containers allocated with WorldUpdateAllocator.</typeparam>
    /// <typeparam name="TWrite">A struct containing things from the Write phase that should be passed on to the Dispatch phase.
    /// It is safe for this type to hold containers allocated with WorldUpdateAllocator.</typeparam>
    public struct CullingComputeDispatchData<TCollect, TWrite> where TCollect : unmanaged where TWrite : unmanaged
    {
        TCollect                    collected;
        TWrite                      written;
        BlackboardEntity            worldBlackboardEntity;
        CullingComputeDispatchState nextExpectedState;

        /// <summary>
        /// Create the backing state. Call this in OnCreate() and assign to a member on the system.
        /// </summary>
        /// <param name="latiosWorld">The LatiosWorldUnmanaged for the system.</param>
        public CullingComputeDispatchData(LatiosWorldUnmanaged latiosWorld)
        {
            collected             = default;
            written               = default;
            worldBlackboardEntity = latiosWorld.worldBlackboardEntity;
            nextExpectedState     = CullingComputeDispatchState.Collect;
        }

        /// <summary>
        /// Perform the OnUpdate() routine of the system, which will then call one of the callbacks specified by the ICullingComputeDispatchSystem interface.
        /// </summary>
        /// <typeparam name="TSystem">The type of system that owns this instance</typeparam>
        /// <param name="state">The SystemState of the system that owns this instance</param>
        /// <param name="system">The system that owns this instance</param>
        public void DoUpdate<TSystem>(ref SystemState state, ref TSystem system) where TSystem : ICullingComputeDispatchSystem<TCollect, TWrite>
        {
            var activeState = worldBlackboardEntity.GetComponentData<CullingComputeDispatchActiveState>();
            if (activeState.state != nextExpectedState)
            {
                UnityEngine.Debug.LogError("The CullingComputeDispatch expected state does not match the current state. Behavior may not be correct.");
            }
            switch (activeState.state)
            {
                case CullingComputeDispatchState.Collect:
                    collected         = system.Collect(ref state);
                    nextExpectedState = CullingComputeDispatchState.Write;
                    break;
                case CullingComputeDispatchState.Write:
                    written           = system.Write(ref state, ref collected);
                    nextExpectedState = CullingComputeDispatchState.Dispatch;
                    break;
                case CullingComputeDispatchState.Dispatch:
                    system.Dispatch(ref state, ref written);
                    nextExpectedState = CullingComputeDispatchState.Collect;
                    break;
            }
        }
    }
}

