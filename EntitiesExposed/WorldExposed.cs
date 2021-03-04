using System;
using Unity.Collections;

namespace Unity.Entities.Exposed
{
    public static class WorldExposedExtensions
    {
        /// <summary>
        /// Returns the managed executing system type if one is executing.
        /// </summary>
        /// <param name="world"></param>
        /// <returns></returns>
        [NotBurstCompatible]
        public static Type ExecutingSystemType(this World world)
        {
            return world.Unmanaged.GetTypeOfSystem(world.Unmanaged.ExecutingSystem);
        }
    }
}

