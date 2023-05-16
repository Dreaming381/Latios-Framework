using System.Collections;
using System.Collections.Generic;
using Latios.Systems;
using Unity.Entities;
using UnityEngine;

namespace Latios
{
    internal class DeferredSimulationEndFrameController : MonoBehaviour
    {
        internal bool                               done = false;
        internal SimulationSystemGroup              simGroup;
        internal LatiosSimulationSystemGroupManager simManager;

        IEnumerator Start()
        {
            var endOfFrame = new WaitForEndOfFrame();

            while (!done)
            {
                simManager.skipInDeferred = false;
                simGroup.Update();
                simManager.skipInDeferred = true;
                yield return endOfFrame;
            }
        }
    }

    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [DisableAutoCreation]
    internal partial class DeferredSimulationEndFrameControllerSystem : SubSystem
    {
        GameObject m_controllerObject = null;

        protected override void OnUpdate()
        {
            if (m_controllerObject == null)
            {
                m_controllerObject                   = new GameObject();
                var controller                       = m_controllerObject.AddComponent<DeferredSimulationEndFrameController>();
                controller.simGroup                  = World.GetExistingSystemManaged<SimulationSystemGroup>();
                controller.simManager                = controller.simGroup.RateManager as LatiosSimulationSystemGroupManager;
                controller.simManager.skipInDeferred = true;
                m_controllerObject.hideFlags         = HideFlags.HideAndDontSave;
            }
        }

        protected override void OnDestroy()
        {
            if (m_controllerObject != null)
            {
                m_controllerObject.GetComponent<DeferredSimulationEndFrameController>().done = true;
                GameObject.Destroy(m_controllerObject);
            }
        }
    }
}

