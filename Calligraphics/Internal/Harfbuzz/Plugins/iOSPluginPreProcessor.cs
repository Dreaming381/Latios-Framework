#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Latios.Calligraphics
{

    public class IOSPluginPreProcessor : IPreprocessBuildWithReport
    {
        const string k_PreCompiledLibrary1Name = "libharfbuzz";
        const string k_PreCompiledLibrary2Name = "libharfbuzz-subset";

        public int callbackOrder => 0;

        void IPreprocessBuildWithReport.OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.iOS)
                return;

            SetRuntimePluginCopyDelegate();
        } 


        static void SetRuntimePluginCopyDelegate()
        {
            var allPlugins = PluginImporter.GetAllImporters();
            foreach (var plugin in allPlugins)
            {
                if (!plugin.isNativePlugin)
                    continue;

                // Process pre-compiled library separately. Exactly one version should always be included in the build
                // regardless of whether the loader is enabled. Otherwise, builds will fail in the linker stage
                if (plugin.assetPath.Contains(k_PreCompiledLibrary1Name) || plugin.assetPath.Contains(k_PreCompiledLibrary2Name))
                {
                    //Debug.Log($"{PlayerSettings.iOS.sdkVersion} plugin {plugin.assetPath}: {ShouldIncludePreCompiledLibraryInBuild(plugin.assetPath)}");
                    plugin.SetIncludeInBuildDelegate(ShouldIncludePreCompiledLibraryInBuild);
                    continue;
                }
            }
        }

        static bool ShouldIncludePreCompiledLibraryInBuild(string path)
        {
            // Exclude libraries that don't match the target SDK
            if (PlayerSettings.iOS.sdkVersion == iOSSdkVersion.DeviceSDK)
            {
                if (path.Contains("Simulator"))
                    return false;
            }
            else
            {
                if (path.Contains("Device"))
                    return false;
            }

            return true;
        }
    }
}
#endif