using Latios.Kinemation;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.MachAxle
{
    struct Graph
    {
        public int                      baseInputCount;
        public int                      easyLayerCount;
        public BlobArray<Layer>         layers;
        public BlobArray<byte>          curveStream;
        public BlobArray<Port>          sourcePorts;
        public BlobArray<Port>          destinationPorts;
        public BlobArray<PortSpan>      destinationPortSpans;
        public BlobArray<float>         constants;
        public CellConstants            outputConstants;
        public CellConstants            temporaryConstants;
        public BlobArray<InstanceGroup> instanceGroups;

        public BlobArray<ParameterClip> parameterClips;
    }

    struct Layer
    {
        public int                  streamStartByte;
        public BlobArray<CurveType> curveTypes;
    }

    enum CellType
    {
        External,
        Internal,
        InstancedExternal,
        InstancedInternal
    }

    struct Port
    {
        ushort packed;

        public CellType type
        {
            get => (CellType)(packed >> 14);
            set => packed = (ushort)((packed & 0x3fff) | ((ushort)value << 14));
        }

        public bool isExternal => (packed & 0x4000) == 0;

        public int index
        {
            get => packed & 0x3fff;
            set => packed = (ushort)((packed & 0xc000) | (ushort)value);
        }
    }

    struct PortSpan
    {
        uint packed;

        public int start
        {
            get => (int)(packed & 0x0003ffff);
            set => packed = (packed & 0xfffc0000) | (uint)value;
        }

        public int count
        {
            get => (int)(packed >> 18);
            set => packed = (packed & 0x0003ffff) | ((uint)value << 18);
        }
    }

    struct CellConstants
    {
        public BlobArray<float>      defaultValues;
        public BlobArray<BitField32> useAddAggregation;  // Unset uses multiply aggregation
    }

    struct InstanceGroup
    {
        public BlobArray<Layer> layers;
        public int              inputCountPerInstance;
        public CellConstants    outputConstants;
        public CellConstants    temporaryConstants;
    }
}

