using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using UnityEngine;

namespace Latios.Kinemation.Authoring
{
    /// <summary>
    /// Defines a single event in an animation clip
    /// </summary>
    public struct ClipEvent : IComparable<ClipEvent>
    {
        public FixedString64Bytes name;
        public int                parameter;
        public float              time;

        public int CompareTo(ClipEvent other)
        {
            var result = time.CompareTo(other.time);
            if (result == 0)
                result = name.CompareTo(other.name);
            if (result == 0)
                result = parameter.CompareTo(other.parameter);
            return result;
        }
    }

    public static class AnimationClipEventExtensions
    {
        /// <summary>
        /// Generates an array of ClipEvent which can be used for generating Kinemation animation clip blobs.
        /// For each AnimationEvent in the clip, functionName, intParameter, and time are extracted.
        /// </summary>
        public static NativeArray<ClipEvent> ExtractKinemationClipEvents(this AnimationClip clip, AllocatorManager.AllocatorHandle allocator)
        {
            var srcEvents = clip.events;
            var dstEvents = CollectionHelper.CreateNativeArray<ClipEvent>(srcEvents.Length, allocator, NativeArrayOptions.UninitializedMemory);

            for (int i = 0; i < srcEvents.Length; i++)
            {
                var src = srcEvents[i];

                dstEvents[i] = new ClipEvent
                {
                    name      = src.functionName,
                    parameter = src.intParameter,
                    time      = src.time
                };
            }

            return dstEvents;
        }
    }

    internal static class ClipEventsBlobHelpers
    {
        internal static void Convert(ref ClipEvents dstEvents, ref BlobBuilder builder, NativeArray<ClipEvent> srcEvents)
        {
            if (srcEvents.IsCreated)
            {
                var times      = builder.Allocate(ref dstEvents.times, srcEvents.Length);
                var nameHashes = builder.Allocate(ref dstEvents.nameHashes, srcEvents.Length);
                var parameters = builder.Allocate(ref dstEvents.parameters, srcEvents.Length);
                var names      = builder.Allocate(ref dstEvents.names, srcEvents.Length);

                srcEvents.Sort();

                for (int i = 0; i < srcEvents.Length; i++)
                {
                    times[i]      = srcEvents[i].time;
                    nameHashes[i] = srcEvents[i].name.GetHashCode();
                    parameters[i] = srcEvents[i].parameter;
                    names[i]      = srcEvents[i].name;
                }
            }
            else
            {
                builder.Allocate(ref dstEvents.times,      0);
                builder.Allocate(ref dstEvents.nameHashes, 0);
                builder.Allocate(ref dstEvents.parameters, 0);
                builder.Allocate(ref dstEvents.names,      0);
            }
        }
    }
}

