using System.Runtime.CompilerServices;
using UnityEngine;

namespace Latios
{
    public static class AuthoringUtilities
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void DestroySafe(this Object obj)
        {
#if UNITY_EDITOR
            if (Application.isPlaying)
            {
                Object.Destroy(obj);
            }
            else
            {
                Object.DestroyImmediate(obj);
            }
#else
            Object.Destroy(go);
#endif
        }
    }
}