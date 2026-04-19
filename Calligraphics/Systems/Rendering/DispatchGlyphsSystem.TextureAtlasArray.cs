using System;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Latios.Calligraphics.Systems
{
    public partial class DispatchGlyphsSystem
    {
        // Todo: We're doing things the easy way for now. We may want to optimize it in the future to
        // send pixel updates to a compute shader and apply changes on the GPU. However, that is somewhat
        // platform-specific because platforms are inconsistent on whether the first scanline is on top or
        // bottom.
        unsafe class TextureAtlasArray<T> : IDisposable where T : unmanaged
        {
            RenderTexture       renderTexture2DArray = null;
            RenderTexture       oldRenderArray       = null;
            int                 shaderPropertyId;
            int                 dimension;
            int                 atlasCount;
            RenderTextureFormat renderFormat;
            bool                useMipmapping;
            bool                linear;

            public TextureAtlasArray(int shaderPropertyId, int dimension, int initialAtlasCount, RenderTextureFormat format, bool useMipmapping, bool linear)
            {
                this.shaderPropertyId = shaderPropertyId;
                this.dimension        = dimension;
                this.atlasCount       = initialAtlasCount;
                this.useMipmapping    = useMipmapping;
                this.linear           = linear;

                this.renderFormat                      = format;
                var rtrw                               = linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB;
                renderTexture2DArray                   = new RenderTexture(dimension, dimension, initialAtlasCount, renderFormat, rtrw);
                renderTexture2DArray.dimension         = TextureDimension.Tex2DArray;
                renderTexture2DArray.volumeDepth       = initialAtlasCount;
                renderTexture2DArray.enableRandomWrite = true;
                renderTexture2DArray.useMipMap         = useMipmapping;
                renderTexture2DArray.autoGenerateMips  = false;

                CommandBuffer cmd = new CommandBuffer();
                cmd.SetRenderTarget(new RenderTargetIdentifier(renderTexture2DArray));
                cmd.ClearRenderTarget(true, true, Color.clear);
                Graphics.ExecuteCommandBuffer(cmd);
                cmd.Dispose();
            }

            public void Dispose()
            {
                if (renderTexture2DArray == null)
                    return;
                if (Application.isPlaying)
                {
                    renderTexture2DArray.Release();
                    UnityEngine.Object.Destroy(renderTexture2DArray);
                }
                else
                {
                    renderTexture2DArray.Release();
                    UnityEngine.Object.DestroyImmediate(renderTexture2DArray);
                }
            }

            // Todo: We no longer care about which indices are dirty. We only care about whether we need to grow the atlas.
            public void ReportDirtyIndices(ReadOnlySpan<uint> dirtyIndicesSorted)
            {
                var atlasesNeeded = 1 + (int)(dirtyIndicesSorted[^ 1] & 0x3fffffffu);
                if (atlasesNeeded >= atlasCount)
                {
                    oldRenderArray                         = renderTexture2DArray;
                    var rtrw                               = linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB;
                    renderTexture2DArray                   = new RenderTexture(dimension, dimension, atlasesNeeded, renderFormat, rtrw);
                    renderTexture2DArray.dimension         = TextureDimension.Tex2DArray;
                    renderTexture2DArray.volumeDepth       = atlasesNeeded;
                    renderTexture2DArray.enableRandomWrite = true;
                    renderTexture2DArray.useMipMap         = useMipmapping;
                    renderTexture2DArray.autoGenerateMips  = false;

                    for (int i = 0; i < atlasesNeeded; i++)
                    {
                        Graphics.CopyTexture(oldRenderArray, math.min(i, atlasCount - 1), renderTexture2DArray, i);
                    }

                    atlasCount = atlasesNeeded;
                }
            }

            public void ApplyChanges()
            {
                if (useMipmapping)
                    renderTexture2DArray.GenerateMips();
                Shader.SetGlobalTexture(shaderPropertyId, renderTexture2DArray);
                if (oldRenderArray != null)
                {
                    oldRenderArray.Release();
                    if (Application.isPlaying)
                        UnityEngine.Object.Destroy(oldRenderArray);
                    else
                        UnityEngine.Object.DestroyImmediate(oldRenderArray);
                    oldRenderArray = null;
                }
            }

            public RenderTexture GetRenderTextureForUpload() => renderTexture2DArray;
        }
    }
}

