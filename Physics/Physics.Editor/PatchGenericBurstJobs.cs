using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.Build;
using Unity.Build.Common;
using Unity.Entities;
using UnityEditor;
using UnityEditor.Compilation;

//Stub.
//Idea was cool until I realized that we wouldn't be able to access private user-defined types.
namespace Latios.PhysicsEngine.Editor
{
    [BuildStep(description = k_buildDescription, category = "Latios")]
    sealed class BuildStepPathGenericBurstJobs : BuildStep
    {
        const string         k_buildDescription = "Patch Generic Physics Burst Jobs";
        TemporaryFileTracker m_tempFileTracker;

        public override string Description => k_buildDescription;

        int    m_buildErrors     = -1;
        string m_firstBuildError = "";

        public override Type[] RequiredComponents => new[]
        {
            typeof(Unity.Build.Common.ClassicBuildProfile)
        };

        public override BuildStepResult RunBuildStep(BuildContext context)
        {
            m_tempFileTracker = new TemporaryFileTracker();

            string output =
                "using Unity.Entities;\n" +
                "namespace Latios.PhysicsEngine.BurstPatch\n" +
                "{\n" +
                "   class BurstPatchSystem : SubSystem\n" +
                "   {\n" +
                "       protected override void OnUpdate()\n" +
                "       {\n" +
                "           if (Enabled == true)\n" +
                "           {\n" +
                "               Enabled = false;\n" +
                "               return;\n" +
                "           }\n";

            output += BuildJobInstances(out List<string> assembliesToReference);

            output +=
                "       }\n" +
                "   }\n" +
                "}\n" +
                "\n";

            //File.WriteAllText("Temp/LatiosPhysicsBurstPatchScript.cs", output);
            Directory.CreateDirectory("Assets/LatiosGenerated");
            File.WriteAllText("Assets/LatiosGenerated/LatiosPhysicsBurstPatchScript.cs", output);
            WriteAssemblyDef("Assets/LatiosGenerated/Latios.Physics.BurstPatch.asmdef", assembliesToReference);
            AssetDatabase.ImportAsset("Assets/LatiosGenerated/Latios.Physics.BurstPatch.asmdef");
            AssetDatabase.ImportAsset("Assets/LatiosGenerated/LatiosPhysicsBurstPatchScript.cs");

            /*
               //var    builder  = new AssemblyBuilder("Library/ScriptAssemblies/Latios.Physics.BurstPatch.dll", "Temp/LatiosPhysicsBurstPatchScript.cs");
               var    builder  = new AssemblyBuilder("Assets/LatiosGenerated/Latios.Physics.BurstPatch.dll", "Temp/LatiosPhysicsBurstPatchScript.cs");
               string debugOut = "";
               //foreach (var s in builder.defaultReferences)
               //    debugOut += $"{s}\n";
               //return Failure($"Default assemblies: {debugOut}");
               builder.additionalReferences = assembliesToReference.ToArray();

               builder.buildFinished += Builder_buildFinished;

               if (!builder.Build())
               {
                return Failure("Failed to start build of Latios.Physics.BurstPatch.dll");
               }

               while (builder.status != AssemblyBuilderStatus.Finished)
                System.Threading.Thread.Sleep(10);

               if (m_buildErrors != 0)
               {
                return Failure($"Encountered {m_buildErrors} build errors during compilation of Latios.Physics.BurstPatch.dll: {m_firstBuildError}");
               }
               //context.BuildManifest.Add(Guid.NewGuid(), "Library/ScriptAssemblies/Latios.Physics.BurstPatch.dll",
               //                       new FileInfo[] { new FileInfo("Temp/LatiosPhysicsBurstPatchScript.cs") });
               AssetDatabase.ImportAsset("Assets/LatiosGenerated/Latios.Physics.BurstPatch.dll");
             */

            return Success();
            //return Failure("Not fully implemented yet");
        }

        private void Builder_buildFinished(string assemblyPath, UnityEditor.Compilation.CompilerMessage[] compilerMessages)
        {
            var errorCount   = compilerMessages.Count(m => m.type == CompilerMessageType.Error);
            var warningCount = compilerMessages.Count(m => m.type == CompilerMessageType.Warning);

            m_buildErrors = errorCount;

            foreach (var message in compilerMessages)
            {
                if (message.type == CompilerMessageType.Error)
                {
                    m_firstBuildError = $"{message.line}: {message.message}";
                    return;
                }
            }
        }

        private string BuildJobInstances(out List<string> assembliesToReference)
        {
            string output         = "";
            var    assemblies     = AppDomain.CurrentDomain.GetAssemblies();
            assembliesToReference = new List<string>();
            int i                 = 0;
            foreach (var assembly in assemblies)
            {
                bool referencesLatiosPhysics = false;
                foreach (var refName in assembly.GetReferencedAssemblies())
                {
                    if (refName.FullName.Contains("Latios.Physics"))
                    {
                        referencesLatiosPhysics = true;
                        break;
                    }
                }
                if (assembly.FullName.Contains("Latios.Physics"))
                    referencesLatiosPhysics = true;

                if (!referencesLatiosPhysics)
                    continue;

                if (assembly.FullName.Contains("BurstPatch"))
                    continue;

                //Not sure how to check that the assembly is an editor assembly so going to check the name for "Editor"
                if (assembly.FullName.Contains("Editor"))
                    continue;

                if (assembly.FullName.Contains("Tests"))
                    continue;

                //IFindPairsProcessor
                var ifpp = typeof(IFindPairsProcessor);

                try
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        if (ifpp.IsAssignableFrom(type) && type != ifpp)
                        {
                            output += $"var job{i++} = new FindPairsInternal.LayerLayerSingle<{type.FullName.Replace("+", ".")}>();\n";
                            output += $"var job{i++} =  new FindPairsInternal.LayerSelfSingle<{type.FullName.Replace("+", ".")}>();\n";
                            output += $"var job{i++} =  new FindPairsInternal.LayerLayerPart1<{type.FullName.Replace("+", ".")}>();\n";
                            output += $"var job{i++} =  new FindPairsInternal.LayerLayerPart2<{type.FullName.Replace("+", ".")}>();\n";
                            output += $"var job{i++} =   new FindPairsInternal.LayerSelfPart1<{type.FullName.Replace("+", ".")}>();\n";
                            output += $"var job{i++} =   new FindPairsInternal.LayerSelfPart2<{type.FullName.Replace("+", ".")}>();\n";
                        }
                    }
                    //string projectAssembly = $"Library/ScriptAssemblies/{assembly.GetName().Name}.dll";
                    //string packageAssembly = $"Library/"
                    string projectAssembly = assembly.GetName().Name;
                    assembliesToReference.Add(projectAssembly);
                }
                catch
                {
                }
            }
            return output;
        }

        private void WriteAssemblyDef(string filenameWithPath, List<string> assembliesToRef)
        {
            string output =
                "{" +
                "   \"name\": \"Latios.Physics.BurstPatch\",\n" +
                "   \"references\": [\n" +
                "       \"Unity.Entities\",\n" +
                "       \"Latios.Core\",\n";

            int i = assembliesToRef.Count;
            foreach (var s in assembliesToRef)
            {
                i--;
                if (i != 0)
                    output += $"        \"{s}\",\n";
                else
                    output += $"        \"{s}\"\n";
            }

            output +=
                "   ],\n" +
                "   \"includePlatforms\": [],\n" +
                "   \"excludePlatforms\": [],\n" +
                "   \"allowUnsafeCode\": true,\n" +
                "   \"overrideReferences\": false,\n" +
                "   \"precompiledReferences\": [],\n" +
                "   \"autoReferenced\": true,\n" +
                "   \"defineConstraints\": [],\n" +
                "   \"versionDefines\": [],\n" +
                "   \"noEngineReferences\": false\n" +
                "}";
            File.WriteAllText(filenameWithPath, output);
        }
    }
}

