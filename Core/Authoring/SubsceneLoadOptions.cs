using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Latios
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Latios/Scene Management/Subscene Load Options (Latios Core)")]
    public class SubsceneLoadOptions : MonoBehaviour
    {
        public enum LoadOptions
        {
            Synchronous,
            Asynchronous
        }

        public LoadOptions loadOptions;
    }
}

