//using System;
//using System.IO;
//using Latios.Calligraphics.HarfBuzz;
//using Unity.Collections;
//using Unity.Collections.LowLevel.Unsafe;
//using Unity.Entities;
//using Unity.Jobs;
//using Unity.Jobs.LowLevel.Unsafe;
//using Unity.Scenes;
//using UnityEngine;
//using Font = Latios.Calligraphics.HarfBuzz.Font;

//namespace Latios.Calligraphics
//{

//    // To-Do: re-design to be able to load collection fonts (contains multiple subfamilies),
//    // and variable fonts in response the requested variation axis (width, weight etc)
//    // of TextRenderer (e.g. generate FontRequests after XML tag extraction)

//    //[DisableAutoCreation]
//    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
//    [RequireMatchingQueriesForUpdate]
//    [UpdateInGroup(typeof(InitializationSystemGroup))]
//    [UpdateAfter(typeof(SceneSystemGroup))]
//    partial struct NativeFontLoaderSystemOld : ISystem
//    {
//        EntityQuery changedFontRequestQ;
//        NativeArray<FixedString512Bytes> systemFontsNative;
//        NativeList<FontLoadDescription> systemFontReferences;
//        JobHandle getFontMetadataJob;

//        public void OnCreate(ref SystemState state)
//        {
//            //schedule fetching of metadata for installed system fonts
//            var systemFonts = UnityEngine.Font.GetPathsToOSFonts();

//            systemFontsNative = new NativeArray<FixedString512Bytes>(systemFonts.Length, Allocator.Persistent);
//            for (int i = 0, ii = systemFonts.Length; i < ii; i++)
//                systemFontsNative[i] = new FixedString512Bytes(systemFonts[i]);

//            systemFontReferences = new NativeList<FontLoadDescription>(systemFonts.Length, Allocator.Persistent);
//            getFontMetadataJob = new GetFontMetadataJob()
//            {
//                systemFonts = systemFontsNative,
//                fontReferences = systemFontReferences,
//            }.Schedule();

//            //setup FontTable
//            var perThreadFontCaches = new NativeArray<UnsafeList<Font>>(JobsUtility.ThreadIndexCount, Allocator.Persistent);
//            for (int i = 0; i < perThreadFontCaches.Length; i++)
//                perThreadFontCaches[i] = new UnsafeList<Font>(64, Allocator.Persistent);

//            state.EntityManager.CreateSingleton(new FontTable
//            {
//                faces = new NativeList<Face>(Allocator.Persistent),
//                perThreadFontCaches = perThreadFontCaches,
//                fontLookupKeys = new NativeList<FontLookupKey>(Allocator.Persistent),
//                fontLookupKeyToFaceIndexMap = new NativeHashMap<FontLookupKey, int>(64, Allocator.Persistent),
//                fontLookupKeyToNamedVariationIndexMap = new NativeHashMap<FontLookupKey, int>(64, Allocator.Persistent),
//            });

//            changedFontRequestQ = SystemAPI.QueryBuilder()
//                .WithAll<FontLoadDescription>()
//                .Build();
//            changedFontRequestQ.SetChangedVersionFilter(ComponentType.ReadWrite<FontLoadDescription>());

//            state.RequireForUpdate(changedFontRequestQ);
//        }

//        //[BurstCompile]
//        public void OnUpdate(ref SystemState state)
//        {
//            if (changedFontRequestQ.IsEmpty)
//                return;

//            getFontMetadataJob.Complete();

//            var changedFontRequestBuffer = changedFontRequestQ.GetSingletonBuffer<FontLoadDescription>();
//            var fontTable = SystemAPI.GetSingletonRW<FontTable>().ValueRW;
//            state.CompleteDependency();

//            //copy to nativeArray because LoadFont would invalidate DynamicBuffer due to structural changes
//            var fontLoadDescriptions = CollectionHelper.CreateNativeArray<FontLoadDescription>(changedFontRequestBuffer.AsNativeArray(), state.WorldUpdateAllocator);
//            for (int i = 0, ii = fontLoadDescriptions.Length; i < ii; i++)
//            {
//                var fontLoadDescription = fontLoadDescriptions[i];
//                if (!fontTable.fontLookupKeyToFaceIndexMap.ContainsKey(fontLoadDescription.fontLookupKey))
//                    LoadFont(fontLoadDescription, ref state, ref fontTable);
//            }
//        }

//        public void OnDestroy(ref SystemState state)
//        {
//            SystemAPI.GetSingletonRW<FontTable>().ValueRW.TryDispose(state.Dependency).Complete();
//            systemFontsNative.Dispose();
//            systemFontReferences.Dispose();
//        }

//        void LoadFont(FontLoadDescription fontLoadDescription, ref SystemState state, ref FontTable fontTable)
//        {
//            Blob blob;
//            string fontAssetPath;
//            if (fontLoadDescription.isSystemFont)
//            {
//                //loading rules: https://www.high-logic.com/fontcreator/manual15/fonttype.html
//                if (!TryGetSystemFontReference(fontLoadDescription, out FontLoadDescription systemFontLoadDescription))
//                {
//                    //Debug.Log($"Could not find system font {fontReference.fontFamily} {fontReference.fontSubFamily}");
//                    return;
//                }
//                //Debug.Log($"Found system font {systemFontReference.fontFamily} {systemFontReference.fontSubFamily} {systemFontReference.filePath}");
//                fontAssetPath = systemFontLoadDescription.filePath.ToString();
//            }
//            else
//            {
//                if (fontLoadDescription.streamingAssetLocationValidated)
//                    fontAssetPath = Path.Combine(Application.streamingAssetsPath, fontLoadDescription.filePath.ToString());
//                else
//                    fontAssetPath = fontLoadDescription.filePath.ToString();

//                if (!File.Exists(fontAssetPath))
//                {
//                    //Debug.Log($"Could not find font in {fontAssetPath}");
//                    return;
//                }
//            }

//            //Debug.Log($"Load {fontReference.fontFamily} {fontReference.fontSubFamily} {File.Exists(fontAssetPath)}");
//            blob = new Blob(fontAssetPath);
//            blob.MakeImmutable();//is this neccessary considering we dispose the blob in next instruction?

//            // in case font file is a collection font, chances are that none of the faces have been loaded yet
//            // while file is open, load them all to avoid opening file again
//            var tempFontReferences = new NativeList<FontLoadDescription>(blob.FaceCount, Allocator.Temp);
//            var language = Language.English;
//            TextHelper.GetFaceInfo(blob, language, fontLoadDescription, tempFontReferences);

//            for (int i = 0, ii = tempFontReferences.Length; i < ii; i++)
//            {
//                var tempFontReference = tempFontReferences[i];
//                var tempFontAssetRef = tempFontReference.fontLookupKey;
//                if (!fontTable.fontLookupKeyToFaceIndexMap.ContainsKey(tempFontAssetRef))
//                {
//                    var id = fontTable.fontLookupKeyToFaceIndexMap.Count;
//                    fontTable.fontLookupKeys.Add(tempFontAssetRef);
//                    fontTable.fontLookupKeyToFaceIndexMap.Add(tempFontAssetRef, id);
//                    var face = new Face(blob, tempFontReference.faceIndexInFile);
//                    face.MakeImmutable();
//                    fontTable.faces.Add(face);

//                    for (int k = 0, kk = fontTable.perThreadFontCaches.Length; k < kk; k++)
//                    {
//                        var list = fontTable.perThreadFontCaches[k];
//                        list.Add(default);
//                        fontTable.perThreadFontCaches[k] = list;
//                    }

//                    //setup lookup of named variable instance
//                    if (face.HasVarData)
//                    {
//                        var axisCount = (int)face.AxisCount;

//                        //fetch a list of all variation axis
//                        Span<AxisInfo> axisInfos = stackalloc AxisInfo[axisCount];
//                        face.GetAxisInfos(0, 0, ref axisInfos, out _);
//                        AxisInfo axisInfo;
//                        float coord;

//                        //fetch a list of named variants                        
//                        //Debug.Log($"found {axisCount} variation axis for font {fontReference.fontFamily} {fontReference.fontSubFamily}, {face.NamedInstanceCount} named instances");
//                        Span<float> coords = stackalloc float[axisCount];
//                        for (int k = 0, kk = (int)face.NamedInstanceCount; k < kk; k++)
//                        {
//                            face.GetNamedInstanceDesignCoords(k, ref coords, out uint coordLength);
//                            var variableFontAssetRef = tempFontAssetRef;
//                            for (int f = 0, ff = (int)coordLength; f < ff; f++)
//                            {
//                                //axisInfos and coords should be aligned in length and order
//                                axisInfo = axisInfos[f];
//                                coord = coords[f];
//                                switch (axisInfo.axisTag)
//                                {
//                                    case AxisTag.WIDTH:
//                                        variableFontAssetRef.width = coord; break;
//                                    case AxisTag.WEIGHT:
//                                        variableFontAssetRef.weight = coord; break;
//                                    case AxisTag.ITALIC:
//                                        variableFontAssetRef.isItalic = (int)coord == 1; break;
//                                    case AxisTag.SLANT:
//                                        variableFontAssetRef.slant = coord; break;
//                                }
//                                //Debug.Log($"Add FontAssetRef {tempFontAssetRef} for variation axis: {axisInfo.axisTag} {face.GetName(axisInfo.nameID, language)}, value = {coord}");
//                            }
//                            fontTable.fontLookupKeyToNamedVariationIndexMap.Add(variableFontAssetRef, k);
//                        }
//                    }
//                }
//            }

//            //blob can be disposed here, face and font are disposed at world shutdown via FontTable.TryDispose
//            blob.Dispose();
//        }

//        bool TryGetSystemFontReference(FontLoadDescription query, out FontLoadDescription fontReference)
//        {
//            int index;
//            if ((index = systemFontReferences.IndexOf(query)) != -1)
//            {
//                fontReference = systemFontReferences[index];
//                return true;
//            }
//            fontReference = default;
//            return false;
//        }

//        struct GetFontMetadataJob : IJob
//        {
//            [ReadOnly] public NativeArray<FixedString512Bytes> systemFonts;
//            public NativeList<FontLoadDescription> fontReferences;

//            public void Execute()
//            {
//                var language = Language.English;
//                for (int i = 0, ii = systemFonts.Length; i < ii; i++)
//                    TextHelper.GetFontInfo(systemFonts[i].ToString(), true, language, fontReferences);
//            }
//        }
//    }
//}

