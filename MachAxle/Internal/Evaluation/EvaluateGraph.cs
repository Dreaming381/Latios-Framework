using System.Diagnostics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.MachAxle
{
    internal static unsafe partial class Evaluation
    {
        public static void EvaluateGraph(ref AxleGraphEvaluator evaluator, bool easyInputsOnly = false)
        {
            CheckEvaluatorAndGraphAreCompatible(ref evaluator);

            // Todo: Support output and inter-layer masking in the future.
            using var allocator = evaluator.m_threadStackAllocator.CreateChildAllocator();
            ref var   graph     = ref *evaluator.m_currentGraph;
            var       baseCells = allocator.Allocate<float>(graph.temporaryConstants.defaultValues.Length);
            UnsafeUtility.MemCpy(baseCells,
                                 graph.temporaryConstants.defaultValues.GetUnsafePtr(),
                                 graph.temporaryConstants.defaultValues.Length * UnsafeUtility.SizeOf<float>());
            UnsafeUtility.MemCpy(evaluator.m_baseOutputBinding,
                                 graph.outputConstants.defaultValues.GetUnsafePtr(),
                                 graph.outputConstants.defaultValues.Length * UnsafeUtility.SizeOf<float>());

            BaseBus baseBus = new BaseBus
            {
                externalInputs  = evaluator.m_baseInputBinding,
                externalOutputs = evaluator.m_baseOutputBinding,
                graph           = evaluator.m_currentGraph,
                cells           = baseCells,
            };

            var groupBuses = allocator.Allocate<InstanceBus>(graph.instanceGroups.Length);
            for (int i = 0; i < graph.instanceGroups.Length; i++)
            {
                var     instanceCount = evaluator.m_instanceGroupBindings[i].inputInstanceCount;
                ref var defaultValues = ref graph.instanceGroups[i].temporaryConstants.defaultValues;
                var     instanceCells = allocator.Allocate<float>(defaultValues.Length * instanceCount);
                UnsafeUtility.MemCpyReplicate(instanceCells, defaultValues.GetUnsafePtr(), defaultValues.Length * UnsafeUtility.SizeOf<float>(), instanceCount);
                groupBuses[i] = new InstanceBus
                {
                    baseCells               = baseCells,
                    externalBaseInputs      = evaluator.m_baseInputBinding,
                    externalBaseOutputs     = evaluator.m_baseOutputBinding,
                    externalInstanceInputs  = evaluator.m_instanceGroupBindings[i].inputBinding,
                    externalInstanceOutputs = evaluator.m_instanceGroupBindings[i].outputBinding,
                    graph                   = evaluator.m_currentGraph,
                    groupIndex              = i,
                    inputsPerInstance       = evaluator.m_instanceGroupBindings[i].inputCountPerInstance,
                    instanceCells           = instanceCells,
                    instanceCount           = instanceCount,
                    internalsPerInstance    = defaultValues.Length,
                    outputsPerInstance      = evaluator.m_instanceGroupBindings[i].outputCountPerInstance
                };
            }

            var layerCount = math.select(graph.layers.Length, graph.easyLayerCount, easyInputsOnly);

            for (int layerIndex = 0; layerIndex < layerCount; layerIndex++)
            {
                EvaluateLayer(ref graph.layers[layerIndex], ref graph, ref baseBus);

                for (int i = 0; i < graph.instanceGroups.Length; i++)
                {
                    EvaluateLayer(ref graph.instanceGroups[i].layers[layerIndex], ref graph, ref groupBuses[i]);
                }
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        static void CheckEvaluatorAndGraphAreCompatible(ref AxleGraphEvaluator evaluator)
        {
            if (evaluator.m_currentGraph == null)
                throw new System.ArgumentNullException("The AxleGraphEvaluator does not refer to a valid AxleGraph.");
            var graph = evaluator.m_currentGraph;
            if (graph->baseInputCount > 0 && evaluator.m_baseInputBinding == null)
                throw new System.ArgumentNullException("The AxleGraphEvaluator does not have a valid Base Input Binding.");
            if (graph->baseInputCount > evaluator.m_baseInputCount)
                throw new System.ArgumentOutOfRangeException(
                    $"The AxleGraphEvaluator has insufficient number of elements for the Base Input Binding. Required: {graph->baseInputCount}, Provided: {evaluator.m_baseInputCount}");
            if (graph->outputConstants.defaultValues.Length > 0 && evaluator.m_baseOutputBinding == null)
                throw new System.ArgumentNullException("The AxleGraphEvaluator does not have a valid Base Output Binding.");
            if (graph->outputConstants.defaultValues.Length > evaluator.m_baseOutputCount)
                throw new System.ArgumentOutOfRangeException(
                    $"The AxleGraphEvaluator has insufficient number of elements for the Base Output Binding. Required: {graph->outputConstants.defaultValues.Length}, Provided: {evaluator.m_baseOutputCount}");

            for (int i = 0; i < graph->instanceGroups.Length; i++)
            {
                ref var group   = ref graph->instanceGroups[i];
                ref var binding = ref evaluator.m_instanceGroupBindings.ElementAt(i);

                if (binding.inputInstanceCount != binding.outputInstanceCount)
                    throw new System.InvalidOperationException(
                        $"Instance Group Binding {i} has differing number of input and output instances. {binding.inputInstanceCount} vs {binding.outputInstanceCount}");
                if (binding.inputInstanceCount > 0 && binding.inputBinding == null)
                    throw new System.ArgumentNullException($"Input Binding for Instance Group {i} has a non-zero instances but a null binding.");
                if (group.inputCountPerInstance > binding.inputCountPerInstance)
                    throw new System.ArgumentOutOfRangeException(
                        $"Input Binding for Instance Group {i} has insufficient number of elements. Required: {group.inputCountPerInstance}, Provided: {binding.inputCountPerInstance}");
                if (binding.outputInstanceCount > 0 && binding.outputBinding == null)
                    throw new System.ArgumentNullException($"Output Binding for Instance Group {i} has a non-zero instances but a null binding.");
                if (group.outputConstants.defaultValues.Length > binding.outputCountPerInstance)
                    throw new System.ArgumentOutOfRangeException(
                        $"Output Binding for Instance Group {i} has insufficient number of elements. Required: {group.outputConstants.defaultValues.Length}, Provided: {binding.outputCountPerInstance}");
            }
        }
    }
}

