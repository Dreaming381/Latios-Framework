#if !LATIOS_TRANSFORMS_UNITY
using System;
using Latios.Unsafe;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Transforms
{
    public static unsafe partial class TransformTools
    {
        internal static unsafe class Propagate
        {
            public struct WriteCommand
            {
                public enum WriteType
                {
                    LocalPositionSet,
                    LocalRotationSet,
                    LocalScaleSet,
                    LocalTransformSet,
                    StretchSet,
                    LocalTransformQvvsSet,
                    WorldPositionSet,
                    WorldRotationSet,
                    WorldScaleSet,
                    WorldTransformSet,
                    LocalPositionDelta,
                    LocalRotationDelta,
                    LocalTransformDelta,
                    LocalInverseTransformDelta,
                    ScaleDelta,
                    StretchDelta,
                    WorldPositionDelta,
                    WorldRotationDelta,
                    WorldTransformDelta,
                    WorldInverseTransformDelta,
                    CopyParentParentChanged
                }
                public int       indexInHierarchy;
                public WriteType writeType;
            }

            // Rules:
            // commands are sorted by index in hierarchy
            // commands are unique indices
            // commands only reference alive entities
            // Only commands that use CopyParentParentChanged are allowed when the inheritance flag is CopyParent
            // Multiple commands embedded within the first command's tree (or any previous command tree) is not yet supported
            public static void WriteAndPropagate<TWorld, TAlive>(NativeArray<EntityInHierarchy> hierarchy,
                                                                 EntityInHierarchy*             extraHierarchy,
                                                                 ReadOnlySpan<TransformQvvs>    commandTransformParts,
                                                                 ReadOnlySpan<WriteCommand>     commands,
                                                                 ref TWorld transformLookup,
                                                                 ref TAlive aliveLookup) where TWorld : unmanaged, IWorldTransform where TAlive : unmanaged,
            IAlive
            {
                var tsa = ThreadStackAllocator.GetAllocator();

                var maxEntitiesToProcess   = hierarchy.Length - commands[0].indexInHierarchy;
                var oldNewTransforms       = tsa.AllocateAsSpan<OldNewWorldTransform>(maxEntitiesToProcess);
                var instructions           = tsa.AllocateAsSpan<PropagationInstruction>(maxEntitiesToProcess);
                int oldNewTransformsLength = 0;
                int instructionsLength     = 0;
                int commandsRead           = 0;
                for (int instructionsRead = 0; instructionsRead < instructionsLength || commandsRead < commands.Length; instructionsRead++)
                {
                    EntityInHierarchy    entityInHierarchyToPropagate = default;
                    OldNewWorldTransform changedTransform             = default;
                    int                  aliveAncestorToChildrenIndex = 0;
                    bool                 dead                         = false;
                    bool                 hasCommandsRemaining         = commandsRead < commands.Length;
                    bool                 hasInstructionsRemaining     = instructionsRead < instructionsLength;
                    if (hasInstructionsRemaining)
                        entityInHierarchyToPropagate = hierarchy[instructions[instructionsRead].indexInHierarchy];
                    if (hasCommandsRemaining && (!hasInstructionsRemaining || commands[commandsRead].indexInHierarchy <= instructions[instructionsRead].indexInHierarchy))
                    {
                        // Next up is a command we need to write
                        var command                  = commands[commandsRead];
                        entityInHierarchyToPropagate = hierarchy[command.indexInHierarchy];
                        aliveAncestorToChildrenIndex = command.indexInHierarchy;

                        if (!hasInstructionsRemaining || command.indexInHierarchy < instructions[instructionsRead].indexInHierarchy)
                        {
                            // We aren't inheriting any changes from parents. This is a modification only.
                            if (command.indexInHierarchy == 0)
                            {
                                changedTransform = ComputeCommandTransformRoot(command,
                                                                               in commandTransformParts[commandsRead],
                                                                               entityInHierarchyToPropagate.entity,
                                                                               ref transformLookup);
                            }
                            else
                            {
                                changedTransform = ComputeCommandTransformChild(command, in commandTransformParts[commandsRead], new EntityInHierarchyHandle
                                {
                                    m_hierarchy      = hierarchy,
                                    m_extraHierarchy = extraHierarchy,
                                    m_index          = command.indexInHierarchy
                                }, ref transformLookup, ref aliveLookup);
                            }
                            // Don't advance the instruction reader
                            instructionsRead--;
                        }
                        else
                        {
                            // We are writing to a transform, but there's also propagations coming.
                            var instruction = instructions[instructionsRead];
                            var handle      = new EntityInHierarchyHandle
                            {
                                m_hierarchy      = hierarchy,
                                m_extraHierarchy = extraHierarchy,
                                m_index          = instruction.indexInHierarchy,
                            };
                            entityInHierarchyToPropagate = hierarchy[instruction.indexInHierarchy];
                            var flags                    = entityInHierarchyToPropagate.m_flags.HasCopyParent() &&
                                                           instruction.useOverrideFlagsForCopyParent ? instruction.overrideFlagsForCopyParent : entityInHierarchyToPropagate.m_flags;
                            var parentOldNewTransforms = oldNewTransforms[instruction.ancestorOldNewTransformIndex];
                            if (entityInHierarchyToPropagate.m_flags.HasCopyParent())
                            {
                                changedTransform = ComputePropagatedTransform(in parentOldNewTransforms,
                                                                              flags,
                                                                              handle,
                                                                              instruction.ancestorIndexInHierarchy,
                                                                              ref transformLookup);
                            }
                            else
                            {
                                var localChangedTransform = ComputePropagatedTransform(in parentOldNewTransforms,
                                                                                       flags,
                                                                                       handle,
                                                                                       instruction.ancestorIndexInHierarchy,
                                                                                       ref transformLookup);
                                changedTransform = ComputeCommandTransform(command,
                                                                           commandTransformParts[commandsRead],
                                                                           handle,
                                                                           in parentOldNewTransforms.newTransform,
                                                                           instruction.ancestorIndexInHierarchy,
                                                                           ref transformLookup);

                                changedTransform.oldTransform = localChangedTransform.oldTransform;
                            }
                        }
                        commandsRead++;
                    }
                    else if (!transformLookup.HasWorldTransform(entityInHierarchyToPropagate.entity))
                    {
                        // This entity only has one of either WorldTransform or TickedWorldTransform, and we are propagating the other type.
                        entityInHierarchyToPropagate.m_childCount = 0;
                    }
                    else if (!aliveLookup.IsAlive(entityInHierarchyToPropagate.entity))
                    {
                        // The current handle is dead. If its parent is also dead, we need to propagate the override flags from the parent.
                        dead                         = true;
                        var instruction              = instructions[instructionsRead];
                        changedTransform             = oldNewTransforms[instruction.ancestorOldNewTransformIndex];
                        entityInHierarchyToPropagate = hierarchy[instruction.indexInHierarchy];
                        aliveAncestorToChildrenIndex = instruction.ancestorIndexInHierarchy;
                        if (instruction.useOverrideFlagsForCopyParent)
                            entityInHierarchyToPropagate.m_flags = instruction.overrideFlagsForCopyParent;
                    }
                    else
                    {
                        // This is a normal handle that needs to receive propagations.
                        var instruction = instructions[instructionsRead];
                        var handle      = new EntityInHierarchyHandle
                        {
                            m_hierarchy      = hierarchy,
                            m_extraHierarchy = extraHierarchy,
                            m_index          = instruction.indexInHierarchy,
                        };
                        entityInHierarchyToPropagate = hierarchy[instruction.indexInHierarchy];
                        aliveAncestorToChildrenIndex = instruction.indexInHierarchy;
                        var flags                    = entityInHierarchyToPropagate.m_flags.HasCopyParent() &&
                                                       instruction.useOverrideFlagsForCopyParent ? instruction.overrideFlagsForCopyParent : entityInHierarchyToPropagate.m_flags;
                        changedTransform = ComputePropagatedTransform(in oldNewTransforms[instruction.ancestorOldNewTransformIndex],
                                                                      flags,
                                                                      handle,
                                                                      instruction.ancestorIndexInHierarchy,
                                                                      ref transformLookup);
                    }

                    if (entityInHierarchyToPropagate.childCount != 0 && !changedTransform.newTransform.Equals(changedTransform.oldTransform))
                    {
                        // Add children to the propagation list
                        oldNewTransforms[oldNewTransformsLength] = changedTransform;
                        var newInstruction                       = new PropagationInstruction
                        {
                            ancestorOldNewTransformIndex  = oldNewTransformsLength,
                            overrideFlagsForCopyParent    = entityInHierarchyToPropagate.m_flags,
                            useOverrideFlagsForCopyParent = dead,
                            indexInHierarchy              = entityInHierarchyToPropagate.firstChildIndex,
                            ancestorIndexInHierarchy      = aliveAncestorToChildrenIndex
                        };
                        oldNewTransformsLength++;

                        for (int i = 0; i < entityInHierarchyToPropagate.childCount; i++)
                        {
                            instructions[instructionsLength] = newInstruction;
                            instructionsLength++;
                            newInstruction.indexInHierarchy++;
                        }
                    }
                }

                tsa.Dispose();
            }

            struct OldNewWorldTransform
            {
                public TransformQvvs oldTransform;
                public TransformQvvs newTransform;
            }

            struct PropagationInstruction
            {
                public int              indexInHierarchy;
                public int              ancestorIndexInHierarchy;
                public int              ancestorOldNewTransformIndex;
                public InheritanceFlags overrideFlagsForCopyParent;
                public bool             useOverrideFlagsForCopyParent;
            }

            static OldNewWorldTransform ComputeCommandTransformRoot<TWorld>(WriteCommand command, in TransformQvvs writeData, Entity root,
                                                                            ref TWorld transformLookup) where TWorld : unmanaged, IWorldTransform
            {
                ref var transform            = ref transformLookup.GetWorldTransformRefRW(root).ValueRW.worldTransform;
                var     oldNewWorldTransform = new OldNewWorldTransform { oldTransform = transform };
                switch (command.writeType)
                {
                    case WriteCommand.WriteType.LocalPositionSet:
                    case WriteCommand.WriteType.WorldPositionSet:
                        transform.position = writeData.position;
                        break;
                    case WriteCommand.WriteType.LocalRotationSet:
                    case WriteCommand.WriteType.WorldRotationSet:
                        transform.rotation = writeData.rotation;
                        break;
                    case WriteCommand.WriteType.LocalScaleSet:
                    case WriteCommand.WriteType.WorldScaleSet:
                        transform.scale = writeData.scale;
                        break;
                    case WriteCommand.WriteType.StretchSet:
                        transform.stretch = writeData.stretch;
                        break;
                    case WriteCommand.WriteType.LocalTransformSet:
                        transform.position = writeData.position;
                        transform.rotation = writeData.rotation;
                        transform.scale    = writeData.scale;
                        break;
                    case WriteCommand.WriteType.LocalTransformQvvsSet:
                    case WriteCommand.WriteType.WorldTransformSet:
                        transform = writeData;
                        break;
                    case WriteCommand.WriteType.LocalPositionDelta:
                    case WriteCommand.WriteType.WorldPositionDelta:
                        transform.position += writeData.position;
                        break;
                    case WriteCommand.WriteType.LocalRotationDelta:
                    case WriteCommand.WriteType.WorldRotationDelta:
                        transform.rotation = math.normalize(math.mul(writeData.rotation, transform.rotation));
                        break;
                    case WriteCommand.WriteType.ScaleDelta:
                        transform.scale *= writeData.scale;
                        break;
                    case WriteCommand.WriteType.StretchDelta:
                        transform.stretch *= writeData.stretch;
                        break;
                    case WriteCommand.WriteType.LocalTransformDelta:
                    case WriteCommand.WriteType.WorldTransformDelta:
                        transform = qvvs.mulclean(in writeData, in transform);
                        break;
                    case WriteCommand.WriteType.LocalInverseTransformDelta:
                    case WriteCommand.WriteType.WorldInverseTransformDelta:
                        transform = qvvs.inversemulqvvsclean(in writeData, in transform);
                        break;
                }
                oldNewWorldTransform.newTransform = transform;
                return oldNewWorldTransform;
            }

            static OldNewWorldTransform ComputeCommandTransformChild<TWorld, TAlive>(WriteCommand command,
                                                                                     in TransformQvvs writeData,
                                                                                     in EntityInHierarchyHandle handle,
                                                                                     ref TWorld transformLookup,
                                                                                     ref TAlive aliveLookup) where TWorld : unmanaged,
            IWorldTransform where TAlive : unmanaged, IAlive
            {
                ref var worldTransform       = ref transformLookup.GetWorldTransformRefRW(handle.entity).ValueRW;
                ref var transform            = ref worldTransform.worldTransform;
                var     oldNewWorldTransform = new OldNewWorldTransform { oldTransform = transform };
                switch (command.writeType)
                {
                    case WriteCommand.WriteType.LocalPositionSet:
                    {
                        var parentTransform = ParentTransformFrom(handle, ref aliveLookup, ref transformLookup, out _);
                        WorldLocalOps.SetLocalPosition(writeData.position, in parentTransform, ref transform, handle, transformLookup.isTicked);
                        break;
                    }
                    case WriteCommand.WriteType.LocalRotationSet:
                    {
                        var parentTransform = ParentTransformFrom(handle, ref aliveLookup, ref transformLookup, out _);
                        WorldLocalOps.SetLocalRotation(writeData.rotation, in parentTransform, ref transform);
                        break;
                    }
                    case WriteCommand.WriteType.LocalScaleSet:
                    {
                        var parentTransform = ParentTransformFrom(handle, ref aliveLookup, ref transformLookup, out _);
                        WorldLocalOps.SetLocalScale(writeData.scale, in parentTransform, ref transform, in handle, transformLookup.isTicked);
                        break;
                    }
                    case WriteCommand.WriteType.LocalTransformSet:
                    {
                        var parentTransform = ParentTransformFrom(handle, ref aliveLookup, ref transformLookup, out _);
                        WorldLocalOps.SetLocalTransform(new TransformQvs(writeData.position, writeData.rotation, writeData.scale),
                                                        in parentTransform,
                                                        ref transform,
                                                        in handle,
                                                        transformLookup.isTicked);
                        break;
                    }
                    case WriteCommand.WriteType.StretchSet:
                        WorldLocalOps.SetStretch(writeData.stretch, ref transform);
                        break;
                    case WriteCommand.WriteType.LocalTransformQvvsSet:
                    {
                        var parentTransform = ParentTransformFrom(handle, ref aliveLookup, ref transformLookup, out _);
                        WorldLocalOps.SetLocalTransformQvvs(in writeData,
                                                            in parentTransform,
                                                            ref transform,
                                                            in handle,
                                                            transformLookup.isTicked);
                        break;
                    }
                    case WriteCommand.WriteType.WorldPositionSet:
                    {
                        var parentTransform = ParentTransformFrom(handle, ref aliveLookup, ref transformLookup, out _);
                        WorldLocalOps.SetWorldPosition(writeData.position, in parentTransform, ref transform, in handle, transformLookup.isTicked);
                        break;
                    }
                    case WriteCommand.WriteType.WorldRotationSet:
                        WorldLocalOps.SetWorldRotation(writeData.rotation, ref transform);
                        break;
                    case WriteCommand.WriteType.WorldScaleSet:
                    {
                        var parentTransform = ParentTransformFrom(handle, ref aliveLookup, ref transformLookup, out _);
                        WorldLocalOps.SetWorldScale(writeData.scale, in parentTransform, ref transform, in handle, transformLookup.isTicked);
                        break;
                    }
                    case WriteCommand.WriteType.WorldTransformSet:
                    {
                        var parentTransform = ParentTransformFrom(handle, ref aliveLookup, ref transformLookup, out _);
                        WorldLocalOps.SetWorldTransform(writeData, in parentTransform, ref transform, in handle, transformLookup.isTicked);
                        break;
                    }
                    case WriteCommand.WriteType.LocalPositionDelta:
                    {
                        var parentTransform = ParentTransformFrom(handle, ref aliveLookup, ref transformLookup, out var parentHandle);
                        WorldLocalOps.TranslateLocal(writeData.position, in parentTransform, ref transform, in parentHandle, in handle, transformLookup.isTicked);
                        break;
                    }
                    case WriteCommand.WriteType.LocalRotationDelta:
                    {
                        var parentTransform = ParentTransformFrom(handle, ref aliveLookup, ref transformLookup, out _);
                        WorldLocalOps.RotateLocal(writeData.rotation, in parentTransform, ref transform);
                        break;
                    }
                    case WriteCommand.WriteType.LocalTransformDelta:
                    {
                        var parentTransform = ParentTransformFrom(handle, ref aliveLookup, ref transformLookup, out var parentHandle);
                        WorldLocalOps.TransformLocal(in writeData, in parentTransform, ref transform, in parentHandle, in handle, transformLookup.isTicked);
                        break;
                    }
                    case WriteCommand.WriteType.LocalInverseTransformDelta:
                    {
                        var parentTransform = ParentTransformFrom(handle, ref aliveLookup, ref transformLookup, out var parentHandle);
                        WorldLocalOps.InverseTransformLocal(in writeData, in parentTransform, ref transform, in parentHandle, in handle, transformLookup.isTicked);
                        break;
                    }
                    case WriteCommand.WriteType.StretchDelta:
                        WorldLocalOps.StretchStretch(writeData.stretch, ref transform);
                        break;
                    case WriteCommand.WriteType.WorldPositionDelta:
                    {
                        var parentTransform = ParentTransformFrom(handle, ref aliveLookup, ref transformLookup, out _);
                        WorldLocalOps.TranslateWorld(writeData.position, in parentTransform, ref transform, in handle, transformLookup.isTicked);
                        break;
                    }
                    case WriteCommand.WriteType.WorldRotationDelta:
                        WorldLocalOps.RotateWorld(writeData.rotation, ref transform);
                        break;
                    case WriteCommand.WriteType.ScaleDelta:
                        WorldLocalOps.ScaleScale(writeData.scale, ref transform, in handle, transformLookup.isTicked);
                        break;
                    case WriteCommand.WriteType.WorldTransformDelta:
                    {
                        var parentTransform = ParentTransformFrom(handle, ref aliveLookup, ref transformLookup, out var parentHandle);
                        WorldLocalOps.TransformWorld(in writeData, in parentTransform, ref transform, in parentHandle, in handle, transformLookup.isTicked);
                        break;
                    }
                    case WriteCommand.WriteType.WorldInverseTransformDelta:
                    {
                        var parentTransform = ParentTransformFrom(handle, ref aliveLookup, ref transformLookup, out var parentHandle);
                        WorldLocalOps.InverseTransformWorld(in writeData, in parentTransform, ref transform, in parentHandle, in handle, transformLookup.isTicked);
                        break;
                    }
                    case WriteCommand.WriteType.CopyParentParentChanged:
                    {
                        var parentTransform = ParentTransformFrom(handle, ref aliveLookup, ref transformLookup, out _);
                        WorldLocalOps.TranslateWorld(writeData.position, in parentTransform, ref transform, in handle, transformLookup.isTicked);
                        break;
                    }
                }
                oldNewWorldTransform.newTransform = transform;
                return oldNewWorldTransform;
            }

            static OldNewWorldTransform ComputeCommandTransform<T>(WriteCommand command,
                                                                   TransformQvvs writeData,
                                                                   EntityInHierarchyHandle handle,
                                                                   in TransformQvvs parentTransform,
                                                                   int parentIndex,
                                                                   ref T transformLookup) where T : unmanaged, IWorldTransform
            {
                ref var transform            = ref transformLookup.GetWorldTransformRefRW(handle.entity).ValueRW.worldTransform;
                var     oldNewWorldTransform = new OldNewWorldTransform { oldTransform = transform };
                var     parentHandle         = handle.GetFromIndexInHierarchy(parentIndex);
                switch (command.writeType)
                {
                    case WriteCommand.WriteType.LocalPositionSet:
                        WorldLocalOps.SetLocalPosition(writeData.position, in parentTransform, ref transform, handle, transformLookup.isTicked);
                        break;
                    case WriteCommand.WriteType.LocalRotationSet:
                        WorldLocalOps.SetLocalRotation(writeData.rotation, in parentTransform, ref transform);
                        break;
                    case WriteCommand.WriteType.LocalScaleSet:
                        WorldLocalOps.SetLocalScale(writeData.scale, in parentTransform, ref transform, in handle, transformLookup.isTicked);
                        break;
                    case WriteCommand.WriteType.LocalTransformSet:
                        WorldLocalOps.SetLocalTransform(new TransformQvs(writeData.position, writeData.rotation, writeData.scale),
                                                        in parentTransform, ref transform, in handle, transformLookup.isTicked);
                        break;
                    case WriteCommand.WriteType.StretchSet:
                        WorldLocalOps.SetStretch(writeData.stretch, ref transform);
                        break;
                    case WriteCommand.WriteType.LocalTransformQvvsSet:
                        WorldLocalOps.SetLocalTransformQvvs(in writeData, in parentTransform, ref transform, in handle, transformLookup.isTicked);
                        break;
                    case WriteCommand.WriteType.WorldPositionSet:
                        WorldLocalOps.SetWorldPosition(writeData.position, in parentTransform, ref transform, in handle, transformLookup.isTicked);
                        break;
                    case WriteCommand.WriteType.WorldRotationSet:
                        WorldLocalOps.SetWorldRotation(writeData.rotation, ref transform);
                        break;
                    case WriteCommand.WriteType.WorldScaleSet:
                        WorldLocalOps.SetWorldScale(writeData.scale, in parentTransform, ref transform, in handle, transformLookup.isTicked);
                        break;
                    case WriteCommand.WriteType.WorldTransformSet:
                        WorldLocalOps.SetWorldTransform(writeData, in parentTransform, ref transform, in handle, transformLookup.isTicked);
                        break;
                    case WriteCommand.WriteType.LocalPositionDelta:
                        WorldLocalOps.TranslateLocal(writeData.position, in parentTransform, ref transform, in parentHandle, in handle, transformLookup.isTicked);
                        break;
                    case WriteCommand.WriteType.LocalRotationDelta:
                        WorldLocalOps.RotateLocal(writeData.rotation, in parentTransform, ref transform);
                        break;
                    case WriteCommand.WriteType.LocalTransformDelta:
                        WorldLocalOps.TransformLocal(in writeData, in parentTransform, ref transform, in parentHandle, in handle, transformLookup.isTicked);
                        break;
                    case WriteCommand.WriteType.LocalInverseTransformDelta:
                        WorldLocalOps.InverseTransformLocal(in writeData, in parentTransform, ref transform, in parentHandle, in handle, transformLookup.isTicked);
                        break;
                    case WriteCommand.WriteType.StretchDelta:
                        WorldLocalOps.StretchStretch(writeData.stretch, ref transform);
                        break;
                    case WriteCommand.WriteType.WorldPositionDelta:
                        WorldLocalOps.TranslateWorld(writeData.position, in parentTransform, ref transform, in handle, transformLookup.isTicked);
                        break;
                    case WriteCommand.WriteType.WorldRotationDelta:
                        WorldLocalOps.RotateWorld(writeData.rotation, ref transform);
                        break;
                    case WriteCommand.WriteType.ScaleDelta:
                        WorldLocalOps.ScaleScale(writeData.scale, ref transform, in handle, transformLookup.isTicked);
                        break;
                    case WriteCommand.WriteType.WorldTransformDelta:
                        WorldLocalOps.TransformWorld(in writeData, in parentTransform, ref transform, in parentHandle, in handle, transformLookup.isTicked);
                        break;
                    case WriteCommand.WriteType.WorldInverseTransformDelta:
                        WorldLocalOps.InverseTransformWorld(in writeData, in parentTransform, ref transform, in parentHandle, in handle, transformLookup.isTicked);
                        break;
                    case WriteCommand.WriteType.CopyParentParentChanged:
                        WorldLocalOps.TranslateWorld(writeData.position, in parentTransform, ref transform, in handle, transformLookup.isTicked);
                        break;
                }
                oldNewWorldTransform.newTransform = transform;
                return oldNewWorldTransform;
            }

            static OldNewWorldTransform ComputePropagatedTransform<T>(in OldNewWorldTransform oldNewWorldTransform, InheritanceFlags flags, EntityInHierarchyHandle handle,
                                                                      int parentIndex, ref T transformLookup) where T : unmanaged, IWorldTransform
            {
                ref var transform    = ref transformLookup.GetWorldTransformRefRW(handle.entity).ValueRW.worldTransform;
                var     parentHandle = handle.GetFromIndexInHierarchy(parentIndex);
                var     result       = new OldNewWorldTransform { oldTransform = transform };
                WorldLocalOps.PropagateTransform(in oldNewWorldTransform.newTransform,
                                                 in oldNewWorldTransform.oldTransform,
                                                 ref transform,
                                                 in parentHandle,
                                                 in handle,
                                                 transformLookup.isTicked);
                result.newTransform = transform;
                return result;
            }

            static quaternion ComputeMixedRotation(quaternion originalWorldRotation, quaternion hierarchyWorldRotation, InheritanceFlags flags)
            {
                var forward = math.select(math.forward(hierarchyWorldRotation),
                                          math.forward(originalWorldRotation),
                                          (flags & InheritanceFlags.WorldForward) == InheritanceFlags.WorldForward);
                var up = math.select(math.rotate(hierarchyWorldRotation, math.up()),
                                     math.rotate(originalWorldRotation, math.up()),
                                     (flags & InheritanceFlags.WorldUp) == InheritanceFlags.WorldUp);

                if ((flags & InheritanceFlags.StrictUp) == InheritanceFlags.StrictUp)
                {
                    float3 right = math.normalizesafe(math.cross(up, forward), float3.zero);
                    if (right.Equals(float3.zero))
                        return math.select(hierarchyWorldRotation.value, originalWorldRotation.value, (flags & InheritanceFlags.WorldUp) == InheritanceFlags.WorldUp);
                    var newForward = math.cross(right, up);
                    return new quaternion(new float3x3(right, up, newForward));
                }
                else
                {
                    float3 right = math.normalizesafe(math.cross(up, forward), float3.zero);
                    if (right.Equals(float3.zero))
                        return math.select(hierarchyWorldRotation.value,
                                           originalWorldRotation.value,
                                           (flags & InheritanceFlags.WorldForward) == InheritanceFlags.WorldForward);
                    var newUp = math.cross(forward, right);
                    return new quaternion(new float3x3(right, newUp, forward));
                }
            }
        }
    }
}
#endif

