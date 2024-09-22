using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;

namespace Latios.Kinemation
{
    /// <summary>
    /// When present, LOD filtering is performed on a MaterialMeshInfo that uses ranges,
    /// such that a LOD mask for each Material, Mesh, and SubMesh tuple in the range
    /// is tested against the active LOD region stored in the MaterialMeshInfo.
    /// LodCrossfade low-res compliments are computed on-the-fly and the LODCrossfade value
    /// is assumed to always represent a high-res LOD, even if no tuple supports the high-res LOD.
    /// </summary>
    public struct UseMmiRangeLodTag : IComponentData {}

    /// <summary>
    /// A LOD Crossfade factor used by BatchRendererGroup. If disabled,
    /// then LOD Crossfade is turned off for the entity for the specified culling pass.
    /// It should be disabled most of the time.
    /// </summary>
    public struct LodCrossfade : IComponentData, IEnableableComponent
    {
        /// <summary>
        /// An snorm representation of the range [-1, 1] where negative values
        /// result in the complement dither of the absolute value
        /// </summary>
        public byte raw;

        /// <summary>
        /// Sets the crossfade value based on the opacity of the higher resolution
        /// LOD and a bool specifying if it should be complemented for the low-resolution
        /// LOD.
        /// </summary>
        /// <param name="opacity">The high resolution LOD opacity</param>
        /// <param name="isLowRes">Set to true if this entity represents the low-res LOD. Always set to false when using multi-LOD filtering.</param>
        public void SetFromHiResOpacity(float opacity, bool isLowRes)
        {
            opacity    = math.max(opacity, math.EPSILON);
            int snorm  = (int)math.ceil(opacity * 127f);
            snorm      = math.select(snorm, -snorm, isLowRes);
            snorm     &= 0xff;
            raw        = (byte)snorm;
        }

        /// <summary>
        /// Retrieves the hi-res LOD opacity.
        /// </summary>
        public float hiResOpacity
        {
            get
            {
                int reg = raw;
                var pos = math.select(reg, 256 - reg, reg > 128);
                return pos / 127f;
            }
        }

        /// <summary>
        /// Returns a complementary crossfade value for the current crossfade
        /// </summary>
        public LodCrossfade ToComplement()
        {
            int snorm                      = raw;
            snorm                         += math.select(0, 1, snorm == 128);
            snorm                          = (~snorm) + 1;
            snorm                         &= 0xff;
            return new LodCrossfade { raw  = (byte)snorm };
        }
    }

    // Note: You might think it would be better to cache the world-space heights before the culling callbacks.
    // However, we still need the positions for distance calculations.
    /// <summary>
    /// A LOD filtering component that restricts the entity to only render when it is within a specific screen height percentage.
    /// </summary>
    public struct LodHeightPercentages : IComponentData
    {
        // Signs represent the LOD index
        public float localSpaceHeight;
        public half  minPercent;
        public half  maxPercent;
    }

    /// <summary>
    /// A LOD filtering component that restricts the entity to only render when it is within a specific screen height percentage.
    /// This variant also defines additional boundaries that define LOD Crossfade regions.
    /// </summary>
    public struct LodHeightPercentagesWithCrossfadeMargins : IComponentData
    {
        // Signs of first three fields represent the LOD index
        public float localSpaceHeight;
        public half  minPercent;
        public half  maxPercent;
        public half  minCrossFadeEdge;  // if negative, then disable crossfade
        public half  maxCrossFadeEdge;  // if negative, then disable crossfade
    }

    /// <summary>
    /// Specifies that this crossfade should use SpeedTree-style crossfades which only ever renders the higher-res LOD
    /// and morphs it to perceptually match the lower-res LOD.
    /// </summary>
    public struct SpeedTreeCrossfadeTag : IComponentData { }

    /// <summary>
    /// Specifies the height and screen percentages for a 2-LOD entity with the UseMmiRangeLodTag.
    /// This is the best choice for high entity counts, such as projectiles.
    /// </summary>
    public struct MmiRange2LodSelect : IComponentData
    {
        public float height;
        public half  fullLod0ScreenHeightFraction;
        public half  fullLod1ScreenHeightFraction;
    }

    /// <summary>
    /// Static class with utility methods for LOD evaluation (mostly extension methods)
    /// </summary>
    public static class LodUtilities
    {
        const uint kLodMaskInMmi = 0xf << 27;

        /// <summary>
        /// Sets the LOD region in the MaterialMeshInfo which can filter individual (mesh, material, submesh) tuples when UseMmiRangeLodTag is present.
        /// </summary>
        /// <param name="currentHiResLodIndex">The target high-res LOD index, with 0 being the highest resolution and 7 being the lowest resolution</param>
        /// <param name="isCrossfadingWithLowerRes">If true, the LOD is in a crossfade with the adjacent lower resolution region where both are visible
        /// (always false for speed tree)</param>
        public static unsafe void SetCurrentLodRegion(ref this MaterialMeshInfo mmi, int currentHiResLodIndex, bool isCrossfadingWithLowerRes)
        {
            uint                     region = (uint)currentHiResLodIndex * 2 + math.select(0u, 1u, isCrossfadingWithLowerRes);
            fixed (MaterialMeshInfo* mmiPtr = &mmi)
            {
                var     uintPtr     = (uint*)mmiPtr;
                ref var packedData  = ref uintPtr[2];
                packedData         &= ~kLodMaskInMmi;
                packedData         |= (region << 27) & kLodMaskInMmi;
            }
        }

        /// <summary>
        /// Extracts the LOD region from the MaterialMeshInfo
        /// </summary>
        /// <param name="currentHiResLodIndex">The target high-res LOD index, with 0 being the highest resolution and 7 being the lowest resolution</param>
        /// <param name="isCrossfadingWithLowerRes">If true, the LOD is in a crossfade with the adjacent lower resolution region where both are visible
        /// (always false for speed tree)</param>
        public static unsafe void GetCurrentLodRegion(in this MaterialMeshInfo mmi, out int currentHiResLodIndex, out bool isCrossfadingWithLowerRes)
        {
            fixed (MaterialMeshInfo* mmiPtr = &mmi)
            {
                var uintPtr               = (uint*)mmiPtr;
                var packedData            = uintPtr[2];
                var region                = (packedData & kLodMaskInMmi) >> 27;
                currentHiResLodIndex      = (int)(region >> 1);
                isCrossfadingWithLowerRes = (region & 1) != 0;
            }
        }

        /// <summary>
        /// Computes a camera factor from the specified LOD parameters and bias.
        /// Use the result in calls to ViewHeightFrom().
        /// </summary>
        /// <param name="lodParameters">The LOD parameters, which can be obtained from CullingContext on the worldBlackboardEntity</param>
        /// <param name="lodBias">The bias which can be obtained from UnityEngine.QualitySettings.lodBias</param>
        /// <returns>A factor which converts a world-space height into a view-space height</returns>
        public static float CameraFactorFrom(in UnityEngine.Rendering.LODParameters lodParameters, float lodBias)
        {
            var isPerspective = !lodParameters.isOrthographic;
            if (isPerspective)
            {
                return lodBias / (2f * math.tan(math.radians(lodParameters.fieldOfView) / 2f));
            }
            else
            {
                return lodBias / (2f * lodParameters.orthoSize);
            }
        }

        /// <summary>
        /// Computes the view-space height for LOD evaluation.
        /// For orthographic comparisons, you can directly compare this against screen fraction values [0, 1].
        /// For perspective comparisons, you must divide the result by the distance of the renderable to the camera
        /// (or multiply such distance with the screen fraction you are comparing to).
        /// </summary>
        /// <param name="localHeight">The local-space height. "Height actually refers to the largest axis of the AABB.</param>
        /// <param name="scale">The uniform scale of the renderable element</param>
        /// <param name="stretch">The non-uniform stretch of the renderable element</param>
        /// <param name="cameraFactor">A camera factor derived from CameraFactorFrom()</param>
        /// <returns>A view-space height</returns>
        public static float ViewHeightFrom(float localHeight, float scale, float3 stretch, float cameraFactor)
        {
            return cameraFactor * math.abs(localHeight) * math.abs(scale) * math.cmax(math.abs(stretch));
        }
    }
}

