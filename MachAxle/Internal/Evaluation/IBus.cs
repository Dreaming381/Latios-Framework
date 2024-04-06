using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.MachAxle
{
    interface IBus
    {
        float Import(Port port, int instanceIndex);
        void Export(PortSpan portSpan, int instanceIndex, float result);

        int InstanceCount();

        bool HasFilters();
        ReadOnlySpan<BitField64> GetFilter();

        void SetJournalPortIndex(int index);
        void SetJournalCurveBaseIndex(int index);
        void SetJournalCurveLocalIndex(int index);
    }

    unsafe struct BaseBus : IBus
    {
        public Graph* graph;
        public float* externalInputs;
        public float* externalOutputs;
        public float* cells;

        public float Import(Port port, int instanceIndex)
        {
            if (port.isExternal)
                return externalInputs[port.index];
            return cells[port.index];
        }

        public void Export(PortSpan portSpan, int instanceIndex, float result)
        {
            var start = portSpan.start;
            for (int i = 0; i < portSpan.count; i++)
            {
                var         port     = graph->destinationPorts[start + i];
                float*      array    = port.isExternal ? externalOutputs : cells;
                BitField32* bitArray =
                    port.isExternal ? (BitField32*)graph->outputConstants.useAddAggregation.GetUnsafePtr() : (BitField32*)graph->temporaryConstants.useAddAggregation.GetUnsafePtr();
                int  index = port.index;
                bool add   = bitArray[index >> 5].IsSet(index & 0x1f);
                if (add)
                    array[index] += result;
                else
                    array[index] *= result;
            }
        }

        public int InstanceCount() => 1;

        public bool HasFilters() => false;

        public ReadOnlySpan<BitField64> GetFilter() => default;

        public void SetJournalCurveBaseIndex(int index)
        {
        }

        public void SetJournalCurveLocalIndex(int index)
        {
        }

        public void SetJournalPortIndex(int index)
        {
        }
    }

    unsafe struct InstanceBus : IBus
    {
        public Graph* graph;
        public float* externalBaseInputs;
        public float* externalBaseOutputs;
        public float* baseCells;

        public float* externalInstanceInputs;
        public float* externalInstanceOutputs;
        public float* instanceCells;

        public int groupIndex;
        public int instanceCount;
        public int inputsPerInstance;
        public int outputsPerInstance;
        public int internalsPerInstance;

        public float Import(Port port, int instanceIndex)
        {
            var index = port.index;
            return port.type switch
                   {
                       CellType.External => externalBaseInputs[index],
                       CellType.Internal => baseCells[index],
                       CellType.InstancedExternal => externalInstanceInputs[index + instanceIndex * inputsPerInstance],
                       CellType.InstancedInternal => instanceCells[index + instanceIndex * internalsPerInstance],
                       _ => 0f,
                   };
        }

        public void Export(PortSpan portSpan, int instanceIndex, float result)
        {
            var start = portSpan.start;
            for (int i = 0; i < portSpan.count; i++)
            {
                var port = graph->destinationPorts[start + i];

                float*      array    = null;
                BitField32* bitArray = null;
                switch (port.type)
                {
                    case CellType.External:
                        array    = externalBaseOutputs;
                        bitArray = (BitField32*)graph->outputConstants.useAddAggregation.GetUnsafePtr();
                        break;
                    case CellType.Internal:
                        array    = baseCells;
                        bitArray = (BitField32*)graph->temporaryConstants.useAddAggregation.GetUnsafePtr();
                        break;
                    case CellType.InstancedExternal:
                        array    = externalInstanceOutputs + outputsPerInstance * instanceIndex;
                        bitArray = (BitField32*)graph->instanceGroups[groupIndex].outputConstants.useAddAggregation.GetUnsafePtr();
                        break;
                    case CellType.InstancedInternal:
                        array    = instanceCells + internalsPerInstance * instanceIndex;
                        bitArray = (BitField32*)graph->instanceGroups[groupIndex].temporaryConstants.useAddAggregation.GetUnsafePtr();
                        break;
                }

                int  index = port.index;
                bool add   = bitArray[index >> 5].IsSet(index & 0x1f);
                if (add)
                    array[index] += result;
                else
                    array[index] *= result;
            }
        }

        public int InstanceCount() => instanceCount;

        public bool HasFilters() => false;

        public ReadOnlySpan<BitField64> GetFilter() => default;

        public void SetJournalCurveBaseIndex(int index)
        {
        }

        public void SetJournalCurveLocalIndex(int index)
        {
        }

        public void SetJournalPortIndex(int index)
        {
        }
    }
}

