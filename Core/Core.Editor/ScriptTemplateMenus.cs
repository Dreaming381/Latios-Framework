using System.Collections;
using System.Collections.Generic;
using UnityEditor;

internal class ScriptTemplateMenus
{
    public const string TemplatesRootSolo      = "Packages/com.latios.core/Core.Editor/ScriptTemplates";
    public const string TemplatesRootFramework = "Packages/com.latios.latiosframework/Core/Core.Editor/ScriptTemplates";
    public const string TemplatesRootAssets    = "Assets/_Code/Core.Editor/ScriptTemplates";

    [MenuItem("Assets/Create/Latios/Bootstrap - Injection Workflow")]
    public static void CreateInjectionBootstrap()
    {
        CreateScriptFromTemplate("InjectionBootstrap.txt", "LatiosBootstrap.cs");
    }

    [MenuItem("Assets/Create/Latios/Bootstrap - Explicit Workflow")]
    public static void CreateExplicitBootstrap()
    {
        CreateScriptFromTemplate("ExplicitBootstrap.txt", "LatiosBootstrap.cs");
    }

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

