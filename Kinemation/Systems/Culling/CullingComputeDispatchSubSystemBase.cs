using System.Collections.Generic;
using Unity.Entities;

namespace Latios.Kinemation
{
    public enum CullingComputeDispatchState : byte
    {
        Collect,
        Write,
        Dispatch,
    }

    public struct CullingComputeDispatchActiveState : IComponentData
    {
        public CullingComputeDispatchState state;
    }

namespace Systems
{
    public abstract partial class CullingComputeDispatchSubSystemBase : SubSystem
    {
        protected abstract IEnumerable<bool> UpdatePhase();

        protected virtual void OnDestroyDispatchSystem()
        {
        }

        /// <summary>
        /// Returns false if the coroutine needs to restart from the beginning.
        /// Terminate is set to true if the system is being destroyed and resources
        /// should be cleaned up. If terminate is false and the method returns true,
        /// operation may continue as normal.
        /// </summary>
        /// <param name="expectedPhase">The expected next phase of operation</param>
        /// <param name="terminate"><see langword="true"/>if the system is being destroyed,
        /// false otherwise</param>
        /// <returns>True if the phase matched expectations, false otherwise.
        /// This returns true if terminate is true.</returns>
        protected bool GetPhaseActions(CullingComputeDispatchState expectedPhase, out bool terminate)
        {
            terminate = m_inShutdown;
            if (m_inShutdown)
                return true; // Force advance to termination.

            var state = worldBlackboardEntity.GetComponentData<CullingComputeDispatchActiveState>().state;
            return state == expectedPhase;
        }

        IEnumerator<bool> m_coroutine;
        bool              m_started    = false;
        bool              m_inShutdown = false;

        protected sealed override void OnUpdate()
        {
            if (!m_started)
            {
                m_coroutine = UpdatePhase().GetEnumerator();
                m_started   = true;
                return;
            }
            if (!m_coroutine.MoveNext())
            {
                UnityEngine.Debug.Log($"{GetType().FullName} UpdatePhase terminated unexpectedly. The system will be disabled.");
                Enabled   = false;
                m_started = false;
            }
        }

        protected sealed override void OnDestroy()
        {
            m_inShutdown = true;
            if (m_started)
                m_coroutine.MoveNext();
            OnDestroyDispatchSystem();
        }
    }
}
}

