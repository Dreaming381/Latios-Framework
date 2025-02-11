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

            var callsiteMethod           = dwiTypeDef.Methods.Single(x => { return x.Name == "Initialize"; });
            var latiosReplacement        = ewioTypeDef.Methods.Single(x => { return x.Name == "CreateBootstrap"; });
            var latiosReplacementFlipped = ewioTypeDef.Methods.Single(x => { return x.Name == "CreateBootstrapFlipped"; });

            var editorBoolField = ewioTypeDef.Fields.Single(x => { return x.Name == "s_isEditorWorld"; });

            var instructions = callsiteMethod.Body.Instructions;
            foreach (var instruction in instructions)
            {
                if (instruction.OpCode == OpCodes.Call)
                {
                    var callDestination = instruction.Operand as MethodReference;
                    if (callDestination.Name == "CreateBootStrap")
                    {
                        // DefaultWorldInitialization's implementation has an `if (!editorWorld)` and the block inside
                        // sets up the bootstrap. We want this branch to always run, and instead capture the editorWorld
                        // parameter and send it to a custom method for setting up the bootstrap.
                        //
                        // One of two things can happen in the IL. Normally, the compiler will emit a Brtrue on the editorWorld
                        // directly from the argument to jump over the bootstrap creator. However, a potential debug-mode
                        // path has been discovered where instead the editorWorld bool value will be inverted in a local variable
                        // and use Brfalse. Additionally, this debug-mode path will scatter Nops everywhere.
                        var branch = instruction.Previous;
                        for (int i = 0; i < 5; i++)
                        {
                            if (branch.OpCode == OpCodes.Brtrue_S)
                            {
                                instruction.Operand = latiosReplacement;
                                branch.OpCode       = OpCodes.Stsfld;
                                branch.Operand      = editorBoolField;
                                return;
                            }
                            else if (branch.OpCode == OpCodes.Brfalse_S)
                            {
                                instruction.Operand = latiosReplacementFlipped;
                                branch.OpCode       = OpCodes.Stsfld;
                                branch.Operand      = editorBoolField;
                                return;
                            }
                            branch = branch.Previous;
                        }
                    }
                }
            }
        }
    }
}

