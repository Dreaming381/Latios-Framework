using System;
using Latios.Unsafe;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.MachAxle
{
    // Make these public once ready
    internal struct AxleGraph
    {
        internal BlobPtr<Graph> m_graph;

        public int baseInputCount => m_graph.Value.baseInputCount;
        public int baseOutputCount => m_graph.Value.outputConstants.defaultValues.Length;
        public int instanceGroupCount => m_graph.Value.instanceGroups.Length;
        public int GetPerInstanceInputCountInInstanceGroup(int instanceGroupIndex) => m_graph.Value.instanceGroups[instanceGroupIndex].inputCountPerInstance;
        public int GetPerInstanceOutputCountInInstanceGroup(int instanceGroupIndex) => m_graph.Value.instanceGroups[instanceGroupIndex].outputConstants.defaultValues.Length;
    }

    // Note: This is Disposable
    internal unsafe ref struct AxleGraphEvaluator
    {
        #region Create and Destroy
        public AxleGraphEvaluator(ref AxleGraph graph)
        {
            this                    = default;
            m_threadStackAllocator  = ThreadStackAllocator.GetAllocator();
            m_currentGraph          = (Graph*)graph.m_graph.GetUnsafePtr();
            var instanceGroupCount  = graph.instanceGroupCount;
            var groupsPtr           = m_threadStackAllocator.Allocate<InstanceGroupBinding>(instanceGroupCount);
            m_instanceGroupBindings = new UnsafeList<InstanceGroupBinding>(groupsPtr, instanceGroupCount);
            m_instanceGroupBindings.Clear();
            m_instanceGroupBindings.AddReplicate(default, instanceGroupCount);
        }

        // Preserves bindings as much as possible
        public void SetGraph(ref AxleGraph graph)
        {
            var instanceGroupCount = graph.instanceGroupCount;
            m_currentGraph         = (Graph*)graph.m_graph.GetUnsafePtr();

            if (instanceGroupCount > m_instanceGroupBindings.Capacity || m_currentGraph == null)
            {
                var groupsPtr        = m_threadStackAllocator.Allocate<InstanceGroupBinding>(instanceGroupCount);
                var newGroupBindings = new UnsafeList<InstanceGroupBinding>(groupsPtr, instanceGroupCount);
                newGroupBindings.Clear();
                newGroupBindings.AddRangeNoResize(m_instanceGroupBindings);
                newGroupBindings.AddReplicate(default, instanceGroupCount - m_instanceGroupBindings.Length);
                m_instanceGroupBindings = newGroupBindings;
            }
            else if (instanceGroupCount < m_instanceGroupBindings.Length)
            {
                m_instanceGroupBindings.Length = instanceGroupCount;
            }
            else if (instanceGroupCount > m_instanceGroupBindings.Length)
            {
                m_instanceGroupBindings.AddReplicate(default, instanceGroupCount - m_instanceGroupBindings.Length);
            }
        }

        public void Dispose()
        {
            m_threadStackAllocator.Dispose();
            this = default;
        }
        #endregion

        #region Bind Unsafe
        public void BindBaseInput(float* inputArray, int floatCount)
        {
            m_baseInputBinding = inputArray;
            m_baseInputCount   = floatCount;
        }
        public void BindBaseOutput(float* outputArray, int floatCount)
        {
            m_baseOutputBinding = outputArray;
            m_baseOutputCount   = floatCount;
        }
        public void BindInstanceGroupInput(int instanceGroupIndex, float* inputArrayForAllInstances, int floatCountPerInstance, int instanceCount)
        {
            ref var group               = ref m_instanceGroupBindings.ElementAt(instanceGroupIndex);
            group.inputBinding          = inputArrayForAllInstances;
            group.inputCountPerInstance = floatCountPerInstance;
            group.inputInstanceCount    = instanceCount;
        }
        public void BindInstanceGroupOutput(int instanceGroupIndex, float* outputArrayForAllInstances, int floatCountPerInstance, int instanceCount)
        {
            ref var group                = ref m_instanceGroupBindings.ElementAt(instanceGroupIndex);
            group.outputBinding          = outputArrayForAllInstances;
            group.outputCountPerInstance = floatCountPerInstance;
            group.outputInstanceCount    = instanceCount;
        }
        #endregion

        #region Evaluate
        public void EvaluateGraphFull()
        {
            Evaluation.EvaluateGraph(ref this, false);
        }

        //public void EvaluateGraphEasyInputs()
        //{
        //    Evaluation.EvaluateGraph(ref this, true);
        //}
        #endregion

        #region Internals
        internal ThreadStackAllocator m_threadStackAllocator;
        internal Graph*               m_currentGraph;

        // Todo: Support safe acceptance of Spans and ReadOnlySpans somehow?
        internal float*                           m_baseInputBinding;
        internal float*                           m_baseOutputBinding;
        internal int                              m_baseInputCount;
        internal int                              m_baseOutputCount;
        internal UnsafeList<InstanceGroupBinding> m_instanceGroupBindings;

        // Todo: Support output masks

        internal struct InstanceGroupBinding
        {
            public float* inputBinding;
            public float* outputBinding;
            public int    inputInstanceCount;
            public int    outputInstanceCount;
            public int    inputCountPerInstance;
            public int    outputCountPerInstance;
        }
        #endregion
    }
}

