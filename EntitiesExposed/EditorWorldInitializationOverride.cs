using System;

namespace Unity.Entities.Exposed
{
    public static class EditorWorldInitializationOverride
    {
        public delegate ICustomBootstrap CreateEditorWorldBootstrapDelegate(bool isEditorWorld);
        public static CreateEditorWorldBootstrapDelegate s_overrideDelegate;

        internal static bool s_isEditorWorld;

        internal static ICustomBootstrap CreateBootstrap()
        {
            if (s_overrideDelegate != null)
                return s_overrideDelegate(s_isEditorWorld);

            if (s_isEditorWorld)
                return null;

            // The following was copied and pasted from DefaultWorldInitialization.cs CreateBootStrap()
            var  bootstrapTypes = TypeManager.GetTypesDerivedFrom(typeof(ICustomBootstrap));
            Type selectedType   = null;

            foreach (var bootType in bootstrapTypes)
            {
                if (bootType.IsAbstract || bootType.ContainsGenericParameters)
                    continue;

                if (selectedType == null)
                    selectedType = bootType;
                else if (selectedType.IsAssignableFrom(bootType))
                    selectedType = bootType;
                else if (!bootType.IsAssignableFrom(selectedType))
                    Debug.LogError("Multiple custom ICustomBootstrap specified, ignoring " + bootType);
            }
            ICustomBootstrap bootstrap = null;
            if (selectedType != null)
                bootstrap = Activator.CreateInstance(selectedType) as ICustomBootstrap;

            return bootstrap;
        }

        internal static ICustomBootstrap CreateBootstrapFlipped()
        {
            s_isEditorWorld = !s_isEditorWorld;
            return CreateBootstrap();
        }
    }
}

