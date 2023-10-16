using System.Collections;
using System.Collections.Generic;
using Latios.Systems;
using Unity.Entities;
using UnityEngine;

namespace Latios
{
    internal class DeferredSimulationEndFrameController : MonoBehaviour
    {
        internal bool                  done = false;
        internal SimulationSystemGroup simGroup;

        IEnumerator Start()
        {
            var endOfFrame = new WaitForEndOfFrame();

            while (!done)
            {
                simGroup.Update();
                yield return endOfFrame;
            }
        }
    }

    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [DisableAutoCreation]
    internal partial class DeferredSimulationEndFrameControllerSystem : ComponentSystemGroup
    {
        GameObject            m_controllerObject      = null;
        SimulationSystemGroup m_simulationSystemGroup = null;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_simulationSystemGroup = World.GetExistingSystemManaged<SimulationSystemGroup>();
            AddSystemToUpdateList(m_simulationSystemGroup);
        }

        protected override void OnUpdate()
        {
            if (m_controllerObject == null)
            {
                m_controllerObject           = new GameObject();
                var controller               = m_controllerObject.AddComponent<DeferredSimulationEndFrameController>();
                controller.simGroup          = m_simulationSystemGroup;
                m_controllerObject.hideFlags = HideFlags.HideAndDontSave;
            }
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

