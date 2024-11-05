using UnityEngine;

namespace Latios
{
    public static class UnityEngineObjectExtensions
    {
        public static void DestroySafelyFromAnywhere(this Object unityEngineObject)
        {
#if UNITY_EDITOR
            if (Application.isPlaying)
            {
                Object.Destroy(unityEngineObject);
            }
            else
            {
                Object.DestroyImmediate(unityEngineObject);
            }
#else
            Object.Destroy(unityEngineObject);
#endif
        }
    }
}

