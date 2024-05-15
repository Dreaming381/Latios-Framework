using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.MachAxle.Authoring
{
    // Make all these public once ready
    internal struct CellHandle
    {
        internal int index;
    }

    internal enum CellAggregationMode
    {
        Multiply,
        Add
    }

    internal interface ICurveDescription
    {
        public void AddCurveToDescription(ref GraphDescription graphDescription);
    }

    internal struct GraphDescription : IDisposable
    {
        public GraphDescription(AllocatorManager.AllocatorHandle allocator)
        {
            throw new NotImplementedException();
        }

        public void Dispose()
        {
            throw new NotImplementedException();
        }

        public CellHandle CreateBaseInput(int journalID, bool isEasyInput = false)
        {
            throw new NotImplementedException();
        }

        public CellHandle CreateInstanceGroupInput(int journalID, int instanceGroupIndex, bool isEasyInput = false)
        {
            throw new NotImplementedException();
        }

        public CellHandle CreateBaseOutput(int journalID, CellAggregationMode aggregationMode)
        {
            throw new NotImplementedException();
        }

        public CellHandle CreateInstanceGroupOutput(int journalID, int instanceGroupIndex, CellAggregationMode aggregationMode)
        {
            throw new NotImplementedException();
        }

        public CellHandle CreateIntermediateCell(int journalID, CellAggregationMode aggregationMode, bool joinInstances = false)
        {
            throw new NotImplementedException();
        }

        public void AddCurve<T>(T curveDescription) where T : ICurveDescription
        {
            throw new NotImplementedException();
        }

        public GraphBuilder CreateBuilder(AllocatorManager.AllocatorHandle allocator)
        {
            throw new NotImplementedException();
        }
    }
}

