using UnityEditor.Graphing;
using UnityEditor.ShaderGraph;
using UnityEditor.ShaderGraph.Drawing.Controls;
using UnityEditor.ShaderGraph.Internal;
using UnityEngine;

namespace Latios.Kinemation.Editor.ShaderGraphNodes
{
    [Title("Input", "Mesh Deformation", "Latios Deform")]
    class LatiosDeformNode : AbstractMaterialNode, IGeneratesBodyCode, IGeneratesFunction, IMayRequireVertexID, IMayRequirePosition, IMayRequireNormal,
        IMayRequireTangent
    {
        public const int kPositionOutputSlotId  = 0;
        public const int kNormalOutputSlotId    = 1;
        public const int kTangentOutputSlotId   = 2;
        public const int kCustomMeshIndexSlotId = 3;

        public const string kOutputSlotPositionName  = "Deformed Position";
        public const string kOutputSlotNormalName    = "Deformed Normal";
        public const string kOutputSlotTangentName   = "Deformed Tangent";
        public const string kSlotCustomMeshIndexName = "Mesh Index";

        public enum Source
        {
            Current,
            Previous,
            TwoAgo,
            Custom
        }

        [SerializeField] private Source m_source;

        [EnumControl("Source")]
        public Source source
        {
            get { return m_source; }
            set
            {
                if (m_source == value)
                    return;

                m_source = value;
                UpdateNodeAfterDeserialization();
                Dirty(ModificationScope.Graph);
            }
        }

        public LatiosDeformNode()
        {
            name = "Latios Deform";
            UpdateNodeAfterDeserialization();
        }

        public sealed override void UpdateNodeAfterDeserialization()
        {
            AddSlot(new Vector3MaterialSlot(kPositionOutputSlotId, kOutputSlotPositionName, kOutputSlotPositionName, SlotType.Output, Vector3.zero, ShaderStageCapability.Vertex));
            AddSlot(new Vector3MaterialSlot(kNormalOutputSlotId, kOutputSlotNormalName, kOutputSlotNormalName, SlotType.Output, Vector3.zero, ShaderStageCapability.Vertex));
            AddSlot(new Vector3MaterialSlot(kTangentOutputSlotId, kOutputSlotTangentName, kOutputSlotTangentName, SlotType.Output, Vector3.zero, ShaderStageCapability.Vertex));

            if (source == Source.Custom)
            {
                // Todo: Explicitly make this a uint type?
                AddSlot(new Vector1MaterialSlot(kCustomMeshIndexSlotId,
                                                kSlotCustomMeshIndexName,
                                                kSlotCustomMeshIndexName,
                                                SlotType.Input,
                                                (int)0,
                                                ShaderStageCapability.Vertex));
                RemoveSlotsNameNotMatching(new[] { kPositionOutputSlotId, kNormalOutputSlotId, kTangentOutputSlotId, kCustomMeshIndexSlotId });
                return;
            }

            RemoveSlotsNameNotMatching(new[] { kPositionOutputSlotId, kNormalOutputSlotId, kTangentOutputSlotId }, true);
        }

        public bool RequiresVertexID(ShaderStageCapability stageCapability = ShaderStageCapability.All)
        {
            return true;
        }

        public NeededCoordinateSpace RequiresPosition(ShaderStageCapability stageCapability = ShaderStageCapability.All)
        {
            if (stageCapability == ShaderStageCapability.Vertex || stageCapability == ShaderStageCapability.All)
                return NeededCoordinateSpace.Object;
            else
                return NeededCoordinateSpace.None;
        }

        public NeededCoordinateSpace RequiresNormal(ShaderStageCapability stageCapability = ShaderStageCapability.All)
        {
            if (stageCapability == ShaderStageCapability.Vertex || stageCapability == ShaderStageCapability.All)
                return NeededCoordinateSpace.Object;
            else
                return NeededCoordinateSpace.None;
        }

        public NeededCoordinateSpace RequiresTangent(ShaderStageCapability stageCapability = ShaderStageCapability.All)
        {
            if (stageCapability == ShaderStageCapability.Vertex || stageCapability == ShaderStageCapability.All)
                return NeededCoordinateSpace.Object;
            else
                return NeededCoordinateSpace.None;
        }

        public override void CollectShaderProperties(PropertyCollector properties, GenerationMode generationMode)
        {
            if (source != Source.Custom)
            {
                properties.AddShaderProperty(new Vector1ShaderProperty()
                {
                    displayName             = $"{source} Index Offset",
                    overrideReferenceName   = GetPropertyReferenceName(),
                    overrideHLSLDeclaration = true,
                    hlslDeclarationOverride = HLSLDeclaration.HybridPerInstance,
                    hidden                  = true,
                    value                   = 0
                });
            }

            base.CollectShaderProperties(properties, generationMode);
        }

        // This generates the code that calls our functions.
        public void GenerateNodeCode(ShaderStringBuilder sb, GenerationMode generationMode)
        {
            sb.AppendLine("#if defined(UNITY_DOTS_INSTANCING_ENABLED)");
            sb.AppendLine("$precision3 {0} = 0;", GetVariableNameForSlot(kPositionOutputSlotId));
            sb.AppendLine("$precision3 {0} = 0;", GetVariableNameForSlot(kNormalOutputSlotId));
            sb.AppendLine("$precision3 {0} = 0;", GetVariableNameForSlot(kTangentOutputSlotId));
            if (generationMode == GenerationMode.ForReals)
            {
                sb.AppendLine($"{GetFunctionName()}(" +
                              (source == Source.Custom ? $"{GetVariableNameForSlot(kCustomMeshIndexSlotId)}, " : "") +
                              $"IN.VertexID, " +
                              $"{GetVariableNameForSlot(kPositionOutputSlotId)}, " +
                              $"{GetVariableNameForSlot(kNormalOutputSlotId)}, " +
                              $"{GetVariableNameForSlot(kTangentOutputSlotId)});");
            }
            sb.AppendLine("#else");
            sb.AppendLine("$precision3 {0} = IN.ObjectSpacePosition;", GetVariableNameForSlot(kPositionOutputSlotId));
            sb.AppendLine("$precision3 {0} = IN.ObjectSpaceNormal;",   GetVariableNameForSlot(kNormalOutputSlotId));
            sb.AppendLine("$precision3 {0} = IN.ObjectSpaceTangent;",  GetVariableNameForSlot(kTangentOutputSlotId));
            sb.AppendLine("#endif");
        }

        // This generates our functions, and is outside any function scope.
        public void GenerateNodeFunction(FunctionRegistry registry, GenerationMode generationMode)
        {
            registry.ProvideFunction("includeLatiosDeform", sb =>
            {
                sb.AppendLine("#if defined(UNITY_DOTS_INSTANCING_ENABLED)");
                // Comment mutes function-not-provided warning
                sb.AppendLine("// includeLatiosDeform");
                sb.AppendLine("#include \"Packages/com.latios.latiosframework/Kinemation/ShaderLibrary/DeformBufferSample.hlsl\"");
                sb.AppendLine("#endif");
            });

            registry.ProvideFunction(GetFunctionName(), sb =>
            {
                sb.AppendLine($"#ifndef PREVENT_REPEAT_{source}");
                sb.AppendLine($"#define PREVENT_REPEAT_{source}");
                sb.AppendLine($"void {GetFunctionName()}(" +
                              // Todo: Make as uint?
                              (source == Source.Custom ? $"float customBase, " : "") +
                              "uint vertexId, " +
                              "out $precision3 positionOut, " +
                              "out $precision3 normalOut, " +
                              "out $precision3 tangentOut)");
                sb.AppendLine("{");
                using (sb.IndentScope())
                {
                    sb.AppendLine("positionOut = 0;");
                    sb.AppendLine("normalOut = 0;");
                    sb.AppendLine("tangentOut = 0;");
                    sb.AppendLine("#if defined(UNITY_DOTS_INSTANCING_ENABLED)");
                    if (source == Source.Custom)
                        sb.AppendLine("uint baseIndex = asuint(customBase);");
                    else
                        sb.AppendLine($"uint baseIndex = asuint(UNITY_ACCESS_HYBRID_INSTANCED_PROP({GetPropertyReferenceName()}, float));");

                    sb.AppendLine("sampleDeform(vertexId, baseIndex, positionOut, normalOut, tangentOut);");
                    sb.AppendLine("#endif");
                }
                sb.AppendLine("}");
                sb.AppendLine("#endif");
            });
        }

        string GetFunctionName()
        {
            return $"Latios_Deform_{source}_$precision";
        }

        string GetPropertyReferenceName()
        {
            string src = source switch
            {
                Source.Current => "Current",
                Source.Previous => "Previous",
                Source.TwoAgo => "TwoAgo",
                Source.Custom => "CustomShouldntHappen",
                _ => "What???",
            };

            return $"_latios{src}DeformBase";
        }
    }
}

