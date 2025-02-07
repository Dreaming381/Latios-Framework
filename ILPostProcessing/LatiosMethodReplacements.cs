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

            PatchComponentSystemGroup();
            PatchDefaultWorldInitialization();

            return true;
        }

        protected override bool PostProcessUnmanagedImpl(TypeDefinition[] unmanagedComponentSystemTypes)
        {
            return false;
        }

        void PatchComponentSystemGroup()
        {
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
        }

        void PatchDefaultWorldInitialization()
        {
            var dwiTypeDef  = AssemblyDefinition.MainModule.GetType("Unity.Entities", "DefaultWorldInitialization");
            var ewioTypeDef = AssemblyDefinition.MainModule.GetType("Unity.Entities.Exposed", "EditorWorldInitializationOverride");

            var callsiteMethod    = dwiTypeDef.Methods.Single(x => { return x.Name == "Initialize"; });
            var latiosReplacement = ewioTypeDef.Methods.Single(x => { return x.Name == "CreateBootstrap"; });

            var editorBoolField = ewioTypeDef.Fields.Single(x => { return x.Name == "s_isEditorWorld"; });

            var instructions = callsiteMethod.Body.Instructions;
            foreach (var instruction in instructions)
            {
                if (instruction.OpCode == OpCodes.Call)
                {
                    var callDestination = instruction.Operand as MethodReference;
                    if (callDestination.Name == "CreateBootStrap")
                    {
                        var branch = instruction.Previous;
                        if (branch.OpCode == OpCodes.Brtrue_S)
                        {
                            instruction.Operand = latiosReplacement;
                            branch.OpCode       = OpCodes.Stsfld;
                            branch.Operand      = editorBoolField;
                        }
                    }
                }
            }
        }
    }
}

