using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Unity.Entities.CodeGen.LatiosPatches
{
    internal class MethodReplacementsPP : EntitiesILPostProcessor
    {
        protected override bool PostProcessImpl(TypeDefinition[] componentSystemTypes)
        {
            if (AssemblyDefinition.Name.Name != "Unity.Entities")
                return false;

            var csgTypeDef = AssemblyDefinition.MainModule.GetType("Unity.Entities", "ComponentSystemGroup");

            var onUpdateMethod = csgTypeDef.Methods.Single(x =>
            {
                return x.Name == "OnUpdate";
            });
            var latiosUpdateAllSystemsReplacement = csgTypeDef.Methods.Single(x =>
            {
                return x.Name == "RunDelegateOrDefaultLatiosInjected";
            });

            var instructions = onUpdateMethod.Body.Instructions;
            foreach (var instruction in instructions)
            {
                if (instruction.OpCode == OpCodes.Call)
                {
                    var callDestination = instruction.Operand as MethodReference;
                    if (callDestination.Name == "UpdateAllSystems")
                        instruction.Operand = latiosUpdateAllSystemsReplacement;
                }
            }
            return true;
        }

        protected override bool PostProcessUnmanagedImpl(TypeDefinition[] unmanagedComponentSystemTypes)
        {
            return false;
        }
    }
}

