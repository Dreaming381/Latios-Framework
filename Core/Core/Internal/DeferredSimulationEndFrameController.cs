using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

namespace Latios
{
    internal class DeferredSimulationEndFrameController : MonoBehaviour
    {
        internal bool                                done = false;
        internal Systems.LatiosSimulationSystemGroup simGroup;

        IEnumerator Start()
        {
            var endOfFrame = new WaitForEndOfFrame();

            while (!done)
            {
                simGroup.skipInDeferred = false;
                simGroup.Update();
                simGroup.skipInDeferred = true;
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
                m_controllerObject                 = new GameObject();
                var controller                     = m_controllerObject.AddComponent<DeferredSimulationEndFrameController>();
                controller.simGroup                = World.GetExistingSystem<Systems.LatiosSimulationSystemGroup>();
                controller.simGroup.skipInDeferred = true;
                m_controllerObject.hideFlags       = HideFlags.HideAndDontSave;
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

