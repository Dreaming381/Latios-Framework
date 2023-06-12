using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

namespace Latios.Authoring.Systems
{
    /// <summary>
    /// Baking System Group when all the Smart Blobber Systems execute.
    /// You need to use the [UpdateInGroup] attribute for custom Smart Blobber Systems.
    /// </summary>
    [UpdateInGroup(typeof(TransformBakingSystemGroup), OrderLast = true)]
    [WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
    public partial class SmartBlobberBakingGroup : ComponentSystemGroup
    {
    }

    /// <summary>
    /// Baking System Group when all the internally generated SmartBlobberPostProcessSystems execute.
    /// Such systems are added via calls to SmartBlobberTools<>.Register().
    /// Do not add custom systems to this group unless you absolutely know what you are doing.
    /// </summary>
    [UpdateInGroup(typeof(TransformBakingSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(SmartBlobberBakingGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
    public partial class SmartBlobberCleanupBakingGroup : ComponentSystemGroup
    {
        List<SystemHandle> m_systemsToAddBeforeCreate = null;
        bool               m_created                  = false;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_created = true;
            if (m_systemsToAddBeforeCreate == null)
                return;

            foreach (var system in m_systemsToAddBeforeCreate)
                AddSystemToUpdateList(system);
        }

        public void AddSystemToUpdateListSafe(SystemHandle system)
        {
            if (m_created)
            {
                AddSystemToUpdateList(system);
            }
            else
            {
                if (m_systemsToAddBeforeCreate == null)
                    m_systemsToAddBeforeCreate = new List<SystemHandle>();
                m_systemsToAddBeforeCreate.Add(system);
            }
        }
    }

    /// <summary>
    /// Baking System Group when all the Smart Blobber Resolver Systems execute to post-process Blob Assets
    /// before any other baking systems.
    /// </summary>
    [UpdateInGroup(typeof(TransformBakingSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(SmartBlobberCleanupBakingGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
    public partial class SmartBakerBakingGroup : ComponentSystemGroup
    {
    }
}

