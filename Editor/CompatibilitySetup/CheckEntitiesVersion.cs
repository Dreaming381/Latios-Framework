namespace Latios.Editor.CompatibilitySetup
{
#if ENTITIES_1_4 && !LATIOS_ENTITIES_1_4
#error \
    Latios Framework 0.14.x support for Entities 1.4.x is experimental due to IAspect being deprecated without any alternative in that version. If you would like to use a fully supported version, please downgrade Unity Entities to version 1.3.14. For concerns about the framework or to report compatibility issues, please reach out on the Latios Framework discord server. For concerns about the deprecation of IAspect, please reach out on the Unity forums, Unity discord, or any other Unity communication channel. If you wish to use Entities 1.4.2 anyways, please add the scripting define LATIOS_ENTITIES_1_4 to your project.
#endif

    public static class CheckEntitiesVersion
    {
        public static bool IsEntities_1_4_2_Plus
        {
            get
            {
#if ENTITIES_1_4
                return true;    return true;
#else
                return false;
#endif
            }
        }
    }
}

