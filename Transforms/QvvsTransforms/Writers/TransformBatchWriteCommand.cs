#if !LATIOS_TRANSFORMS_UNITY
using System;
using System.Collections.Generic;
using Latios.Unsafe;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Transforms
{
    /// <summary>
    /// A struct describing a single write task for a single TransformAspect, which can be performed as part of a bulk operation
    /// for better performance.
    /// </summary>
    public struct TransformBatchWriteCommand
    {
        internal TransformAspect                                 aspect;
        internal TransformTools.Propagate.WriteCommand.WriteType writeType;
        internal TransformQvvs                                   writeData;

        /// <summary>
        /// Creates a command that sets the world transform
        /// </summary>
        /// <param name="transform">The TransformAspect the command should apply to</param>
        /// <param name="worldTransform">The new world transform to apply</param>
        /// <returns>The resulting command that can be applied later</returns>
        public static TransformBatchWriteCommand SetWorldTransform(TransformAspect transform, in TransformQvvs worldTransform)
        {
            return new TransformBatchWriteCommand
            {
                aspect    = transform,
                writeType = TransformTools.Propagate.WriteCommand.WriteType.WorldTransformSet,
                writeData = worldTransform
            };
        }

        /// <summary>
        /// Creates a command that sets the local transform including stretch and context32
        /// </summary>
        /// <param name="transform">The TransformAspect the command should apply to</param>
        /// <param name="localTransform">The new local transform to apply</param>
        /// <returns>The resulting command that can be applied later</returns>
        public static TransformBatchWriteCommand SetLocalTransformQvvs(TransformAspect transform, in TransformQvvs localTransform)
        {
            return new TransformBatchWriteCommand
            {
                aspect    = transform,
                writeType = TransformTools.Propagate.WriteCommand.WriteType.LocalTransformQvvsSet,
                writeData = localTransform
            };
        }

        /// <summary>
        /// Creates a command that sets the local transform
        /// </summary>
        /// <param name="transform">The TransformAspect the command should apply to</param>
        /// <param name="localTransform">The new local transform to apply</param>
        /// <returns>The resulting command that can be applied later</returns>
        public static TransformBatchWriteCommand SetLocalTransform(TransformAspect transform, in TransformQvs localTransform)
        {
            return new TransformBatchWriteCommand
            {
                aspect    = transform,
                writeType = TransformTools.Propagate.WriteCommand.WriteType.LocalTransformSet,
                writeData = new TransformQvvs(localTransform.position, localTransform.rotation, localTransform.scale, 1f)
            };
        }

        /// <summary>
        /// Creates a command that sets the local position
        /// </summary>
        /// <param name="transform">The TransformAspect the command should apply to</param>
        /// <param name="localPosition">The new local position to apply</param>
        /// <returns>The resulting command that can be applied later</returns>
        public static TransformBatchWriteCommand SetLocalPosition(TransformAspect transform, float3 localPosition)
        {
            var data      = TransformQvvs.identity;
            data.position = localPosition;
            return new TransformBatchWriteCommand
            {
                aspect    = transform,
                writeType = TransformTools.Propagate.WriteCommand.WriteType.LocalPositionSet,
                writeData = data
            };
        }

        /// <summary>
        /// Creates a command that sets the local rotation
        /// </summary>
        /// <param name="transform">The TransformAspect the command should apply to</param>
        /// <param name="localRotation">The new local rotation to apply</param>
        /// <returns>The resulting command that can be applied later</returns>
        public static TransformBatchWriteCommand SetLocalRotation(TransformAspect transform, quaternion localRotation)
        {
            var data      = TransformQvvs.identity;
            data.rotation = localRotation;
            return new TransformBatchWriteCommand
            {
                aspect    = transform,
                writeType = TransformTools.Propagate.WriteCommand.WriteType.LocalRotationSet,
                writeData = data
            };
        }

        /// <summary>
        /// Creates a command that sets the local scale
        /// </summary>
        /// <param name="transform">The TransformAspect the command should apply to</param>
        /// <param name="localScale">The new local scale to apply</param>
        /// <returns>The resulting command that can be applied later</returns>
        public static TransformBatchWriteCommand SetLocalScale(TransformAspect transform, float localScale)
        {
            var data   = TransformQvvs.identity;
            data.scale = localScale;
            return new TransformBatchWriteCommand
            {
                aspect    = transform,
                writeType = TransformTools.Propagate.WriteCommand.WriteType.LocalScaleSet,
                writeData = data
            };
        }

        /// <summary>
        /// Creates a command that sets the stretch
        /// </summary>
        /// <param name="transform">The TransformAspect the command should apply to</param>
        /// <param name="stretch">The new stretch to apply</param>
        /// <returns>The resulting command that can be applied later</returns>
        public static TransformBatchWriteCommand SetStretch(TransformAspect transform, float3 stretch)
        {
            var data     = TransformQvvs.identity;
            data.stretch = stretch;
            return new TransformBatchWriteCommand
            {
                aspect    = transform,
                writeType = TransformTools.Propagate.WriteCommand.WriteType.StretchSet,
                writeData = data
            };
        }
    }

    public static class TransformBatchWriteCommandExtensions
    {
        /// <summary>
        /// Applies a batch of commands.
        /// WARNING: This method is incomplete, and currently only supports the fast path. For the fast-path to work
        /// all commands must apply to the same hierarchy. In addition, only one command exists per entity, and the
        /// commands are ordered by the order of entities in the hierarchy. Update: This method will now sort
        /// unordered commands.
        /// </summary>
        /// <param name="commands">An array of commands to apply</param>
        /// <exception cref="System.NotSupportedException">Thrown if the commands do not satisfy the fast-path criteria</exception>
        public static void ApplyTransforms(this Span<TransformBatchWriteCommand> commands)
        {
            ApplyTransforms((ReadOnlySpan<TransformBatchWriteCommand>)commands);
        }

        /// <summary>
        /// Applies a batch of commands.
        /// WARNING: This method is incomplete, and currently only supports the fast path. For the fast-path to work
        /// all commands must apply to the same hierarchy. In addition, only one command exists per entity, and the
        /// commands are ordered by the order of entities in the hierarchy. Update: This method will now sort
        /// unordered commands.
        /// </summary>
        /// <param name="commands">An array of commands to apply</param>
        /// <exception cref="System.NotSupportedException">Thrown if the commands do not satisfy the fast-path criteria</exception>
        public static void ApplyTransforms(this ReadOnlySpan<TransformBatchWriteCommand> commands)
        {
            if (commands.Length == 0)
                return;

            bool fastPath    = true;
            bool sortPath    = true;
            var  firstAspect = commands[0].aspect;
            if (!firstAspect.entityInHierarchyHandle.isNull)
            {
                var previousIndex = -1;
                var hierarchy     = firstAspect.entityInHierarchyHandle.m_hierarchy;

                foreach (var command in commands)
                {
                    var handle = command.aspect.entityInHierarchyHandle;
                    if (handle.isNull || handle.m_hierarchy != hierarchy)
                    {
                        fastPath = false;
                        sortPath = false;
                        break;
                    }
                    if (handle.indexInHierarchy <= previousIndex)
                    {
                        fastPath = false;
                    }
                    previousIndex = handle.indexInHierarchy;
                }
            }

            var tsa = ThreadStackAllocator.GetAllocator();
            if (fastPath)
            {
                ApplyBatchTransformsWithoutChecks(commands, ref tsa);
            }
            else if (sortPath)
            {
                var sortedCommands = tsa.AllocateAsSpan<TransformBatchWriteCommand>(commands.Length);
                commands.CopyTo(sortedCommands);
                sortedCommands.Sort(new CommandSorter());
                int previousIndex = -1;
                foreach (var command in sortedCommands)
                {
                    if (previousIndex == command.aspect.entityInHierarchyHandle.indexInHierarchy)
                    {
                        sortPath = false;
                        break;
                    }
                    previousIndex = command.aspect.entityInHierarchyHandle.indexInHierarchy;
                }
                if (sortPath)
                {
                    ApplyBatchTransformsWithoutChecks(sortedCommands, ref tsa);
                }
            }
            if (!fastPath && !sortPath)
            {
                // Todo: We need to sort the commands by root entity by first appearance. Not sure how to do that deterministically and efficiently yet with TSA.
                // Then we need to merge commands when multiple writes are applied to the same target. We'll need to split writes if the commands conflict.
                // And finally, we need to apply batches by hierarchy.
                throw new System.NotSupportedException($"ApplyTransforms() is incomplete and only supports the fast-path. Refer to the method documentation for details.");
            }
            tsa.Dispose();
        }

        static unsafe void ApplyBatchTransformsWithoutChecks(ReadOnlySpan<TransformBatchWriteCommand> commands, ref ThreadStackAllocator tsa)
        {
            var firstAspect = commands[0].aspect;
            var data        = tsa.AllocateAsSpan<TransformQvvs>(commands.Length);
            var ops         = tsa.AllocateAsSpan<TransformTools.Propagate.WriteCommand>(commands.Length);
            for (int i = 0; i < commands.Length; i++)
            {
                data[i] = commands[i].writeData;
                ops[i]  = new TransformTools.Propagate.WriteCommand
                {
                    indexInHierarchy = commands[i].aspect.entityInHierarchyHandle.indexInHierarchy,
                    writeType        = commands[i].writeType,
                };
            }
            switch (firstAspect.m_accessType)
            {
                case TransformAspect.AccessType.EntityManager:
                {
                    var access = new TransformTools.EntityManagerAccess(*(EntityManager*)firstAspect.m_access);
                    TransformTools.Propagate.WriteAndPropagate(firstAspect.entityInHierarchyHandle.m_hierarchy,
                                                               firstAspect.entityInHierarchyHandle.m_extraHierarchy,
                                                               data,
                                                               ops,
                                                               ref access,
                                                               ref access);
                    break;
                }
                case TransformAspect.AccessType.ComponentBroker:
                {
                    ref var access = ref TransformTools.ComponentBrokerAccess.From(ref *(ComponentBroker*)firstAspect.m_access);
                    TransformTools.Propagate.WriteAndPropagate(firstAspect.entityInHierarchyHandle.m_hierarchy,
                                                               firstAspect.entityInHierarchyHandle.m_extraHierarchy,
                                                               data,
                                                               ops,
                                                               ref access,
                                                               ref access);
                    break;
                }
                case TransformAspect.AccessType.ComponentBrokerKeyed:
                {
                    ref var access = ref TransformTools.ComponentBrokerParallelAccess.From(ref *(ComponentBroker*)firstAspect.m_access);
                    TransformTools.Propagate.WriteAndPropagate(firstAspect.entityInHierarchyHandle.m_hierarchy,
                                                               firstAspect.entityInHierarchyHandle.m_extraHierarchy,
                                                               data,
                                                               ops,
                                                               ref access,
                                                               ref access);
                    break;
                }
                case TransformAspect.AccessType.ComponentLookup:
                {
                    ref var access = ref TransformTools.LookupWorldTransform.From(ref *(ComponentLookup<WorldTransform>*)firstAspect.m_access);
                    ref var alive  = ref TransformTools.EsilAlive.From(ref firstAspect.m_esil);
                    TransformTools.Propagate.WriteAndPropagate(firstAspect.entityInHierarchyHandle.m_hierarchy,
                                                               firstAspect.entityInHierarchyHandle.m_extraHierarchy,
                                                               data,
                                                               ops,
                                                               ref access,
                                                               ref alive);
                    break;
                }
            }
        }

        struct CommandSorter : IComparer<TransformBatchWriteCommand>
        {
            public int Compare(TransformBatchWriteCommand x, TransformBatchWriteCommand y)
            {
                return x.aspect.entityInHierarchyHandle.indexInHierarchy.CompareTo(y.aspect.entityInHierarchyHandle.indexInHierarchy);
            }
        }
    }
}
#endif

