using System.Collections;
using Unity.Entities;
using UnityEngine;

namespace Latios
{
    internal class DeferredSimulationEndFrameController : MonoBehaviour
    {
        internal bool                  needsUpdate = false;
        internal bool                  done        = false;
        internal SimulationSystemGroup simGroup;

        IEnumerator Start()
        {
            var endOfFrame = new WaitForEndOfFrame();

            while (!done)
            {
                simGroup.Update();
                needsUpdate = false;
                yield return endOfFrame;
            }
        }
    }

    [DisableAutoCreation]
    internal partial class DeferredSimulationEndFrameControllerSystem : SystemBase
    {
        GameObject                           m_controllerObject      = null;
        DeferredSimulationEndFrameController m_controller            = null;
        SimulationSystemGroup                m_simulationSystemGroup = null;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_simulationSystemGroup = World.GetExistingSystemManaged<SimulationSystemGroup>();
        }

        protected override void OnUpdate()
        {
            if (m_controllerObject == null)
            {
                m_controllerObject           = new GameObject();
                m_controller                 = m_controllerObject.AddComponent<DeferredSimulationEndFrameController>();
                m_controller.simGroup        = m_simulationSystemGroup;
                m_controllerObject.hideFlags = HideFlags.HideAndDontSave;
            }
            else if (m_controller.needsUpdate)
            {
                m_simulationSystemGroup.Update();
            }
            m_controller.needsUpdate = true;
        }

        protected override void OnDestroy()
        {
            if (m_controllerObject != null)
            {
                m_controllerObject.GetComponent<DeferredSimulationEndFrameController>().done = true;
                GameObject.Destroy(m_controllerObject);
            }
            base.OnDestroy();
        }
    }
}

