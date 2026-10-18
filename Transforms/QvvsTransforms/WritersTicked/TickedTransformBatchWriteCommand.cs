#if !LATIOS_TRANSFORMS_UNITY
using System;
using System.Collections.Generic;
using Latios.Unsafe;
using static Latios.Transforms.TransformTools.Propagate;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Transforms
{
    /// <summary>
    /// A struct describing a single write task for a single TickedTransformAspect, which can be performed as part of a bulk operation
    /// for better performance.
    /// </summary>
    public struct TickedTransformBatchWriteCommand
    {
        internal TickedTransformAspect  aspect;
        internal WriteCommand.WriteType writeType;
        internal int                    sortId;  // Todo: Eliminate this with a stable sort.
        internal TransformQvvs          writeData;

        /// <summary>
        /// Creates a command that sets the world transform
        /// </summary>
        /// <param name="transform">The TickedTransformAspect the command should apply to</param>
        /// <param name="worldTransform">The new world transform to apply</param>
        /// <returns>The resulting command that can be applied later</returns>
        public static TickedTransformBatchWriteCommand SetWorldTransform(TickedTransformAspect transform, in TransformQvvs worldTransform)
        {
            return new TickedTransformBatchWriteCommand
            {
                aspect    = transform,
                writeType = WriteCommand.WriteType.WorldTransformSet,
                writeData = worldTransform
            };
        }

        /// <summary>
        /// Creates a command that sets the world position
        /// </summary>
        /// <param name="transform">The TickedTransformAspect the command should apply to</param>
        /// <param name="worldPosition">The new world position to apply</param>
        /// <returns>The resulting command that can be applied later</returns>
        public static TickedTransformBatchWriteCommand SetWorldPosition(TickedTransformAspect transform, float3 worldPosition)
        {
            var data      = TransformQvvs.identity;
            data.position = worldPosition;
            return new TickedTransformBatchWriteCommand
            {
                aspect    = transform,
                writeType = WriteCommand.WriteType.WorldPositionSet,
                writeData = data
            };
        }

        /// <summary>
        /// Creates a command that sets the world rotation
        /// </summary>
        /// <param name="transform">The TickedTransformAspect the command should apply to</param>
        /// <param name="worldRotation">The new world rotation to apply</param>
        /// <returns>The resulting command that can be applied later</returns>
        public static TickedTransformBatchWriteCommand SetWorldRotation(TickedTransformAspect transform, quaternion worldRotation)
        {
            var data      = TransformQvvs.identity;
            data.rotation = worldRotation;
            return new TickedTransformBatchWriteCommand
            {
                aspect    = transform,
                writeType = WriteCommand.WriteType.WorldRotationSet,
                writeData = data
            };
        }

        /// <summary>
        /// Creates a command that sets the world scale
        /// </summary>
        /// <param name="transform">The TickedTransformAspect the command should apply to</param>
        /// <param name="worldScale">The new world scale to apply</param>
        /// <returns>The resulting command that can be applied later</returns>
        public static TickedTransformBatchWriteCommand SetWorldScale(TickedTransformAspect transform, float worldScale)
        {
            var data   = TransformQvvs.identity;
            data.scale = worldScale;
            return new TickedTransformBatchWriteCommand
            {
                aspect    = transform,
                writeType = WriteCommand.WriteType.WorldScaleSet,
                writeData = data
            };
        }

        /// <summary>
        /// Creates a command that sets the world position and rotation
        /// </summary>
        /// <param name="transform">The TickedTransformAspect the command should apply to</param>
        /// <param name="worldPosition">The new world position to apply</param>
        /// <param name="worldRotation">The new world rotation to apply</param>
        /// <returns>The resulting command that can be applied later</returns>
        public static TickedTransformBatchWriteCommand SetWorldPositionAndRotation(TickedTransformAspect transform, float3 worldPosition, quaternion worldRotation)
        {
            var data      = TransformQvvs.identity;
            data.position = worldPosition;
            data.rotation = worldRotation;
            return new TickedTransformBatchWriteCommand
            {
                aspect    = transform,
                writeType = WriteCommand.WriteType.WorldPositionRotationSet,
                writeData = data
            };
        }

        /// <summary>
        /// Creates a command that sets the local transform including stretch and context32
        /// </summary>
        /// <param name="transform">The TickedTransformAspect the command should apply to</param>
        /// <param name="localTransform">The new local transform to apply</param>
        /// <returns>The resulting command that can be applied later</returns>
        public static TickedTransformBatchWriteCommand SetLocalTransformQvvs(TickedTransformAspect transform, in TransformQvvs localTransform)
        {
            return new TickedTransformBatchWriteCommand
            {
                aspect    = transform,
                writeType = WriteCommand.WriteType.LocalTransformQvvsSet,
                writeData = localTransform
            };
        }

        /// <summary>
        /// Creates a command that sets the local transform
        /// </summary>
        /// <param name="transform">The TickedTransformAspect the command should apply to</param>
        /// <param name="localTransform">The new local transform to apply</param>
        /// <returns>The resulting command that can be applied later</returns>
        public static TickedTransformBatchWriteCommand SetLocalTransform(TickedTransformAspect transform, in TransformQvs localTransform)
        {
            return new TickedTransformBatchWriteCommand
            {
                aspect    = transform,
                writeType = WriteCommand.WriteType.LocalTransformSet,
                writeData = new TransformQvvs(localTransform.position, localTransform.rotation, localTransform.scale, 1f)
            };
        }

        /// <summary>
        /// Creates a command that sets the local position
        /// </summary>
        /// <param name="transform">The TickedTransformAspect the command should apply to</param>
        /// <param name="localPosition">The new local position to apply</param>
        /// <returns>The resulting command that can be applied later</returns>
        public static TickedTransformBatchWriteCommand SetLocalPosition(TickedTransformAspect transform, float3 localPosition)
        {
            var data      = TransformQvvs.identity;
            data.position = localPosition;
            return new TickedTransformBatchWriteCommand
            {
                aspect    = transform,
                writeType = WriteCommand.WriteType.LocalPositionSet,
                writeData = data
            };
        }

        /// <summary>
        /// Creates a command that sets the local rotation
        /// </summary>
        /// <param name="transform">The TickedTransformAspect the command should apply to</param>
        /// <param name="localRotation">The new local rotation to apply</param>
        /// <returns>The resulting command that can be applied later</returns>
        public static TickedTransformBatchWriteCommand SetLocalRotation(TickedTransformAspect transform, quaternion localRotation)
        {
            var data      = TransformQvvs.identity;
            data.rotation = localRotation;
            return new TickedTransformBatchWriteCommand
            {
                aspect    = transform,
                writeType = WriteCommand.WriteType.LocalRotationSet,
                writeData = data
            };
        }

        /// <summary>
        /// Creates a command that sets the local position and rotation
        /// </summary>
        /// <param name="transform">The TickedTransformAspect the command should apply to</param>
        /// <param name="localPosition">The new local position to apply</param>
        /// <param name="localRotation">The new local rotation to apply</param>
        /// <returns>The resulting command that can be applied later</returns>
        public static TickedTransformBatchWriteCommand SetLocalPositionAndRotation(TickedTransformAspect transform, float3 localPosition, quaternion localRotation)
        {
            var data      = TransformQvvs.identity;
            data.position = localPosition;
            data.rotation = localRotation;
            return new TickedTransformBatchWriteCommand
            {
                aspect    = transform,
                writeType = WriteCommand.WriteType.LocalPositionRotationSet,
                writeData = data
            };
        }

        /// <summary>
        /// Creates a command that sets the local scale
        /// </summary>
        /// <param name="transform">The TickedTransformAspect the command should apply to</param>
        /// <param name="localScale">The new local scale to apply</param>
        /// <returns>The resulting command that can be applied later</returns>
        public static TickedTransformBatchWriteCommand SetLocalScale(TickedTransformAspect transform, float localScale)
        {
            var data   = TransformQvvs.identity;
            data.scale = localScale;
            return new TickedTransformBatchWriteCommand
            {
                aspect    = transform,
                writeType = WriteCommand.WriteType.LocalScaleSet,
                writeData = data
            };
        }

        /// <summary>
        /// Creates a command that sets the stretch
        /// </summary>
        /// <param name="transform">The TickedTransformAspect the command should apply to</param>
        /// <param name="stretch">The new stretch to apply</param>
        /// <returns>The resulting command that can be applied later</returns>
        public static TickedTransformBatchWriteCommand SetStretch(TickedTransformAspect transform, float3 stretch)
        {
            var data     = TransformQvvs.identity;
            data.stretch = stretch;
            return new TickedTransformBatchWriteCommand
            {
                aspect    = transform,
                writeType = WriteCommand.WriteType.StretchSet,
                writeData = data
            };
        }

        /// <summary>
        /// Creates a commands that applies a position delta in world-space
        /// </summary>
        /// <param name="transform">The TickedTransformAspect the command should apply to</param>
        /// <param name="translation">The position delta to apply</param>
        /// <returns>The resulting command that can be applied later</returns>
        public static TickedTransformBatchWriteCommand TranslateWorld(TickedTransformAspect transform, float3 translation)
        {
            var data      = TransformQvvs.identity;
            data.position = translation;
            return new TickedTransformBatchWriteCommand
            {
                aspect    = transform,
                writeType = WriteCommand.WriteType.WorldPositionDelta,
                writeData = data
            };
        }

        /// <summary>
        /// Creates a commands that applies a rotation delta in world-space
        /// </summary>
        /// <param name="transform">The TickedTransformAspect the command should apply to</param>
        /// <param name="rotation">The rotation delta to apply</param>
        /// <returns>The resulting command that can be applied later</returns>
        public static TickedTransformBatchWriteCommand RotateWorld(TickedTransformAspect transform, quaternion rotation)
        {
            var data      = TransformQvvs.identity;
            data.rotation = rotation;
            return new TickedTransformBatchWriteCommand
            {
                aspect    = transform,
                writeType = WriteCommand.WriteType.WorldRotationDelta,
                writeData = data
            };
        }

        /// <summary>
        /// Creates a commands that applies position and rotation deltas in world-space
        /// </summary>
        /// <param name="transform">The TickedTransformAspect the command should apply to</param>
        /// <param name="translation">The position delta to apply</param>
        /// <param name="rotation">The rotation delta to apply</param>
        /// <returns>The resulting command that can be applied later</returns>
        public static TickedTransformBatchWriteCommand TranslateRotateWorld(TickedTransformAspect transform, float3 translation, quaternion rotation)
        {
            var data      = TransformQvvs.identity;
            data.position = translation;
            data.rotation = rotation;
            return new TickedTransformBatchWriteCommand
            {
                aspect    = transform,
                writeType = WriteCommand.WriteType.WorldPositionRotationDelta,
                writeData = data
            };
        }

        /// <summary>
        /// Creates a commands that applies a uniform scale delta
        /// </summary>
        /// <param name="transform">The TickedTransformAspect the command should apply to</param>
        /// <param name="scale">The scale delta to apply</param>
        /// <returns>The resulting command that can be applied later</returns>
        public static TickedTransformBatchWriteCommand Scale(TickedTransformAspect transform, float scale)
        {
            var data   = TransformQvvs.identity;
            data.scale = scale;
            return new TickedTransformBatchWriteCommand
            {
                aspect    = transform,
                writeType = WriteCommand.WriteType.ScaleDelta,
                writeData = data
            };
        }

        /// <summary>
        /// Creates a commands that applies a position delta in local-space
        /// </summary>
        /// <param name="transform">The TickedTransformAspect the command should apply to</param>
        /// <param name="translation">The position delta to apply</param>
        /// <returns>The resulting command that can be applied later</returns>
        public static TickedTransformBatchWriteCommand TranslateLocal(TickedTransformAspect transform, float3 translation)
        {
            var data      = TransformQvvs.identity;
            data.position = translation;
            return new TickedTransformBatchWriteCommand
            {
                aspect    = transform,
                writeType = WriteCommand.WriteType.LocalPositionDelta,
                writeData = data
            };
        }

        /// <summary>
        /// Creates a commands that applies a rotation delta in local-space
        /// </summary>
        /// <param name="transform">The TickedTransformAspect the command should apply to</param>
        /// <param name="rotation">The rotation delta to apply</param>
        /// <returns>The resulting command that can be applied later</returns>
        public static TickedTransformBatchWriteCommand RotateLocal(TickedTransformAspect transform, quaternion rotation)
        {
            var data      = TransformQvvs.identity;
            data.rotation = rotation;
            return new TickedTransformBatchWriteCommand
            {
                aspect    = transform,
                writeType = WriteCommand.WriteType.LocalRotationDelta,
                writeData = data
            };
        }

        /// <summary>
        /// Creates a commands that applies position and rotation deltas in local-space
        /// </summary>
        /// <param name="transform">The TickedTransformAspect the command should apply to</param>
        /// <param name="translation">The position delta to apply</param>
        /// <param name="rotation">The rotation delta to apply</param>
        /// <returns>The resulting command that can be applied later</returns>
        public static TickedTransformBatchWriteCommand TranslateRotateLocal(TickedTransformAspect transform, float3 translation, quaternion rotation)
        {
            var data      = TransformQvvs.identity;
            data.position = translation;
            data.rotation = rotation;
            return new TickedTransformBatchWriteCommand
            {
                aspect    = transform,
                writeType = WriteCommand.WriteType.LocalPositionRotationDelta,
                writeData = data
            };
        }
    }

    public static class TickedTransformBatchWriteCommandExtensions
    {
        /// <summary>
        /// Applies a batch of commands. If there is a hierarchy, commands are applied in index order within the hierarchy.
        /// If two commands target the same entity, then the commands are applied in the order they show up in the list
        /// relative to each other.
        /// </summary>
        /// <param name="commands">An array of commands to apply</param>
        public static void ApplyTransforms(this NativeList<TickedTransformBatchWriteCommand> commands)
        {
            ApplyTransforms((ReadOnlySpan<TickedTransformBatchWriteCommand>)commands.AsReadOnly());
        }

        /// <summary>
        /// Applies a batch of commands. If there is a hierarchy, commands are applied in index order within the hierarchy.
        /// If two commands target the same entity, then the commands are applied in the order they show up in the array
        /// relative to each other.
        /// </summary>
        /// <param name="commands">An array of commands to apply</param>
        public static void ApplyTransforms(this NativeArray<TickedTransformBatchWriteCommand>.ReadOnly commands)
        {
            ApplyTransforms((ReadOnlySpan<TickedTransformBatchWriteCommand>)commands);
        }

        /// <summary>
        /// Applies a batch of commands. If there is a hierarchy, commands are applied in index order within the hierarchy.
        /// If two commands target the same entity, then the commands are applied in the order they show up in the array
        /// relative to each other.
        /// </summary>
        /// <param name="commands">An array of commands to apply</param>
        public static void ApplyTransforms(this NativeArray<TickedTransformBatchWriteCommand> commands)
        {
            ApplyTransforms((ReadOnlySpan<TickedTransformBatchWriteCommand>)commands);
        }

        /// <summary>
        /// Applies a batch of commands. If there is a hierarchy, commands are applied in index order within the hierarchy.
        /// If two commands target the same entity, then the commands are applied in the order they show up in the span
        /// relative to each other.
        /// </summary>
        /// <param name="commands">An array of commands to apply</param>
        public static void ApplyTransforms(this Span<TickedTransformBatchWriteCommand> commands)
        {
            ApplyTransforms((ReadOnlySpan<TickedTransformBatchWriteCommand>)commands);
        }

        /// <summary>
        /// Applies a batch of commands. If there is a hierarchy, commands are applied in index order within the hierarchy.
        /// If two commands target the same entity, then the commands are applied in the order they show up in the span
        /// relative to each other.
        /// </summary>
        /// <param name="commands">An array of commands to apply</param>
        public static void ApplyTransforms(this ReadOnlySpan<TickedTransformBatchWriteCommand> commands)
        {
            if (commands.Length == 0)
                return;

            bool fastPath    = true;
            var  firstAspect = commands[0].aspect;
            if (!firstAspect.entityInHierarchyHandle.isNull)
            {
                var previousIndex = -1;
                var hierarchy     = firstAspect.entityInHierarchyHandle.m_hierarchy;

                foreach (var command in commands)
                {
                    var handle = command.aspect.entityInHierarchyHandle;
                    if (handle.isNull || handle.m_hierarchy != hierarchy || handle.indexInHierarchy <= previousIndex)
                    {
                        fastPath = false;
                        break;
                    }
                    previousIndex = handle.indexInHierarchy;
                }
            }
            else
            {
                fastPath = false;
            }

            var tsa = ThreadStackAllocator.GetAllocator();
            if (fastPath)
            {
                ApplyHierarchyBatchTransformsWithoutChecks(commands, ref tsa);
            }
            else
            {
                var sortedCommands = tsa.AllocateAsSpan<TickedTransformBatchWriteCommand>(commands.Length);
                int dst            = 0;
                for (int src = 0; src < commands.Length; src++)
                {
                    var command = commands[src];
                    if (command.aspect.entityInHierarchyHandle.isNull)
                    {
                        ApplySoloCommand(command);
                        continue;
                    }
                    sortedCommands[dst]        = command;
                    sortedCommands[dst].sortId = dst;
                    dst++;
                }
                if (dst == 0)
                {
                    tsa.Dispose();
                    return;
                }

                sortedCommands = sortedCommands.Slice(0, dst);
                sortedCommands.Sort(new CommandSorter());

                int rangeStart = 0;
                var hierarchy  = sortedCommands[0].aspect.entityInHierarchyHandle.m_hierarchy;
                for (int i = 1; i <= sortedCommands.Length; i++)
                {
                    if (i == sortedCommands.Length || hierarchy != sortedCommands[i].aspect.entityInHierarchyHandle.m_hierarchy)
                    {
                        var commandSlice = sortedCommands.Slice(rangeStart, i - rangeStart);
                        var childTsa     = tsa.CreateChildAllocator();
                        ApplyHierarchyBatchTransformsWithoutChecks(commandSlice, ref childTsa);
                        childTsa.Dispose();
                        rangeStart = i;
                        if (i < sortedCommands.Length)
                            hierarchy = sortedCommands[i].aspect.entityInHierarchyHandle.m_hierarchy;
                    }
                }
            }
            tsa.Dispose();
        }

        static unsafe void ApplyHierarchyBatchTransformsWithoutChecks(ReadOnlySpan<TickedTransformBatchWriteCommand> commands, ref ThreadStackAllocator tsa)
        {
            var firstAspect = commands[0].aspect;
            var data        = tsa.AllocateAsSpan<TransformQvvs>(commands.Length);
            var ops         = tsa.AllocateAsSpan<WriteCommand>(commands.Length);
            int dst         = 0;
            for (int i = 0; i < commands.Length; i++)
            {
                if (commands[i].aspect.entityInHierarchyHandle.isRoot || !commands[i].aspect.entityInHierarchyHandle.inheritanceFlags.HasCopyParent())
                {
                    data[dst] = commands[i].writeData;
                    ops[dst]  = new WriteCommand
                    {
                        indexInHierarchy = commands[i].aspect.entityInHierarchyHandle.indexInHierarchy,
                        writeType        = commands[i].writeType,
                    };
                    dst++;
                }
            }
            if (dst == 0)
                return;
            data = data.Slice(0, dst);
            ops  = ops.Slice(0, dst);
            switch (firstAspect.m_accessType)
            {
                case TickedTransformAspect.AccessType.EntityManager:
                {
                    var access = new TransformTools.TickedEntityManagerAccess(*(EntityManager*)firstAspect.m_access);
                    WriteAndPropagate(firstAspect.entityInHierarchyHandle.m_hierarchy,
                                      firstAspect.entityInHierarchyHandle.m_extraHierarchy,
                                      data,
                                      ops,
                                      ref access,
                                      ref access);
                    break;
                }
                case TickedTransformAspect.AccessType.ComponentBroker:
                {
                    ref var access = ref TransformTools.TickedComponentBrokerAccess.From(ref *(ComponentBroker*)firstAspect.m_access);
                    WriteAndPropagate(firstAspect.entityInHierarchyHandle.m_hierarchy,
                                      firstAspect.entityInHierarchyHandle.m_extraHierarchy,
                                      data,
                                      ops,
                                      ref access,
                                      ref access);
                    break;
                }
                case TickedTransformAspect.AccessType.ComponentBrokerKeyed:
                {
                    ref var access = ref TransformTools.TickedComponentBrokerParallelAccess.From(ref *(ComponentBroker*)firstAspect.m_access);
                    WriteAndPropagate(firstAspect.entityInHierarchyHandle.m_hierarchy,
                                      firstAspect.entityInHierarchyHandle.m_extraHierarchy,
                                      data,
                                      ops,
                                      ref access,
                                      ref access);
                    break;
                }
                case TickedTransformAspect.AccessType.ComponentLookup:
                {
                    ref var access = ref TransformTools.LookupTickedWorldTransform.From(ref *(ComponentLookup<TickedWorldTransform>*)firstAspect.m_access);
                    ref var alive  = ref TransformTools.EsilAlive.From(ref firstAspect.m_esil);
                    WriteAndPropagate(firstAspect.entityInHierarchyHandle.m_hierarchy,
                                      firstAspect.entityInHierarchyHandle.m_extraHierarchy,
                                      data,
                                      ops,
                                      ref access,
                                      ref alive);
                    break;
                }
            }
        }

        static unsafe void ApplySoloCommand(TickedTransformBatchWriteCommand command)
        {
            ref var transform = ref command.aspect.m_worldTransform.ValueRW.worldTransform;
            switch (command.writeType)
            {
                case WriteCommand.WriteType.LocalPositionSet:
                case WriteCommand.WriteType.WorldPositionSet:
                    transform.position = command.writeData.position;
                    break;
                case WriteCommand.WriteType.LocalRotationSet:
                case WriteCommand.WriteType.WorldRotationSet:
                    transform.rotation = command.writeData.rotation;
                    break;
                case WriteCommand.WriteType.LocalScaleSet:
                case WriteCommand.WriteType.WorldScaleSet:
                    transform.scale = command.writeData.scale;
                    break;
                case WriteCommand.WriteType.StretchSet:
                    transform.stretch = command.writeData.stretch;
                    break;
                case WriteCommand.WriteType.LocalTransformSet:
                    transform.position = command.writeData.position;
                    transform.rotation = command.writeData.rotation;
                    transform.scale    = command.writeData.scale;
                    break;
                case WriteCommand.WriteType.LocalPositionRotationSet:
                case WriteCommand.WriteType.WorldPositionRotationSet:
                    transform.position = command.writeData.position;
                    transform.rotation = command.writeData.rotation;
                    break;
                case WriteCommand.WriteType.LocalTransformQvvsSet:
                case WriteCommand.WriteType.WorldTransformSet:
                    transform = command.writeData;
                    break;
                case WriteCommand.WriteType.LocalPositionDelta:
                case WriteCommand.WriteType.WorldPositionDelta:
                    transform.position += command.writeData.position;
                    break;
                case WriteCommand.WriteType.LocalRotationDelta:
                case WriteCommand.WriteType.WorldRotationDelta:
                    transform.rotation = math.normalize(math.mul(command.writeData.rotation, transform.rotation));
                    break;
                case WriteCommand.WriteType.LocalPositionRotationDelta:
                case WriteCommand.WriteType.WorldPositionRotationDelta:
                    transform.position += command.writeData.position;
                    transform.rotation  = math.normalize(math.mul(command.writeData.rotation, transform.rotation));
                    break;
                case WriteCommand.WriteType.ScaleDelta:
                    transform.scale *= command.writeData.scale;
                    break;
                case WriteCommand.WriteType.StretchDelta:
                    transform.stretch *= command.writeData.stretch;
                    break;
                case WriteCommand.WriteType.LocalTransformDelta:
                case WriteCommand.WriteType.WorldTransformDelta:
                    transform = qvvs.mulclean(in command.writeData, in transform);
                    break;
                case WriteCommand.WriteType.LocalInverseTransformDelta:
                case WriteCommand.WriteType.WorldInverseTransformDelta:
                    transform = qvvs.inversemulqvvsclean(in command.writeData, in transform);
                    break;
            }
        }

        struct CommandSorter : IComparer<TickedTransformBatchWriteCommand>
        {
            public unsafe int Compare(TickedTransformBatchWriteCommand x, TickedTransformBatchWriteCommand y)
            {
                var lptr = x.aspect.entityInHierarchyHandle.m_hierarchy.GetUnsafeReadOnlyPtr();
                var rptr = y.aspect.entityInHierarchyHandle.m_hierarchy.GetUnsafeReadOnlyPtr();
                if (lptr == rptr)
                {
                    var result = x.aspect.entityInHierarchyHandle.indexInHierarchy.CompareTo(y.aspect.entityInHierarchyHandle.indexInHierarchy);
                    if (result == 0)
                        return x.sortId.CompareTo(y.sortId);
                    return result;
                }
                return math.select(1, -1, lptr < rptr);
            }
        }
    }
}
#endif

