using System.Collections;
using System.Collections.Generic;
using UnityEditor;

internal class ScriptTemplateMenus
{
    public const string TemplatesRootSolo      = "Packages/com.latios.core/Core.Editor/ScriptTemplates";
    public const string TemplatesRootFramework = "Packages/com.latios.latiosframework/Core/Core.Editor/ScriptTemplates";
    public const string TemplatesRootAssets    = "Assets/_Code/Core.Editor/ScriptTemplates";

    [MenuItem("Assets/Create/Latios/Bootstrap/Minimal - Injection Workflow")]
    public static void CreateMinimalInjectionBootstrap()
    {
        CreateScriptFromTemplate("MinimalInjectionBootstrap.txt", "LatiosBootstrap.cs");
    }

    [MenuItem("Assets/Create/Latios/Bootstrap/Minimal - Explicit Workflow")]
    public static void CreateMinimalExplicitBootstrap()
    {
        CreateScriptFromTemplate("MinimalExplicitBootstrap.txt", "LatiosBootstrap.cs");
    }

    [MenuItem("Assets/Create/Latios/Bootstrap/Standard - Injection Workflow")]
    public static void CreateStandardInjectionBootstrap()
    {
        CreateScriptFromTemplate("StandardInjectionBootstrap.txt", "LatiosBootstrap.cs");
    }

    [MenuItem("Assets/Create/Latios/Bootstrap/Standard - Explicit Workflow")]
    public static void CreateStandardExplicitBootstrap()
    {
        CreateScriptFromTemplate("StandardExplicitBootstrap.txt", "LatiosBootstrap.cs");
    }

    [MenuItem("Assets/Create/Latios/Bootstrap/Dreaming Specialized")]
    public static void CreateDreamingBootstrap()
    {
        CreateScriptFromTemplate("DreamingBootstrap.txt", "LatiosBootstrap.cs");
    }

    [MenuItem("Assets/Create/Latios/SubSystem")]
    public static void CreateSubSystem()
    {
        CreateScriptFromTemplate("SubSystem.txt", "NewSubSystem.cs");
    }

    [MenuItem("Assets/Create/Latios/ISystem")]
    public static void CreateISystem()
    {
        CreateScriptFromTemplate("ISystem.txt", "NewBurstSystem.cs");
    }

#if NETCODE_PROJECT
    [MenuItem("Assets/Create/Latios/Bootstrap/NetCode Minimal - Injection Workflow")]
    public static void CreateNetCodeMinimalInjectionBootstrap()
    {
        CreateScriptFromTemplate("NetCodeMinimalInjectionBootstrap.txt", "NetCodeLatiosBootstrap.cs");
    }

    [MenuItem("Assets/Create/Latios/Bootstrap/NetCode Standard - Injection Workflow")]
    public static void CreateNetCodeStandardInjectionBootstrap()
    {
        CreateScriptFromTemplate("NetCodeStandardInjectionBootstrap.txt", "NetCodeLatiosBootstrap.cs");
    }
#endif

    public static void CreateScriptFromTemplate(string templateName, string defaultScriptName)
    {
        bool success = true;
        try
        {
            ProjectWindowUtil.CreateScriptAssetFromTemplateFile(
                $"{TemplatesRootFramework}/{templateName}",
                defaultScriptName);
        }
        catch (System.IO.FileNotFoundException)
        {
            success = false;
        }
        if (!success)
        {
            success = true;
            try
            {
                ProjectWindowUtil.CreateScriptAssetFromTemplateFile(
                    $"{TemplatesRootSolo}/{templateName}",
                    defaultScriptName);
            }
            catch (System.IO.FileNotFoundException)
            {
                success = false;
            }
        }
        if (!success)
        {
            //success = true;
            try
            {
                ProjectWindowUtil.CreateScriptAssetFromTemplateFile(
                    $"{TemplatesRootAssets}/{templateName}",
                    defaultScriptName);
            }
            catch (System.IO.FileNotFoundException)
            {
                //success = false;
            }
        }
    }
}

