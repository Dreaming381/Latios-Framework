using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Myri.Driver
{
    internal static class DriverSelection
    {
        public static bool useManagedDriver
        {
#if !UNITY_EDITOR
            get => false;
#else
            get => UnityEditor.EditorPrefs.GetBool(editorPrefName);
            private set => UnityEditor.EditorPrefs.SetBool(editorPrefName, value);
#endif
        }

#if UNITY_EDITOR
        static string editorPrefName = $"{UnityEditor.PlayerSettings.productName}_MyriEditorManagedDriver";
        const string menuPath        = "Edit/Latios/Use Myri Editor Managed Driver";

        [UnityEditor.InitializeOnLoadMethod]
        public static void InitToggle() => UnityEditor.Menu.SetChecked(menuPath, useManagedDriver);

        [UnityEditor.MenuItem(menuPath)]
        public static void ToggleDriver()
        {
            var currentState = useManagedDriver;
            currentState     = !currentState;
            useManagedDriver = currentState;
            UnityEditor.Menu.SetChecked(menuPath, currentState);
        }
#endif
    }
}

