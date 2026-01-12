using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.ShaderGraph;

namespace Latios.Kinemation.Editor
{
    static class FixDecalMeshes
    {
        [InitializeOnLoadMethod]
        public static void FixDecalShaderGraphVertices()
        {
            var subTargets = TypeCache.GetTypesDerivedFrom<SubTarget>();
            foreach (var subTarget in subTargets)
            {
                if (subTarget.Name.Contains("Decal"))
                {
                    if (subTarget.Name.Contains("Universal"))
                        PatchUrp(subTarget);
                    else
                        PatchHdrp(subTarget);
                }
            }
        }

        static void PatchUrp(System.Type type)
        {
            var passes = type.GetNestedType("DecalPasses", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            if (passes == null)
                UnityEngine.Debug.Log($"Passes was null. Target: {type.Name}");

            UpdatePass(passes, "DBufferMesh");
            UpdatePass(passes, "ForwardEmissiveMesh");
            UpdatePass(passes, "ScreenSpaceMesh");
            UpdatePass(passes, "GBufferMesh");

            var absolutePath = Path.GetFullPath("Packages/com.unity.render-pipelines.universal/Editor/Decal/DecalPass.template");
            var lines = File.ReadAllLines(absolutePath).ToList();
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                if (line.Contains("// Build Graph Inputs"))
                {
                    if (!lines[i + 1].Contains("BuildVertexDescriptionInputs"))
                    { 
                        lines.Insert(i + 1, "    $features.graphVertex:  $include(\"BuildVertexDescriptionInputs.template.hlsl\")");
                        File.WriteAllLines(absolutePath, lines);
                    }
                    break;
                }
            }
        }

        static void PatchHdrp(System.Type type)
        {
            var passes = type.GetNestedType("DecalPasses", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
            if (passes == null) // We can find the WaterDecalSubTarget
                return;

            UpdatePass(passes, "DBufferMesh");
            UpdatePass(passes, "DecalMeshForwardEmissive");
        }

        static void UpdatePass(System.Type passes, string fieldName)
        {
            var field = passes.GetField(fieldName, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
            var pass = (PassDescriptor)field.GetValue(null);
            pass.validVertexBlocks = positionBlocks;
            field.SetValue(null, pass);
        }

        static BlockFieldDescriptor[] positionBlocks =
        {
            BlockFields.VertexDescription.Position,
            BlockFields.VertexDescription.Normal,
            BlockFields.VertexDescription.Tangent
        };
    }
}