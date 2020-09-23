using System;
using System.Collections.Generic;
using System.Text;
using Debug = UnityEngine.Debug;
using Mono.Cecil;

namespace Latios.Editor
{
    internal class BurstPatcherGenerator
    {
        private List<AssemblyDefinition> m_interestingAssemblies;

        public string CreateScript(List<string> interestingAssemblyPaths)
        {
            m_interestingAssemblies = new List<AssemblyDefinition>(interestingAssemblyPaths.Count);
            foreach(var asmPath in interestingAssemblyPaths)
            {
                m_interestingAssemblies.Add(AssemblyDefinition.ReadAssembly(asmPath));
            }

            string jobs         = BuildJobsAndGetAssemblies(out HashSet<AssemblyDefinition> refAssemblies);
            string infiltrators = BuildInfiltrators(refAssemblies);

            string result =
                $@"using System;
using System.Runtime.CompilerServices;

{infiltrators}
namespace Latios.BurstPatch.Generated
{{
    internal class BurstPatchInvokationsSystem : SubSystem
    {{
        protected override void OnUpdate()
        {{
            if (Enabled == true)
            {{
                Enabled = false;
                return;
            }}
{jobs}
        }}
    }}
}}

namespace System.Runtime.CompilerServices
{{
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    internal class IgnoresAccessChecksToAttribute : Attribute
    {{
        public IgnoresAccessChecksToAttribute(string assemblyName)
        {{
            AssemblyName = assemblyName;
        }}
 
        public string AssemblyName {{ get; }}
    }}
}}
";
            foreach (var asm in m_interestingAssemblies)
            {
                asm.Dispose();
            }
            return result;
        }

        private string BuildJobsAndGetAssemblies(out HashSet<AssemblyDefinition> refAssemblies)
        {
            StringBuilder result = new StringBuilder();
            refAssemblies        = new HashSet<AssemblyDefinition>();

            int count = 0;
            var pairs = FindInterfaceJobPairs();
            pairs.Sort();

            TypeReference lastType = null;
            for (int i = 0; i < pairs.Count; i++)
            {
                var pair = pairs[i];
                if (pair.iface != lastType)
                {
                    refAssemblies.Add(pair.iface.Module.Assembly);
                    var instances = FindAllInstancesOfInterface(pair.iface);
                    for (int j = i; j < pairs.Count; j++)
                    {
                        if (pairs[j].iface != pair.iface)
                            break;

                        var jobName = pairs[j].job.FullName.Replace('+', '.').Replace("`1", "").Replace('/', '.');
                        refAssemblies.Add(pairs[j].job.Module.Assembly);
                        foreach (var instance in instances)
                        {
                            result.Append($"var burstPatchJob{count++} = new {jobName}<{instance.FullName.Replace('+', '.').Replace('/', '.')}>();\n");
                            refAssemblies.Add(instance.Module.Assembly);
                        }
                    }
                }
                lastType = pair.iface;
            }
            return result.ToString();
        }

        private struct InterfaceJobPair : IComparable<InterfaceJobPair>
        {
            public TypeReference iface;
            public TypeReference job;

            public int CompareTo(InterfaceJobPair other)
            {
                return iface.GetHashCode().CompareTo(other.iface.GetHashCode());
            }
        }

        private List<InterfaceJobPair> FindInterfaceJobPairs()
        {
            var results = new List<InterfaceJobPair>();
            foreach (var assembly in m_interestingAssemblies)
            {
                bool referencesLatios = false;
                foreach (var refasm in GetReferencedAssemblies(assembly))
                {
                    if (refasm.FullName.Contains("Latios"))
                    {
                        referencesLatios = true;
                        break;
                    }
                }
                if (assembly.FullName.Contains("Latios"))
                    referencesLatios = true;

                if (referencesLatios)
                {
                    //Debug.Log(assembly);

                    foreach (var t in GetTypes(assembly))
                    {
                        if (!t.HasCustomAttributes)
                            continue;

                        foreach (var att in t.CustomAttributes)
                        {
                            //IgnoreBurstPatcherAttribute does not have constructor arguments.
                            if (att.AttributeType.Name.Contains("BurstPatcherAttribute") && att.HasConstructorArguments)
                            {
                                var typeRef = att.ConstructorArguments[0].Value as TypeReference;
                                if (typeRef == null)
                                {
                                    Debug.LogWarning($"Failed to resolve attribute type name: {att.ConstructorArguments[0].Value}");
                                }
                                else
                                {
                                    results.Add(new InterfaceJobPair
                                    {
                                        iface = typeRef,
                                        job   = t
                                    });
                                }
                            }
                        }
                    }
                }
            }
            return results;
        }

        private List<TypeReference> FindAllInstancesOfInterface(TypeReference iface)
        {
            var results           = new List<TypeReference>();
            var ifaceAssemblyName = iface.Module.Assembly.FullName;
            foreach (var assembly in m_interestingAssemblies)
            {
                bool referencesIFace = false;
                foreach (var refAsm in GetReferencedAssemblies(assembly))
                {
                    if (refAsm.FullName.Contains(ifaceAssemblyName))
                    {
                        //Debug.Log("This asm refs iface: " + assembly);
                        referencesIFace = true;
                        break;
                    }
                }
                if (assembly.FullName.Contains(ifaceAssemblyName))
                {
                    referencesIFace = true;
                }
                if (referencesIFace)
                {
                    foreach (var type in GetTypes(assembly))
                    {
                        if (type == null)
                            continue;

                        if (!type.IsInterface && DoesTypeImplementInterface(type, iface))
                            results.Add(type);
                    }
                }
            }
            return results;
        }

        private string BuildInfiltrators(HashSet<AssemblyDefinition> refAssemblies)
        {
            StringBuilder result = new StringBuilder();
            foreach (var asm in refAssemblies)
            {
                result.Append($"[assembly: IgnoresAccessChecksTo(\"{asm.Name.Name}\")]\n");
            }
            return result.ToString();
        }

        private IEnumerable<AssemblyNameReference> GetReferencedAssemblies(AssemblyDefinition assembly)
        {
            var result = new HashSet<AssemblyNameReference>();
            foreach (var module in assembly.Modules)
            {
                foreach (var asmRef in module.AssemblyReferences)
                {
                    result.Add(asmRef);
                }
            }
            return result;
        }

        private IEnumerable<TypeDefinition> GetTypes(AssemblyDefinition assembly)
        {
            var result = new List<TypeDefinition>();
            foreach (var module in assembly.Modules)
            {
                //module.
                foreach (var type in module.Types)
                {
                    result.Add(type);
                    GetSubTypes(result, type);
                }
            }
            return result;
        }

        private void GetSubTypes(List<TypeDefinition> types, TypeDefinition rootType)
        {
            if (!rootType.HasNestedTypes)
                return;

            foreach (var t in rootType.NestedTypes)
            {
                types.Add(t);
                GetSubTypes(types, t);
            }
        }

        private bool DoesTypeImplementInterface(TypeDefinition type, TypeReference iface)
        {
            foreach (var ifaceImpl in type.Interfaces)
            {
                if (ifaceImpl.InterfaceType.Name == iface.Name)
                    return true;
            }
            return false;
        }
    }
}

