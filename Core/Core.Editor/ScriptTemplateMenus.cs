using System.Collections;
using System.Collections.Generic;
using UnityEditor;

internal class ScriptTemplateMenus
{
    public const string TemplatesRootSolo      = "Packages/com.latios.core/Core.Editor/ScriptTemplates";
    public const string TemplatesRootFramework = "Packages/com.latios.latiosframework/Core/Core.Editor/ScriptTemplates";
    public const string TemplatesRootAssets    = "Assets/_Code/Core.Editor/ScriptTemplates";

    [MenuItem("Assets/Create/Latios/Bootstrap")]
    public static void CreateRuntimeComponentType()
    {
        bool success = true;
        try
        {
            ProjectWindowUtil.CreateScriptAssetFromTemplateFile(
                $"{TemplatesRootFramework}/Bootstrap.txt",
                "LatiosBootstrap.cs");
        }
        catch(System.IO.FileNotFoundException e)
        {
            success = false;
        }
        if (!success)
        {
            success = true;
            try
            {
                ProjectWindowUtil.CreateScriptAssetFromTemplateFile(
                    $"{TemplatesRootSolo}/Bootstrap.txt",
                    "LatiosBootstrap.cs");
            }
            catch (System.IO.FileNotFoundException e)
            {
                success = false;
            }
        }
        if (!success)
        {
            success = true;
            try
            {
                ProjectWindowUtil.CreateScriptAssetFromTemplateFile(
                    $"{TemplatesRootAssets}/Bootstrap.txt",
                    "LatiosBootstrap.cs");
            }
            catch (System.IO.FileNotFoundException e)
            {
                success = false;
            }
        }
    }
}

