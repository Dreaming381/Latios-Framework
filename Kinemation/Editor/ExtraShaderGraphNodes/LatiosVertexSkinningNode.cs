using UnityEditor.Graphing;
using UnityEditor.ShaderGraph;
using UnityEditor.ShaderGraph.Drawing.Controls;
using UnityEditor.ShaderGraph.Internal;
using UnityEngine;

namespace Latios.Kinemation.Editor.ShaderGraphNodes
{
    [Title("Input", "Mesh Deformation", "Latios Vertex Skinning")]
    class LatiosVertexSkinningNode : AbstractMaterialNode, IGeneratesBodyCode, IGeneratesFunction, IMayRequireVertexSkinning, IMayRequirePosition, IMayRequireNormal,
        IMayRequireTangent
    {
        public const int kPositionSlotId                    = 0;
        public const int kNormalSlotId                      = 1;
        public const int kTangentSlotId                     = 2;
        public const int kPositionOutputSlotId              = 3;
        public const int kNormalOutputSlotId                = 4;
        public const int kTangentOutputSlotId               = 5;
        public const int kCustomSkeletonIndexSlotId         = 6;
        public const int kCustomSkeletonBindposeIndexSlotId = 7;

        public const string kSlotPositionName                    = "Vertex Position";
        public const string kSlotNormalName                      = "Vertex Normal";
        public const string kSlotTangentName                     = "Vertex Tangent";
        public const string kOutputSlotPositionName              = "Skinned Position";
        public const string kOutputSlotNormalName                = "Skinned Normal";
        public const string kOutputSlotTangentName               = "Skinned Tangent";
        public const string kSlotCustomSkeletonIndexName         = "Skeleton Index";
        public const string kSlotCustomSkeletonBindposeIndexName = " Skeleton and Bindpose Indices";

        public enum Algorithm
        {
            Matrix = 0,
            DualQuaternion = 1
        }

        public enum Source
        {
            Current,
            Previous,
            TwoAgo,
            Custom
        }

        [SerializeField] private Algorithm m_algorithm;
        [SerializeField] private Source    m_source;

        [EnumControl("Algorithm")]
        public Algorithm algorithm
        {
            get { return m_algorithm; }
            set
            {
                if (m_algorithm == value)
                    return;

                m_algorithm = value;
                UpdateNodeAfterDeserialization();
                Dirty(ModificationScope.Graph);
            }
        }

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

        public LatiosVertexSkinningNode()
        {
            name = "Latios Vertex Skinning";
            UpdateNodeAfterDeserialization();
        }

        public sealed override void UpdateNodeAfterDeserialization()
        {
            AddSlot(new PositionMaterialSlot(kPositionSlotId, kSlotPositionName, kSlotPositionName, CoordinateSpace.Object, ShaderStageCapability.Vertex));
            AddSlot(new NormalMaterialSlot(kNormalSlotId, kSlotNormalName, kSlotNormalName, CoordinateSpace.Object, ShaderStageCapability.Vertex));
            AddSlot(new TangentMaterialSlot(kTangentSlotId, kSlotTangentName, kSlotTangentName, CoordinateSpace.Object, ShaderStageCapability.Vertex));
            AddSlot(new Vector3MaterialSlot(kPositionOutputSlotId, kOutputSlotPositionName, kOutputSlotPositionName, SlotType.Output, Vector3.zero, ShaderStageCapability.Vertex));
            AddSlot(new Vector3MaterialSlot(kNormalOutputSlotId, kOutputSlotNormalName, kOutputSlotNormalName, SlotType.Output, Vector3.zero, ShaderStageCapability.Vertex));
            AddSlot(new Vector3MaterialSlot(kTangentOutputSlotId, kOutputSlotTangentName, kOutputSlotTangentName, SlotType.Output, Vector3.zero, ShaderStageCapability.Vertex));

            if (source == Source.Custom)
            {
                // Todo: Explicitly make this a uint type?
                if (algorithm == Algorithm.Matrix)
                {
                    AddSlot(new Vector1MaterialSlot(kCustomSkeletonIndexSlotId,
                                                    kSlotCustomSkeletonIndexName,
                                                    kSlotCustomSkeletonIndexName,
                                                    SlotType.Input,
                                                    (int)0,
                                                    ShaderStageCapability.Vertex));
                    RemoveSlotsNameNotMatching(new[] { kPositionSlotId, kNormalSlotId, kTangentSlotId, kPositionOutputSlotId, kNormalOutputSlotId, kTangentOutputSlotId,
                                                       kCustomSkeletonIndexSlotId }, true);
                }
                else
                {
                    AddSlot(new Vector2MaterialSlot(kCustomSkeletonBindposeIndexSlotId,
                                                    kSlotCustomSkeletonBindposeIndexName,
                                                    kSlotCustomSkeletonBindposeIndexName,
                                                    SlotType.Input,
                                                    Vector2.zero,
                                                    ShaderStageCapability.Vertex));
                    RemoveSlotsNameNotMatching(new[] { kPositionSlotId, kNormalSlotId, kTangentSlotId, kPositionOutputSlotId, kNormalOutputSlotId, kTangentOutputSlotId,
                                                       kCustomSkeletonBindposeIndexSlotId }, true);
                }

                return;
            }

            RemoveSlotsNameNotMatching(new[] { kPositionSlotId, kNormalSlotId, kTangentSlotId, kPositionOutputSlotId, kNormalOutputSlotId, kTangentOutputSlotId }, true);
        }

        public bool RequiresVertexSkinning(ShaderStageCapability stageCapability = ShaderStageCapability.All)
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
                if (algorithm == Algorithm.Matrix)
                {
                    properties.AddShaderProperty(new Vector1ShaderProperty()
                    {
                        displayName             = $"{source} {algorithm} Index Offset",
                        overrideReferenceName   = GetPropertyReferenceName(),
                        overrideHLSLDeclaration = true,
                        hlslDeclarationOverride = HLSLDeclaration.HybridPerInstance,
                        hidden                  = true,
                        value                   = 0
                    });
                }
                else
                {
                    properties.AddShaderProperty(new Vector2ShaderProperty()
                    {
                        displayName             = $"{source} {algorithm} Index Offset",
                        overrideReferenceName   = GetPropertyReferenceName(),
                        overrideHLSLDeclaration = true,
                        hlslDeclarationOverride = HLSLDeclaration.HybridPerInstance,
                        hidden                  = true,
                        value                   = Vector4.zero
                    });
                }
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
                              (source == Source.Custom ? $"{GetCustomOffsetVariableNameForSlot()}, " : "") +
                              $"IN.BoneIndices, " +
                              $"IN.BoneWeights, " +
                              $"{GetSlotValue(kPositionSlotId, generationMode)}, " +
                              $"{GetSlotValue(kNormalSlotId, generationMode)}, " +
                              $"{GetSlotValue(kTangentSlotId, generationMode)}, " +
                              $"{GetVariableNameForSlot(kPositionOutputSlotId)}, " +
                              $"{GetVariableNameForSlot(kNormalOutputSlotId)}, " +
                              $"{GetVariableNameForSlot(kTangentOutputSlotId)});");
            }
            sb.AppendLine("#else");
            sb.AppendLine("$precision3 {0} = {1};", GetVariableNameForSlot(kPositionOutputSlotId), GetSlotValue(kPositionSlotId, generationMode));
            sb.AppendLine("$precision3 {0} = {1};", GetVariableNameForSlot(kNormalOutputSlotId),   GetSlotValue(kNormalSlotId, generationMode));
            sb.AppendLine("$precision3 {0} = {1};", GetVariableNameForSlot(kTangentOutputSlotId),  GetSlotValue(kTangentSlotId, generationMode));
            sb.AppendLine("#endif");
        }

        // This generates our functions, and is outside any function scope.
        public void GenerateNodeFunction(FunctionRegistry registry, GenerationMode generationMode)
        {
            registry.ProvideFunction("includeLatiosVertexSkinning", sb =>
            {
                sb.AppendLine("#if defined(UNITY_DOTS_INSTANCING_ENABLED)");
                // Comment mutes function-not-provided warning
                sb.AppendLine("// includeLatiosVertexSkinning");
                sb.AppendLine("#include \"Packages/com.latios.latiosframework/Kinemation/ShaderLibrary/VertexSkinning.hlsl\"");
                sb.AppendLine("#endif");
            });

            registry.ProvideFunction(GetFunctionName(), sb =>
            {
                sb.AppendLine($"#ifndef PREVENT_REPEAT_{source}_{algorithm}");
                sb.AppendLine($"#define PREVENT_REPEAT_{source}_{algorithm}");
                sb.AppendLine($"void {GetFunctionName()}(" +
                              // Todo: Make as uint?
                              (source == Source.Custom ? (algorithm == Algorithm.Matrix ? $"float customBase, " : $"float2 customBase, ") : "") +
                              "uint4 indices, " +
                              "$precision4 weights, " +
                              "$precision3 positionIn, " +
                              "$precision3 normalIn, " +
                              "$precision3 tangentIn, " +
                              "out $precision3 positionOut, " +
                              "out $precision3 normalOut, " +
                              "out $precision3 tangentOut)");
                sb.AppendLine("{");
                using (sb.IndentScope())
                {
                    sb.AppendLine("positionOut = positionIn;");
                    sb.AppendLine("normalOut = normalIn;");
                    sb.AppendLine("tangentOut = tangentIn;");
                    sb.AppendLine("#if defined(UNITY_DOTS_INSTANCING_ENABLED)");
                    var indexType    = algorithm == Algorithm.Matrix ? "uint" : "uint2";
                    var propertyType = algorithm == Algorithm.Matrix ? "float" : "float2";
                    if (source == Source.Custom)
                        sb.AppendLine($"{indexType} baseIndex = asuint(customBase);");
                    else
                        sb.AppendLine($"{indexType} baseIndex = asuint(UNITY_ACCESS_HYBRID_INSTANCED_PROP({GetPropertyReferenceName()}, {propertyType}));");

                    if (algorithm == Algorithm.Matrix)
                        sb.AppendLine("vertexSkinMatrix(indices, weights, baseIndex, positionOut, normalOut, tangentOut);");
                    else
                        sb.AppendLine("vertexSkinDqs(indices, weights, baseIndex, positionOut, normalOut, tangentOut);");
                    sb.AppendLine("#endif");
                }
                sb.AppendLine("}");
                sb.AppendLine("#endif");
            });
        }

        string GetFunctionName()
        {
            return $"Latios_VertexSkinning_{algorithm}_{source}_$precision";
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
            string algo = algorithm switch
            {
                Algorithm.Matrix => "Matrix",
                Algorithm.DualQuaternion => "Dqs",
                _ => "What???",
            };

            return $"_latios{src}VertexSkinning{algo}Base";
        }

        string GetCustomOffsetVariableNameForSlot() => algorithm == Algorithm.Matrix ? GetVariableNameForSlot(kCustomSkeletonIndexSlotId) : GetVariableNameForSlot(
            kCustomSkeletonBindposeIndexSlotId);
    }
}

