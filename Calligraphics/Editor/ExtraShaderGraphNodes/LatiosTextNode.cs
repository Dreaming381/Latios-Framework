using UnityEditor.Graphing;
using UnityEditor.ShaderGraph;
using UnityEditor.ShaderGraph.Drawing.Controls;
using UnityEditor.ShaderGraph.Internal;
using UnityEngine;

namespace Latios.Kinemation.Editor.ShaderGraphNodes
{
    [Title("Input", "Text", "Latios Text")]
    class LatiosTextNode : AbstractMaterialNode, IGeneratesBodyCode, IGeneratesFunction, IMayRequireVertexID, IMayRequirePosition, IMayRequireNormal,
        IMayRequireTangent, IMayRequireMeshUV, IMayRequireVertexColor
    {
        public const int kPositionOutputSlotId = 0;
        public const int kNormalOutputSlotId   = 1;
        public const int kTangentOutputSlotId  = 2;
        public const int kUVAOutputSlotId      = 3;
        public const int kUVBOutputSlotId      = 4;
        public const int kColorOutputSlotId    = 5;
        public const int kUnicodeOutputSlotId  = 6;

        public const string kOutputSlotPositionName = "Local Position";
        public const string kOutputSlotNormalName   = "Local Normal";
        public const string kOutputSlotTangentName  = "Local Tangent";
        public const string kOutputSlotUVAName      = "UV A";
        public const string kOutputSlotUVBName      = "UV B";
        public const string kOutputSlotColorName    = "Color";
        public const string kOutputslotUnicodeName  = "Unicode";

        public LatiosTextNode()
        {
            name = "Latios Text";
            UpdateNodeAfterDeserialization();
        }

        public sealed override void UpdateNodeAfterDeserialization()
        {
            AddSlot(new Vector3MaterialSlot(kPositionOutputSlotId, kOutputSlotPositionName, kOutputSlotPositionName, SlotType.Output, Vector3.zero, ShaderStageCapability.Vertex));
            AddSlot(new Vector3MaterialSlot(kNormalOutputSlotId, kOutputSlotNormalName, kOutputSlotNormalName, SlotType.Output, Vector3.zero, ShaderStageCapability.Vertex));
            AddSlot(new Vector3MaterialSlot(kTangentOutputSlotId, kOutputSlotTangentName, kOutputSlotTangentName, SlotType.Output, Vector3.zero, ShaderStageCapability.Vertex));
            AddSlot(new Vector4MaterialSlot(kUVAOutputSlotId, kOutputSlotUVAName, kOutputSlotUVAName, SlotType.Output, Vector4.zero, ShaderStageCapability.Vertex));
            AddSlot(new Vector2MaterialSlot(kUVBOutputSlotId, kOutputSlotUVBName, kOutputSlotUVBName, SlotType.Output, Vector2.zero, ShaderStageCapability.Vertex));
            AddSlot(new ColorRGBAMaterialSlot(kColorOutputSlotId, kOutputSlotColorName, kOutputSlotColorName, SlotType.Output, Vector4.one, ShaderStageCapability.Vertex));
            // Todo: Expose Unicode later

            RemoveSlotsNameNotMatching(new[] { kPositionOutputSlotId, kNormalOutputSlotId, kTangentOutputSlotId, kUVAOutputSlotId, kUVBOutputSlotId, kColorOutputSlotId }, true);
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

        public bool RequiresMeshUV(UVChannel channel, ShaderStageCapability stageCapability = ShaderStageCapability.All)
        {
            // Todo: Switch to UV1 for older TextMeshPro?
            return channel == UVChannel.UV0 || channel == UVChannel.UV2;
        }

        public bool RequiresVertexColor(ShaderStageCapability stageCapability = ShaderStageCapability.All)
        {
            return true;
        }

        public override void CollectShaderProperties(PropertyCollector properties, GenerationMode generationMode)
        {
            properties.AddShaderProperty(new Vector2ShaderProperty()
            {
                displayName             = $"Text Index and Count",
                overrideReferenceName   = "_latiosTextGlyphBase",
                overrideHLSLDeclaration = true,
                hlslDeclarationOverride = HLSLDeclaration.HybridPerInstance,
                hidden                  = true,
                value                   = Vector2.zero
            });
            properties.AddShaderProperty(new Vector1ShaderProperty()
            {
                displayName             = $"Text Material Mask Index",
                overrideReferenceName   = "_latiosTextGlyphMaskBase",
                overrideHLSLDeclaration = true,
                hlslDeclarationOverride = HLSLDeclaration.HybridPerInstance,
                hidden                  = true,
                value                   = 0f
            });

            base.CollectShaderProperties(properties, generationMode);
        }

        // This generates the code that calls our functions.
        public void GenerateNodeCode(ShaderStringBuilder sb, GenerationMode generationMode)
        {
            sb.AppendLine("#if defined(UNITY_DOTS_INSTANCING_ENABLED)");
            sb.AppendLine("$precision3 {0} = 0;", GetVariableNameForSlot(kPositionOutputSlotId));
            sb.AppendLine("$precision3 {0} = 0;", GetVariableNameForSlot(kNormalOutputSlotId));
            sb.AppendLine("$precision3 {0} = 0;", GetVariableNameForSlot(kTangentOutputSlotId));
            sb.AppendLine("$precision4 {0} = 0;", GetVariableNameForSlot(kUVAOutputSlotId));
            sb.AppendLine("$precision2 {0} = 0;", GetVariableNameForSlot(kUVBOutputSlotId));
            sb.AppendLine("$precision4 {0} = 0;", GetVariableNameForSlot(kColorOutputSlotId));
            if (generationMode == GenerationMode.ForReals)
            {
                sb.AppendLine("uint2 baseIndex = asuint(UNITY_ACCESS_HYBRID_INSTANCED_PROP(_latiosTextGlyphBase, float2));");
                sb.AppendLine("uint maskIndex = asuint(UNITY_ACCESS_HYBRID_INSTANCED_PROP(_latiosTextGlyphMaskBase, float));");  // We rely on the default from AddShaderProperty
                sb.AppendLine("GlyphVertex glyph = sampleGlyph(IN.VertexID, baseIndex.x, baseIndex.y, maskIndex);");
                sb.AppendLine("{0} = glyph.position;", GetVariableNameForSlot(kPositionOutputSlotId));
                sb.AppendLine("{0} = glyph.normal;",   GetVariableNameForSlot(kNormalOutputSlotId));
                sb.AppendLine("{0} = glyph.tangent;",  GetVariableNameForSlot(kTangentOutputSlotId));
                sb.AppendLine("{0} = glyph.uvA;",      GetVariableNameForSlot(kUVAOutputSlotId));
                sb.AppendLine("{0} = glyph.uvB;",      GetVariableNameForSlot(kUVBOutputSlotId));
                sb.AppendLine("{0} = glyph.color;",    GetVariableNameForSlot(kColorOutputSlotId));
            }
            sb.AppendLine("#else");
            sb.AppendLine("$precision3 {0} = IN.ObjectSpacePosition;", GetVariableNameForSlot(kPositionOutputSlotId));
            sb.AppendLine("$precision3 {0} = IN.ObjectSpaceNormal;",   GetVariableNameForSlot(kNormalOutputSlotId));
            sb.AppendLine("$precision3 {0} = IN.ObjectSpaceTangent;",  GetVariableNameForSlot(kTangentOutputSlotId));
            sb.AppendLine("$precision4 {0} = IN.uv0;",                 GetVariableNameForSlot(kUVAOutputSlotId));
            sb.AppendLine("$precision2 {0} = IN.uv2;",                 GetVariableNameForSlot(kUVBOutputSlotId));
            sb.AppendLine("$precision4 {0} = IN.VertexColor;",         GetVariableNameForSlot(kColorOutputSlotId));
            sb.AppendLine("#endif");
        }

        // This generates our functions, and is outside any function scope.
        public void GenerateNodeFunction(FunctionRegistry registry, GenerationMode generationMode)
        {
            registry.ProvideFunction("includeLatiosText", sb =>
            {
                sb.AppendLine("#if defined(UNITY_DOTS_INSTANCING_ENABLED)");
                // Comment mutes function-not-provided warning
                sb.AppendLine("// includeLatiosText");
                sb.AppendLine("#include \"Packages/com.latios.latiosframework/Calligraphics/ShaderLibrary/TextGlyphParsing.hlsl\"");
                sb.AppendLine("#endif");
            });
        }
    }
}

