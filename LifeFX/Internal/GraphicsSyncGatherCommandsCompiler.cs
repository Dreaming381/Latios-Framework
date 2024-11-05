using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.LifeFX
{
    internal struct GraphicsSyncCopyOp
    {
        public enum OpType : byte
        {
            ByteRange,
            BitRange,
            SyncExist,
            ComponentExist,
            ComponentEnabled
        }

        public byte   indexInTypeHandles;
        public OpType opType;
        public short  dstStart;
        public short  srcStart;
        public short  count;
    }

    internal struct GraphicsSyncInstructionsForType
    {
        public bool                          isEnableable;
        public bool                          requiresOrderVersionCheck;
        public short                         typeSize;
        public short                         stateFieldOffset;
        public short                         bufferElementTypeSize;
        public BlobArray<TypeIndex>          changeFilterTypes;
        public BlobArray<GraphicsSyncCopyOp> copyOps;
        public UnityObjectRef<ComputeShader> uploadShader;
        public int                           shaderPropertyID;
    }

    internal struct GraphicsSyncInstructionsBlob
    {
        public BlobArray<GraphicsSyncInstructionsForType> instructionsByType;
        public BlobArray<TypeIndex>                       typeIndicesForTypeHandles;
    }

    internal static class GraphicsSyncGatherCommandsCompiler
    {
        static BlobAssetReference<GraphicsSyncInstructionsBlob> s_blob = default;
        static List<ComputeShader>                              s_computeShaders;  // GC defeat

        public static void Init()
        {
            if (s_blob.IsCreated)
                return;

            CompileAllTypes();

            // important: this will always be called from a special unload thread (main thread will be blocking on this)
            AppDomain.CurrentDomain.DomainUnload += (_, __) => { Shutdown(); };

            // There is no domain unload in player builds, so we must be sure to shutdown when the process exits.
            AppDomain.CurrentDomain.ProcessExit += (_, __) => { Shutdown(); };
        }

        static void Shutdown()
        {
            if (s_blob.IsCreated)
                s_blob.Dispose();
            s_computeShaders.Clear();
            s_computeShaders = null;
        }

        static void CompileAllTypes()
        {
            var typesToCompile = new UnsafeList<TypeIndex>(128, Allocator.Temp);
            var baseInterface  = typeof(IGraphicsSyncComponentBase);

            foreach (var componentType in TypeManager.AllTypes)
            {
                if (componentType.IsZeroSized || componentType.BakingOnlyType || componentType.TemporaryBakingType)
                    continue;
                var typeIndex = componentType.TypeIndex;
                if (!typeIndex.IsComponentType || typeIndex.IsManagedType)
                    continue;

                var type = componentType.Type;
                if (!baseInterface.IsAssignableFrom(type))
                    continue;

                typesToCompile.Add(typeIndex);
            }

            if (typesToCompile.Length > 255)
                throw new System.InvalidOperationException("There are too many IGraphicsSyncComponent<> types in the project.");

            s_computeShaders = new List<ComputeShader>();

            BlobBuilder builder            = new BlobBuilder(Allocator.Temp);
            ref var     root               = ref builder.ConstructRoot<GraphicsSyncInstructionsBlob>();
            var         instructionsByType = builder.Allocate(ref root.instructionsByType, typesToCompile.Length);

            var  typeIndicesToHandleIndices = new NativeHashMap<TypeIndex, byte>(256, Allocator.Temp);
            byte counter                    = 0;
            foreach (var typeIndex in typesToCompile)
            {
                typeIndicesToHandleIndices.Add(typeIndex, counter);
                counter++;
            }

            for (counter = 0; counter < typesToCompile.Length; counter++)
                CompileType(ref instructionsByType[counter], typesToCompile[counter], typeIndicesToHandleIndices);

            var handles = builder.Allocate(ref root.typeIndicesForTypeHandles, typeIndicesToHandleIndices.Count);
            foreach (var indices in typeIndicesToHandleIndices)
                handles[indices.Value] = indices.Key;

            s_blob = builder.CreateBlobAssetReference<GraphicsSyncInstructionsBlob>(Allocator.Persistent);
        }

        static GraphicsSyncGatherRegistration s_registration = new GraphicsSyncGatherRegistration();

        static unsafe void CompileType(ref GraphicsSyncInstructionsForType instructionsForType, TypeIndex type, NativeHashMap<TypeIndex, byte> typeIndicesToHandleIndices)
        {
            s_registration.bitPackRanges    = new UnsafeList<GraphicsSyncGatherRegistration.BitPackRange>(4, Allocator.Temp);
            s_registration.assignmentRanges = new UnsafeList<GraphicsSyncGatherRegistration.AssignmentRange>(8, Allocator.Temp);
            var ops                         = new UnsafeList<GraphicsSyncCopyOp>(32, Allocator.Temp);

            var  boxed                 = Activator.CreateInstance(TypeManager.GetTypeInfo(type).Type) as IGraphicsSyncComponentBase;
            var  sizeAndAlignment      = boxed.GetTypeSizeAndAlignmentBase();
            var  elementBytes          = new Span<byte>(AllocatorManager.Allocate(Allocator.Temp, sizeAndAlignment.x, sizeAndAlignment.y), sizeAndAlignment.x);
            bool needsMoreRegistration = true;
            int  passIndex             = 0;
            while (needsMoreRegistration)
            {
                elementBytes.Clear();
                s_registration.nextByte = 1;
                s_registration.bitPackRanges.Clear();
                s_registration.assignmentRanges.Clear();
                needsMoreRegistration = boxed.RegisterBase(s_registration, ref elementBytes, passIndex);

                for (int i = 0; i < elementBytes.Length; i++)
                {
                    var assignedByte = elementBytes[i];
                    if (assignedByte == 0)
                        continue;

                    var assignmentRange = FindRange(s_registration, assignedByte, i);
                    if (assignmentRange.bitPackCount == 0)
                    {
                        ops.Add(new GraphicsSyncCopyOp
                        {
                            srcStart           = (short)(assignedByte - assignmentRange.start),
                            dstStart           = (short)i,
                            count              = 1,
                            indexInTypeHandles = GetHandleIndexForType(assignmentRange.type, typeIndicesToHandleIndices),
                            opType             = GraphicsSyncCopyOp.OpType.ByteRange
                        });
                    }
                    else
                    {
                        var dstBitStart     = i * 8;
                        var bitPackBitStart = 8 * (assignedByte - assignmentRange.start);
                        for (int j = 0; j < assignmentRange.bitPackCount; j++)
                        {
                            var packData = s_registration.bitPackRanges[j];
                            if (packData.packBitStart >= bitPackBitStart + 8 || packData.packBitStart + packData.componentBitCount < bitPackBitStart)
                                continue;

                            if (packData.isExistBit)
                            {
                                ops.Add(new GraphicsSyncCopyOp
                                {
                                    count              = 1,
                                    dstStart           = (short)(packData.packBitStart - bitPackBitStart + dstBitStart),
                                    srcStart           = 0,
                                    indexInTypeHandles = 0,
                                    opType             = GraphicsSyncCopyOp.OpType.SyncExist
                                });
                            }
                            else if (packData.isEnabledBit)
                            {
                                ops.Add(new GraphicsSyncCopyOp
                                {
                                    count              = 1,
                                    dstStart           = (short)(packData.packBitStart - bitPackBitStart + dstBitStart),
                                    srcStart           = 0,
                                    indexInTypeHandles = GetHandleIndexForType(packData.type, typeIndicesToHandleIndices),
                                    opType             = GraphicsSyncCopyOp.OpType.ComponentEnabled
                                });
                            }
                            else if (packData.isHasComponentBit)
                            {
                                ops.Add(new GraphicsSyncCopyOp
                                {
                                    count              = 1,
                                    dstStart           = (short)(packData.packBitStart - bitPackBitStart + dstBitStart),
                                    srcStart           = 0,
                                    indexInTypeHandles = GetHandleIndexForType(packData.type, typeIndicesToHandleIndices),
                                    opType             = GraphicsSyncCopyOp.OpType.ComponentExist
                                });
                            }
                            else
                            {
                                var dstStartRelative = packData.packBitStart - bitPackBitStart;
                                var maxBits          = 8 - math.max(0, dstStartRelative);
                                var count            = (short)math.min(dstStartRelative + packData.componentBitCount, maxBits);
                                var dstStart         = (short)(math.max(0, packData.packBitStart - bitPackBitStart) + dstBitStart);
                                var srcStart         = (short)(packData.componentBitStart + math.max(0, bitPackBitStart - packData.packBitStart));

                                ops.Add(new GraphicsSyncCopyOp
                                {
                                    count              = count,
                                    dstStart           = dstStart,
                                    srcStart           = srcStart,
                                    indexInTypeHandles = GetHandleIndexForType(packData.type, typeIndicesToHandleIndices),
                                    opType             = GraphicsSyncCopyOp.OpType.BitRange
                                });
                            }
                        }
                    }
                }

                passIndex++;
            }
            // Todo
        }

        static GraphicsSyncGatherRegistration.AssignmentRange FindRange(GraphicsSyncGatherRegistration registration, byte b, int offset)
        {
            foreach (var assignment in registration.assignmentRanges)
            {
                if (assignment.start <= b && assignment.start + assignment.count > b)
                    return assignment;
            }
            throw new System.InvalidOperationException($"Range not found for the byte at offset {offset}. The assignment codes may be corrupted.");
        }

        static byte GetHandleIndexForType(TypeIndex typeIndex, NativeHashMap<TypeIndex, byte> map)
        {
            var found = map.TryGetValue(typeIndex, out var handleIndex);
            if (found)
                return handleIndex;

            var count = map.Count;
            if (count >= 255)
                throw new System.InvalidOperationException("Too many different component types have been collected.");

            handleIndex = (byte)count;
            map.Add(typeIndex, handleIndex);
            return handleIndex;
        }
    }
}

