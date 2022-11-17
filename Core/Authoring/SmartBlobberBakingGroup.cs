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
    public class SmartBlobberBakingGroup : ComponentSystemGroup
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
    public class SmartBlobberCleanupBakingGroup : ComponentSystemGroup
    {
    }

    /// <summary>
    /// Baking System Group when all the Smart Baker Systems execute to post-process ISmartBakeItem.
    /// Do not add custom systems to this group unless you absolutely know what you are doing.
    /// </summary>
    [UpdateInGroup(typeof(TransformBakingSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(SmartBlobberCleanupBakingGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
    public class SmartBakerBakingGroup : ComponentSystemGroup
    {
    }
}

