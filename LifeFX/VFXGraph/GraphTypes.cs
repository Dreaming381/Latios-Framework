#if HAS_VFX_GRAPH_RUNTIME

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.VFX;

namespace Latios.LifeFX
{
    [VFXType(VFXTypeAttribute.Usage.Default | VFXTypeAttribute.Usage.GraphicsBuffer)]
    struct VfxQvvs
    {
        public Vector4 a;
        public Vector4 b;
        public Vector4 c;
    }
}
#endif

