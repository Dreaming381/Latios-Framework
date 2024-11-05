using Latios.Unsafe;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Unika
{
    internal static unsafe class ScriptStructuralChangeInternal
    {
        struct InstanceIdWarningKey { }

        static readonly SharedStatic<byte> s_instanceIdWarning = SharedStatic<byte>.GetOrCreate<InstanceIdWarningKey>();

        public static void InitializeStatics() => s_instanceIdWarning.Data = 0;

        public static int AllocateScript(ref DynamicBuffer<UnikaScripts> scriptBuffer, int scriptType)
        {
            var currentScriptCount = scriptBuffer.AllScripts(default).length;
            var scripts            = scriptBuffer.Reinterpret<ScriptHeader>();
            var mask               = ScriptTypeInfoManager.GetBloomMask((short)scriptType);
            var sizeAndAlignment   = ScriptTypeInfoManager.GetSizeAndAlignement((short)scriptType);
            UnityEngine.Assertions.Assert.IsTrue((ulong)sizeAndAlignment.x < ScriptHeader.kMaxByteOffset);
            UnityEngine.Assertions.Assert.IsTrue(sizeAndAlignment.y <= UnsafeUtility.SizeOf<ScriptHeader>());

            if (currentScriptCount == 0)
            {
                scripts.Add(new ScriptHeader
                {
                    bloomMask          = mask,
                    instanceCount      = 1,
                    lastUsedInstanceId = 1
                });

                var newCapacity = math.ceilpow2(1);
                scripts.Add(new ScriptHeader
                {
                    bloomMask  = mask,
                    instanceId = 1,
                    scriptType = scriptType,
                    byteOffset = 0
                });
                for (int i = 1; i < newCapacity; i++)
                    scripts.Add(default);

                var elementsNeeded = CollectionHelper.Align(sizeAndAlignment.x, UnsafeUtility.SizeOf<ScriptHeader>()) / UnsafeUtility.SizeOf<ScriptHeader>();
                for (int i = 0; i < elementsNeeded; i++)
                    scripts.Add(default);

                return 0;
            }

            // If we need to increase the capacity, add new elements, then slide all the scripts over
            var scriptCapacity = math.ceilpow2(currentScriptCount);
            if (currentScriptCount == scriptCapacity)
            {
                for (int i = 0; i < scriptCapacity; i++)
                    scripts.Add(default);

                var src       = scripts.AsNativeArray().GetSubArray(1 + scriptCapacity, 1).GetUnsafePtr();
                var dst       = scripts.AsNativeArray().GetSubArray(1 + scriptCapacity * 2, 1).GetUnsafePtr();
                var byteCount = (scripts.Length - (1 + scriptCapacity * 2)) * UnsafeUtility.SizeOf<ScriptHeader>();
                UnsafeUtility.MemMove(dst, src, byteCount);
                scriptCapacity *= 2;
            }

            // Update the master index and allocate the new script's instance ID
            ref var master        = ref scripts.ElementAt(0);
            master.bloomMask     |= mask;
            master.instanceCount  = currentScriptCount + 1;
            var nextIndex         = master.lastUsedInstanceId;
            if ((ulong)nextIndex == ScriptHeader.kMaxInstanceId)
            {
                if (s_instanceIdWarning.Data == 0)
                {
                    UnityEngine.Debug.LogWarning(
                        "Exhausted all instance IDs in a Unika entity. Instance IDs will be reused, which may result in stale references incorrectly referencing new scripts. This message will be disabled to prevent spamming.");
                    s_instanceIdWarning.Data = 1;
                }
                // Grab all the currently used instance IDs and sort them
                using var allocator   = ThreadStackAllocator.GetAllocator();
                var       rawArray    = allocator.Allocate<int>(currentScriptCount);
                var       usedIndices = new UnsafeList<int>(rawArray, currentScriptCount);
                for(int   i           = 0; i < currentScriptCount; i++)
                {
                    usedIndices[i] = scripts[i + 1].instanceId;
                }
                usedIndices.Sort();
                // If the largest instance ID is not close to the max, that means that all the high-valued instance IDs were destroyed
                // and all new instances have lower IDs. We can stop hitting the slow path by incrementing from the highest ID already in use.
                if ((ulong)usedIndices[usedIndices.Length - 1] < ScriptHeader.kMaxInstanceId / 2)
                {
                    nextIndex                 = usedIndices[usedIndices.Length - 1] + 1;
                    master.lastUsedInstanceId = nextIndex;
                }
                else
                {
                    // Find the first free ID. Because IDs are unique and sorted, we only have to find where the incrementing sequence breaks.
                    for (int i = 0; i < usedIndices.Length; i++)
                    {
                        if (i + 1 != usedIndices[i])
                        {
                            nextIndex = i + 1;
                            break;
                        }
                    }
                }
            }
            else
            {
                nextIndex++;
                master.lastUsedInstanceId = nextIndex;
            }

            // Look up the last script to find the total bytes used, align it, and allocate the new script's memory
            var nextFreeByteOffset = scripts[currentScriptCount].byteOffset + ScriptTypeInfoManager.GetSizeAndAlignement((short)scripts[currentScriptCount].scriptType).x;
            var alignment          = CollectionHelper.Align(nextFreeByteOffset, sizeAndAlignment.y);
            UnityEngine.Assertions.Assert.IsTrue((ulong)(alignment + sizeAndAlignment.x) <= ScriptHeader.kMaxByteOffset + 1);
            var requiredTotalElementSize =
                (CollectionHelper.Align(alignment + sizeAndAlignment.x, UnsafeUtility.SizeOf<ScriptHeader>()) / UnsafeUtility.SizeOf<ScriptHeader>()) + scriptCapacity + 1;
            for (int i = scripts.Length; i < requiredTotalElementSize; i++)
                scripts.Add(default);

            scripts[currentScriptCount + 1] = new ScriptHeader
            {
                bloomMask  = mask,
                scriptType = scriptType,
                byteOffset = alignment,
                instanceId = nextIndex
            };
            return currentScriptCount + 1;
        }

        public static void FreeScript(ref DynamicBuffer<UnikaScripts> scriptBuffer, int scriptIndex)
        {
            var currentScriptCount = scriptBuffer.AllScripts(default).length;
            var scripts            = scriptBuffer.Reinterpret<ScriptHeader>();
            if (scripts.Length == 1)
            {
                scripts.Clear();
                return;
            }

            // Slide subsequent headers over by 1
            var currentScriptCapacity = math.ceilpow2(currentScriptCount);
            var newScriptCapacity     = math.ceilpow2(currentScriptCount - 1);
            var removedHeader         = scripts[scriptIndex + 1];
            for (int i = scriptIndex + 1; i < scripts.Length; i++)
                scripts[i] = scripts[i + 1];

            // If the capacity changes, slide preceeding scripts over to the new script segment base pointer.
            var oldBasePtr = (byte*)scripts.AsNativeArray().GetSubArray(currentScriptCapacity + 1, 1).GetUnsafePtr();
            var newBasePtr = (byte*)scripts.AsNativeArray().GetSubArray(newScriptCapacity + 1, 1).GetUnsafePtr();
            if (currentScriptCapacity != newScriptCapacity)
            {
                UnsafeUtility.MemMove(newBasePtr, oldBasePtr, removedHeader.byteOffset);
            }

            // Accumulate masks for preceeding scripts and capture real byte count used (because the removed script may have had padded alignment)
            ulong accumulatedMask     = 0;
            int   usedBytesPreceeding = 0;
            for (int i = 0; i < scriptIndex; i++)
            {
                var header      = scripts[i + 1];
                accumulatedMask = header.bloomMask;
                if (i + 1 == scriptIndex)
                    usedBytesPreceeding = header.byteOffset + ScriptTypeInfoManager.GetSizeAndAlignement((short)header.scriptType).x;
            }

            // Accumulate masks for subsequent scripts and move each script one-by-one, as alignment may be different
            for (int i = scriptIndex; i < currentScriptCount - 1; i++)
            {
                var header           = scripts[i + 1];
                accumulatedMask      = header.bloomMask;
                var sizeAndAlignment = ScriptTypeInfoManager.GetSizeAndAlignement((short)header.scriptType);
                var newOffset        = CollectionHelper.Align(usedBytesPreceeding, sizeAndAlignment.y);
                UnsafeUtility.MemMove(newBasePtr + newOffset, oldBasePtr + header.byteOffset, sizeAndAlignment.x);
                header.byteOffset   = newOffset;
                usedBytesPreceeding = newOffset + sizeAndAlignment.x;
            }

            // Update the master header
            ref var master       = ref scripts.ElementAt(0);
            master.bloomMask     = accumulatedMask;
            master.instanceCount = currentScriptCount - 1;

            // Delete elements off the end to account for the new size
            var totalUsedElements  = 1 + newScriptCapacity;
            totalUsedElements     += CollectionHelper.Align(usedBytesPreceeding, UnsafeUtility.SizeOf<ScriptHeader>()) / UnsafeUtility.SizeOf<ScriptHeader>();
            scripts.Length         = totalUsedElements;
        }
    }
}

