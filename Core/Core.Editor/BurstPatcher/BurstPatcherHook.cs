using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Security.Permissions;
using Debug = UnityEngine.Debug;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Compilation;

namespace Latios.Editor
{
#if BURST_PATCHER
    internal class BurstPatcherHook : IPostBuildPlayerScriptDLLs
    {
        //Set the callback order to happen before Burst Compilation.
        int IOrderedCallback.callbackOrder => - 1;

        public void OnPostBuildPlayerScriptDLLs(BuildReport report)
        {
            //var step = report.BeginBuildStep("latiosBurstPatch");
            try
            {
                var assemblies           = GetPlayerAssemblies();
                var assemblyStagingPaths = GetStagingAssemblyPaths(assemblies);

                string scriptText = new BurstPatcherGenerator().CreateScript(assemblyStagingPaths);
                File.WriteAllText("Temp/BurstPatchInvocationsSystem.cs", scriptText);

                var dllPath = BurstPatcherCompiler.Compile("Temp/BurstPatchInvocationsSystem.cs", assemblyStagingPaths);
                if (dllPath != null)
                {
                    var dllName = Path.GetFileName(dllPath);
                    File.Copy(dllPath, @"Temp/StagingArea/Data/Managed/" + dllName, true);
                }
            }
            finally
            {
                //report.EndBuildStep(step);
            }
        }

        private static Assembly[] GetPlayerAssemblies()
        {
            // We need to build the list of root assemblies based from the "PlayerScriptAssemblies" folder.
            // This is so we compile the versions of the library built for the individual platforms, not the editor version.

            //Unfortunately, Unity likes to make Burst access private API. So we have to use hackier methods.

            Type typeOfInterface = null;
            foreach (var t in typeof(CompilationPipeline).Assembly.GetTypes())
            {
                if (t.FullName.Contains("EditorCompilationInterface") && !t.FullName.Contains("+"))
                {
                    typeOfInterface = t;
                }
            }
            //var typeOfInterface = typeof(CompilationPipeline).Assembly.GetType("EditorCompilationInterface", true);
            var oldOutputDir = typeOfInterface.GetMethod("GetCompileScriptsOutputDirectory",
                                                         System.Reflection.BindingFlags.Static |
                                                         System.Reflection.BindingFlags.NonPublic |
                                                         System.Reflection.BindingFlags.Public).Invoke(null, null);
            try
            {
                typeOfInterface.GetMethod("SetCompileScriptsOutputDirectory",
                                          System.Reflection.BindingFlags.Static |
                                          System.Reflection.BindingFlags.NonPublic |
                                          System.Reflection.BindingFlags.Public).Invoke(null,
                                                                                        new object[] { "Library/PlayerScriptAssemblies" });
                return CompilationPipeline.GetAssemblies(AssembliesType.PlayerWithoutTestAssemblies);
            }
            finally
            {
                typeOfInterface.GetMethod("SetCompileScriptsOutputDirectory",
                                          System.Reflection.BindingFlags.Static |
                                          System.Reflection.BindingFlags.NonPublic |
                                          System.Reflection.BindingFlags.Public).Invoke(null,
                                                                                        new object[] { oldOutputDir });
            }
        }

        private static List<string> GetStagingAssemblyPaths(Assembly[] assemblies)
        {
            const string tempStagingManagedDirectoryPath = @"Temp/StagingArea/Data/Managed/";

            // --------------------------------------------------------------------------------------------------------
            // 2) Calculate root assemblies
            // These are the assemblies that the compiler will look for methods to compile
            // This list doesn't typically include .NET runtime assemblies but only assemblies compiled as part
            // of the current Unity project
            // --------------------------------------------------------------------------------------------------------
            var rootAssemblies = new List<string>();
            foreach (var playerAssembly in assemblies)
            {
                // the file at path `playerAssembly.outputPath` is actually not on the disk
                // while it is in the staging folder because OnPostBuildPlayerScriptDLLs is being called once the files are already
                // transferred to the staging folder, so we are going to work from it but we are reusing the file names that we got earlier
                var playerAssemblyPathToStaging = Path.Combine(tempStagingManagedDirectoryPath, Path.GetFileName(playerAssembly.outputPath));
                if (!File.Exists(playerAssemblyPathToStaging))
                {
                    Debug.LogWarning($"Unable to find player assembly: {playerAssemblyPathToStaging}");
                }
                else
                {
                    rootAssemblies.Add(playerAssemblyPathToStaging);
                }
            }

            //Append System dependencies
            List<string> systemAssemblyNames = new List<string>();
            systemAssemblyNames.Add("System.dll");
            systemAssemblyNames.Add("System.Core.dll");
            systemAssemblyNames.Add("System.Runtime.dll");
            systemAssemblyNames.Add("mscorlib.dll");
            systemAssemblyNames.Add("netstandard.dll");

            foreach (var asm in systemAssemblyNames)
            {
                var pathToStaging = Path.Combine(tempStagingManagedDirectoryPath, asm);
                if (!File.Exists(pathToStaging))
                {
                    Debug.LogWarning($"Unable to find systme assembly: {pathToStaging}");
                }
                else
                {
                    rootAssemblies.Add(pathToStaging);
                }
            }

            return rootAssemblies;
        }
    }
#endif
}

