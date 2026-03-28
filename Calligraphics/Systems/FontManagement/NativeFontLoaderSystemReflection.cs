using System;
using System.IO;
using System.Reflection;
using Latios.Calligraphics.HarfBuzz;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

using Font = Latios.Calligraphics.HarfBuzz.Font;

namespace Latios.Calligraphics.Systems
{
    [DisableAutoCreation]
    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    [RequireMatchingQueriesForUpdate]
    [UpdateInGroup(typeof(Latios.Systems.LatiosWorldSyncGroup), OrderLast = true)]
    [BurstCompile]
    public partial struct NativeFontLoaderSystem : ISystem
    {
        LatiosWorldUnmanaged latiosWorld;
        static MethodInfo    sMethodInfo;
        static FieldInfo[]   sFontLoadDescription;
        static object        sFontRef;

        public void OnCreate(ref SystemState state)
        {
            latiosWorld             = state.GetLatiosWorldUnmanaged();
            var perThreadFontCaches = new NativeArray<UnsafeList<Font> >(JobsUtility.ThreadIndexCount, Allocator.Persistent);
            for (int i = 0; i < perThreadFontCaches.Length; i++)
            {
                perThreadFontCaches[i] = new UnsafeList<Font>(64, Allocator.Persistent);
            }
            latiosWorld.worldBlackboardEntity.AddOrSetCollectionComponentAndDisposeOld(new FontTable
            {
                faces                                 = new NativeList<Face>(Allocator.Persistent),
                perThreadFontCaches                   = perThreadFontCaches,
                fontLookupKeys                        = new NativeList<FontLookupKey>(Allocator.Persistent),
                fontLookupKeyToFaceIndexMap           = new NativeHashMap<FontLookupKey, int>(64, Allocator.Persistent),
                fontLookupKeyToNamedVariationIndexMap = new NativeHashMap<FontLookupKey, int>(64, Allocator.Persistent),
            });

            GetSystemFontsMethod();
        }

        public void OnUpdate(ref SystemState state)
        {
            NativeArray<NativeArray<FontLoadDescription> > batchedLoadDescriptions = default;
            FontTable                                      fontTable               = default;
            DoFindFontsToLoad(ref state, ref this, ref batchedLoadDescriptions, ref fontTable);

            foreach (var batch in batchedLoadDescriptions)
                LoadFont(batch, ref state, ref fontTable);
        }

        void GetSystemFontsMethod()
        {
            Assembly textCoreFontEngineModule = default;
            foreach (Assembly loadedAssembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (loadedAssembly.GetName().Name == "UnityEngine.TextCoreFontEngineModule")
                {
                    textCoreFontEngineModule = loadedAssembly;
                    FontEngine.GetSystemFontNames();
                    UnityEngine.Font.GetPathsToOSFonts();
                    //Debug.Log($"Found UnityEngine.TextCoreFontEngineModule in loaded assemblies: {loadedAssembly.FullName}");
                    break;
                }
            }
            var fontLoadDescriptionType = textCoreFontEngineModule.GetType("UnityEngine.TextCore.LowLevel.FontLoadDescription");
            sFontLoadDescription        = fontLoadDescriptionType.GetFields();
            var m_fontRef               = Activator.CreateInstance(fontLoadDescriptionType);

            BindingFlags bindingFlags = BindingFlags.NonPublic | BindingFlags.Static;
            sMethodInfo               = typeof(FontEngine).GetMethod("TryGetSystemFontLoadDescription", bindingFlags);
            //MakeDelegate<sFontLoadDescription>(sMethodInfo);
        }
        static Func<string, string, object, bool> MakeDelegate<U>(MethodInfo methodInfo)
        {
            var f = (Func<string, string, U, bool>)Delegate.CreateDelegate(typeof(Func<string, string, U, bool>), methodInfo);
            return (a, b, c) => f(a, b, (U)c);
        }

        void LoadFont(NativeArray<FontLoadDescription> fontLoadDescriptions, ref SystemState state, ref FontTable fontTable)
        {
            Blob   blob;
            string fontAssetPath;
            var    firstFontLoadDescription = fontLoadDescriptions[0];

            if (firstFontLoadDescription.isSystemFont)
            {
                //loading rules: https://www.high-logic.com/fontcreator/manual15/fonttype.html

                var      typeographicFamilyDataMissing = (firstFontLoadDescription.typographicFamily.IsEmpty || firstFontLoadDescription.typographicSubfamily.IsEmpty);
                var      family                        = typeographicFamilyDataMissing ? firstFontLoadDescription.fontFamily : firstFontLoadDescription.typographicFamily;
                var      subFamily                     = typeographicFamilyDataMissing ? firstFontLoadDescription.fontSubFamily : firstFontLoadDescription.typographicSubfamily;
                object[] args                          = new object[] { family.ToString(), subFamily.ToString(), sFontRef };
                var      systemFontFound               = (bool)sMethodInfo.Invoke(null, args);
                var      result                        = args[2];

                //if (!TryGetSystemFontLoadDescription(family.ToString(), subFamily.ToString(), out UnityFontLoadDescription unityFontLoadDescription))
                if (!systemFontFound)
                {
                    //Debug.Log($"Could not find system font {sFontLoadDescription.fontFamily} {sFontLoadDescription.fontSubFamily}");
                    return;
                }
                //Debug.Log($"Found {fieldInfos[0].GetValue(result)} {fieldInfos[1].GetValue(result)} {fieldInfos[2].GetValue(result)} {fieldInfos[3].GetValue(result)}");
                fontAssetPath = (string)sFontLoadDescription[3].GetValue(result);
            }
            else
            {
                if (firstFontLoadDescription.streamingAssetLocationValidated)
                    fontAssetPath = Path.Combine(Application.streamingAssetsPath, firstFontLoadDescription.filePath.ToString());
                else
                    fontAssetPath = firstFontLoadDescription.filePath.ToString();

                if (!File.Exists(fontAssetPath))
                {
                    //Debug.Log($"Could not find font in {fontAssetPath}");
                    return;
                }
            }

            blob = new Blob(fontAssetPath);
            blob.MakeImmutable();  //is this neccessary considering we dispose the blob in next instruction?

            // in case font file is a collection font, we load all the ones we want in a batch to avoid reopening the file.
            for (int i = 0, ii = fontLoadDescriptions.Length; i < ii; i++)
            {
                var tempFontLoadDescription = fontLoadDescriptions[i];
                var tempFontLookupKey       = tempFontLoadDescription.fontLookupKey;
                if (!fontTable.fontLookupKeyToFaceIndexMap.ContainsKey(tempFontLookupKey))
                {
                    var id = fontTable.fontLookupKeyToFaceIndexMap.Count;
                    fontTable.fontLookupKeys.Add(tempFontLookupKey);
                    fontTable.fontLookupKeyToFaceIndexMap.Add(tempFontLookupKey, id);
                    var face = new Face(blob, tempFontLoadDescription.faceIndexInFile);
                    face.MakeImmutable();
                    fontTable.faces.Add(face);

                    for (int k = 0, kk = fontTable.perThreadFontCaches.Length; k < kk; k++)
                    {
                        var list = fontTable.perThreadFontCaches[k];
                        list.Add(default);
                        fontTable.perThreadFontCaches[k] = list;
                    }

                    //setup lookup of named variable instance
                    if (face.HasVarData)
                    {
                        var axisCount = (int)face.AxisCount;

                        //fetch a list of all variation axis
                        Span<AxisInfo> axisInfos = stackalloc AxisInfo[axisCount];
                        face.GetAxisInfos(0, 0, ref axisInfos, out _);
                        AxisInfo axisInfo;
                        float    coord;

                        //fetch a list of named variants
                        //Debug.Log($"found {axisCount} variation axis for font {sFontLoadDescription.fontFamily} {sFontLoadDescription.fontSubFamily}, {face.NamedInstanceCount} named instances");
                        Span<float> coords = stackalloc float[axisCount];
                        for (int k = 0, kk = (int)face.NamedInstanceCount; k < kk; k++)
                        {
                            face.GetNamedInstanceDesignCoords(k, ref coords, out uint coordLength);
                            var variableFontLookupKey = tempFontLookupKey;
                            for (int f = 0, ff = (int)coordLength; f < ff; f++)
                            {
                                //axisInfos and coords should be aligned in length and order
                                axisInfo = axisInfos[f];
                                coord    = coords[f];
                                switch (axisInfo.axisTag)
                                {
                                    case AxisTag.WIDTH:
                                        variableFontLookupKey.width = coord; break;
                                    case AxisTag.WEIGHT:
                                        variableFontLookupKey.weight = coord; break;
                                    case AxisTag.ITALIC:
                                        variableFontLookupKey.isItalic = (int)coord == 1; break;
                                    case AxisTag.SLANT:
                                        variableFontLookupKey.slant = coord; break;
                                }
                                //Debug.Log($"Add FontLookupKey {tempFontLookupKey} for variation axis: {axisInfo.axisTag} {face.GetName(axisInfo.nameID, language)}, value = {coord}");
                            }
                            fontTable.fontLookupKeyToNamedVariationIndexMap.Add(variableFontLookupKey, k);
                        }
                    }
                }
            }
            //blob can be disposed here, face and font are disposed at world shutdown via FontTable.TryDispose
            blob.Dispose();
        }

        [BurstCompile]
        static void DoFindFontsToLoad(ref SystemState state,
                                      ref NativeFontLoaderSystem system,
                                      ref NativeArray<NativeArray<FontLoadDescription> > batchedLoadDescriptions,
                                      ref FontTable fontTable)
        {
            system.FindFontsToLoad(ref state, ref batchedLoadDescriptions, ref fontTable);
        }

        void FindFontsToLoad(ref SystemState state, ref NativeArray<NativeArray<FontLoadDescription> > batchedLoadDescriptions, ref FontTable fontTable)
        {
            var descList   = new NativeList<FontLoadDescription>(32, state.WorldUpdateAllocator);
            var descSet    = new NativeHashSet<FontLookupKey>(32, state.WorldUpdateAllocator);
            var blobSet    = new NativeHashSet<BlobAssetReference<FontLoadDescriptionsBlob> >(8, state.WorldUpdateAllocator);
            var pathMap    = new NativeHashMap<FixedString512Bytes, int>(32, state.WorldUpdateAllocator);
            var pathCounts = new NativeList<int>(32, state.WorldUpdateAllocator);

            fontTable = latiosWorld.worldBlackboardEntity.GetCollectionComponent<FontTable>(false);
            state.CompleteDependency();

            foreach (var blobRef in SystemAPI.Query<FontLoadDescriptionsBlobReference>().WithOptions(EntityQueryOptions.IncludePrefab |
                                                                                                     EntityQueryOptions.IncludeDisabledEntities).WithChangeFilter<
                         FontLoadDescriptionsBlobReference>())
            {
                if (!blobSet.Add(blobRef.blob))
                    continue;

                var descriptions = blobRef.blob.Value.descriptions.AsSpan();
                foreach (var desc in descriptions)
                {
                    if (!descSet.Add(desc.fontLookupKey))
                        continue;

                    descList.Add(desc);
                    if (pathMap.TryGetValue(desc.filePath, out var index))
                        pathCounts.ElementAt(index)++;
                    else
                    {
                        pathMap.Add(desc.filePath, pathCounts.Length);
                        pathCounts.Add(1);
                    }
                }
            }
            var resultDescs   = CollectionHelper.CreateNativeArray<FontLoadDescription>(descList.Length, state.WorldUpdateAllocator, NativeArrayOptions.UninitializedMemory);
            var resultBatches = CollectionHelper.CreateNativeArray<NativeArray<FontLoadDescription> >(pathCounts.Length,
                                                                                                      state.WorldUpdateAllocator,
                                                                                                      NativeArrayOptions.UninitializedMemory);
            int running = 0;
            for (int i = 0; i < pathCounts.Length; i++)
            {
                var count         = pathCounts[i];
                resultBatches[i]  = resultDescs.GetSubArray(running, count);
                running          += count;
                pathCounts[i]     = 0;
            }

            foreach (var desc in descList)
            {
                var     pathIndex = pathMap[desc.filePath];
                ref var count     = ref pathCounts.ElementAt(pathIndex);
                var     batch     = resultBatches[pathIndex];
                batch[count]      = desc;
                count++;
            }
            batchedLoadDescriptions = resultBatches;
        }
    }
}

