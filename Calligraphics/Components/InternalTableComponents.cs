using System;
using Font = Latios.Calligraphics.HarfBuzz.Font;
using Latios.Calligraphics.HarfBuzz;
using Latios.Unsafe;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.TextCore;

namespace Latios.Calligraphics
{
    internal partial struct FontTable : ICollectionComponent
    {
        public NativeList<Face>                  faces;
        public NativeArray<UnsafeList<Font> >    perThreadFontCaches;
        public NativeHashMap<FontLookupKey, int> fontLookupKeyToFaceIndexMap;
        //variable fonts got names instances. create FontLookupKey for each instance,
        //and map it to index of named instance in face. Use this to lookup instance profile via FontLookupKey
        public NativeHashMap<FontLookupKey, int> fontLookupKeyToNamedVariationIndexMap;
        public NativeList<FontLookupKey>         fontLookupKeys;

        public Font SetVariableProfile(int faceIndex, int threadIndex, int variableProfileIndex)
        {
            var fonts = perThreadFontCaches[threadIndex];
            var font  = fonts[faceIndex];

            font.VariationNamedInstance      = (uint)variableProfileIndex;
            font.currentVariableProfileIndex = variableProfileIndex;
            fonts[faceIndex]                 = font;
            return font;
        }
        public bool GetNamedVariationLookup(FontLookupKey desiredFontAssetRef, out int namedVariationIndex)
        {
            if (fontLookupKeyToNamedVariationIndexMap.ContainsKey(desiredFontAssetRef))
            {
                namedVariationIndex = fontLookupKeyToNamedVariationIndexMap[desiredFontAssetRef];
                return true;
            }

            //fall back to matching at least family and normal/italic
            for (int i = 0, lenght = fontLookupKeys.Length; i < lenght; i++)
            {
                //Debug.Log($"fallback candidate: {fontAssetRefs[i].ToString()}");
                if (fontLookupKeys[i].familyHash == desiredFontAssetRef.familyHash && fontLookupKeys[i].isItalic == desiredFontAssetRef.isItalic)
                {
                    //Debug.Log($"desired: {desiredFontAssetRef}, found fallback candidate: {fontAssetRefs[i]}");
                    namedVariationIndex = fontLookupKeyToFaceIndexMap[fontLookupKeys[i]];
                    return false;
                }
            }
            namedVariationIndex = default;
            return false;
        }

        public int GetFaceIndex(FontLookupKey desiredFontAssetRef)
        {
            //Debug.Log($"Search for: {desiredFontAssetRef}");
            //default: perfect match of family name, weight, width and italic/normal
            var fontIndex = fontLookupKeys.IndexOf(desiredFontAssetRef);
            if(fontIndex != -1)
            {
                //Debug.Log($"desired: {desiredFontAssetRef}, found candidate: {fontAssetRefs[fontIndex]}");
                return fontLookupKeyToFaceIndexMap[fontLookupKeys[fontIndex]];
            }

            //fall back to matching at least family and normal/italic
            for (int i = 0, lenght = fontLookupKeys.Length; i < lenght; i++)
            {
                //Debug.Log($"fallback candidate: {fontAssetRefs[i].ToString()}");
                if (fontLookupKeys[i].familyHash == desiredFontAssetRef.familyHash && fontLookupKeys[i].isItalic == desiredFontAssetRef.isItalic)
                {
                    //Debug.Log($"desired: {desiredFontAssetRef}, found fallback candidate: {fontAssetRefs[i]}");
                    return fontLookupKeyToFaceIndexMap[fontLookupKeys[i]];
                }
            }

            //fall back to matching at least family
            for (int i = 0, lenght = fontLookupKeys.Length; i < lenght; i++)
            {
                //Debug.Log($"fallback candidate: {fontAssetRefs[i].ToString()}");
                if (fontLookupKeys[i].familyHash == desiredFontAssetRef.familyHash)
                    return fontLookupKeyToFaceIndexMap[fontLookupKeys[i]];
            }
            //Debug.Log($"Requested font {desiredFontAssetRef} not found");
            return -1;
        }

        public Font GetOrCreateFont(int faceIndex, int threadIndex)
        {
            var fonts = perThreadFontCaches[threadIndex];
            var font  = fonts[faceIndex];
            if (font.ptr == IntPtr.Zero)
            {
                var face         = faces[faceIndex];
                font             = new Font(face);
                fonts[faceIndex] = font;
            }
            return font;
        }

        public JobHandle TryDispose(JobHandle inputDeps)
        {
            if (faces.IsCreated)
            {
                var jh = new DisposeInnerJob { table = this }.Schedule(inputDeps);
                jh                                   = JobHandle.CombineDependencies(faces.Dispose(jh),
                                                                                     fontLookupKeyToNamedVariationIndexMap.Dispose(jh),
                                                                                     perThreadFontCaches.Dispose(jh));
                return JobHandle.CombineDependencies(jh, fontLookupKeys.Dispose(jh), fontLookupKeyToFaceIndexMap.Dispose(jh));
            }
            return inputDeps;
        }

        struct DisposeInnerJob : IJob
        {
            public FontTable table;

            public void Execute()
            {
                for (int thread = 0; thread < table.perThreadFontCaches.Length; thread++)
                {
                    var list = table.perThreadFontCaches[thread];
                    foreach (var font in list)
                    {
                        if (font.ptr == IntPtr.Zero)
                            continue;
                        font.Dispose();
                    }
                    list.Dispose();
                }
                foreach (var face in table.faces)
                {
                    if (face.ptr == IntPtr.Zero)
                        continue;
                    face.Dispose();
                }

                // We don't need to dispose blobs, as we already "destroy" them upon creation after initializing the face,
                // thus the ref count decremented to 0 after disposing the face.
            }
        }
    }

    internal struct VariableProfile : IEquatable<VariableProfile>
    {
        public FixedList64Bytes<Variation> variations;  // space for 8 variations (8 byte per variation axis)

        public VariableProfile(NativeList<AxisInfo> axisInfos)
        {
            variations = new FixedList64Bytes<Variation>();
            for (int i = 0, ii = axisInfos.Length; i < ii; i++)
            {
                var axisInfo = axisInfos[i];
                variations.Add(new Variation(axisInfo.axisTag, axisInfo.defaultValue));
            }
        }
        public VariableProfile(ref Span<AxisInfo> axisInfos, uint axisCount)
        {
            variations = new FixedList64Bytes<Variation>();
            for (int i = 0; i < axisCount; i++)
            {
                var axisInfo = axisInfos[i];
                variations.Add(new Variation(axisInfo.axisTag, axisInfo.defaultValue));
            }
        }
        public VariableProfile(NativeList<Variation> variationsIN)
        {
            variations = new FixedList64Bytes<Variation>();
            for (int i = 0, ii = variationsIN.Length; i < ii; i++)
                variations.Add(variationsIN[i]);
        }
        public VariableProfile(float weight, float width)
        {
            variations = new FixedList64Bytes<Variation>()
            {
                new Variation(AxisTag.WEIGHT, weight),
                new Variation(AxisTag.WIDTH, width),
            };
        }

        public VariableProfile(float weight, float width, NativeList<AxisInfo> axisInfos)
        {
            variations = new FixedList64Bytes<Variation>();
            for (int i = 0, ii = axisInfos.Length; i < ii; i++)
            {
                var axisInfo = axisInfos[i];
                switch (axisInfo.axisTag)
                {
                    case AxisTag.WIDTH:
                        variations.Add(GetVariation(width, ref axisInfo));
                        break;
                    case AxisTag.WEIGHT:
                        variations.Add(GetVariation(weight, ref axisInfo));
                        break;
                }
            }
        }
        Variation GetVariation(float value, ref AxisInfo axisInfo)
        {
            return new Variation(axisInfo.axisTag, math.clamp(value, axisInfo.minValue, axisInfo.maxValue));
        }
        public override bool Equals(object obj) => obj is VariableProfile other && Equals(other);

        public bool Equals(VariableProfile other)
        {
            return GetHashCode() == other.GetHashCode();
        }

        public static bool operator ==(VariableProfile e1, VariableProfile e2)
        {
            return e1.GetHashCode() == e2.GetHashCode();
        }
        public static bool operator !=(VariableProfile e1, VariableProfile e2)
        {
            return e1.GetHashCode() != e2.GetHashCode();
        }
        public override int GetHashCode()
        {
            int hashCode = 2055808453;
            foreach (var item in variations)
            {
                hashCode = hashCode * -1521134295 + item.GetHashCode();
            }
            return hashCode;
        }
    }
    internal enum RenderFormat : byte
    {
        SDF8 = 0,
        SDF16 = 1,
        Bitmap8888 = 2,
    }
    internal enum GlyphEntryIDFlags : byte
    {
        SDF8NormalSize = 0,
        SDF16BigSize = 1,
        SDF16MassiveSize = 2,
        Bitmap8888 = 3,
    }

    internal partial struct GlyphTable : ICollectionComponent
    {
        public struct Key : IEquatable<Key>, IComparable<Key>
        {
            public ulong packed;

            public ushort glyphIndex
            {
                get => (ushort)Bits.GetBits(packed, 0, 16);
                set => Bits.SetBits(ref packed, 0, 16, value);
            }

            public int faceIndex
            {
                get => (int)Bits.GetBits(packed, 16, 20);
                set => Bits.SetBits(ref packed, 16, 20, (uint)value);
            }

            public RenderFormat format
            {
                get => (RenderFormat)Bits.GetBits(packed, 36, 2);
                set => Bits.SetBits(ref packed, 36, 2, (uint)value);
            }

            public FontTextureSize textureSize
            {
                get => (FontTextureSize)Bits.GetBits(packed, 38, 2);
                set => Bits.SetBits(ref packed, 38, 2, (uint)value);
            }

            public int variableProfileIndex
            {
                get => (int)Bits.GetBits(packed, 40, 24);
                set => Bits.SetBits(ref packed, 40, 24, (uint)value);
            }

            public bool Equals(Key other) => packed.Equals(other.packed);
            public override int GetHashCode() => packed.GetHashCode();
            public int CompareTo(Key other) => packed.CompareTo(other.packed);
        }

        public struct Entry
        {
            public Key   key;
            public int   refCount;
            public short x;
            public short y;
            public short z;
            public short width;
            public short height;
            public short xBearing;
            public short yBearing;
            public short padding;

            public bool isInAtlas => x >= 0;
            public GlyphRect PaddedAtlasRect
            {
                get
                {
                    var doublePadding = 2 * padding;
                    return new GlyphRect(x, y, width + doublePadding, height + doublePadding);
                }
            }
            public BBox ClipRect
            {
                get { return new BBox(xBearing, yBearing - height, xBearing + width, yBearing); }
            }
            public GlyphExtents GlyphExtents
            {
                get { return new GlyphExtents {width = width, height = height, x_bearing = xBearing, y_bearing = yBearing }; }
            }
            // Todo:
        }

        /// <summary>
        /// the upper 2 bit are used by AllocateNewGlyphsJob to store the glyph type:
        /// (00, 01, 10 = SDF, values of FontTextureSize enum for ultimate use in shader to calcualte SDR, 11 = Bitmap8888)
        /// encode using EncodeGlyphEntryIDFlags and decode using DecodeGlyphEntryIDFlags
        /// </summary>
        public NativeHashMap<Key, uint> glyphHashToIdMap;
        public NativeList<Entry>        entries;

        /// <summary>
        /// Flags will be decoded in shader by ExtractGlyphFlagsFromEntryID and used there to determine if glyph is SDF or bitmap,
        /// and to determind the Signed Distance Ratio for SDF (keep in sync with FontEnumerationExtensions.GetSpread()!)
        /// Will also be used to determine which atlas is dirty in DispatchGlyphsSystem.Write
        /// </summary>
        /// <param name="glyphEntryID"></param>
        public static void EncodeGlyphEntryIDFlags(in Key key, ref uint glyphEntryID)
        {
            uint topBits = key.format == RenderFormat.Bitmap8888 ? 3 : (uint)key.textureSize;
            Bits.SetBits(ref glyphEntryID, 30, 2, topBits);
        }
        public static GlyphEntryIDFlags DecodeGlyphEntryIDFlags(uint glyphEntryID)
        {
            return (GlyphEntryIDFlags)(glyphEntryID >> 30);
        }

        public JobHandle TryDispose(JobHandle inputDeps)
        {
            if (glyphHashToIdMap.IsCreated)
            {
                return JobHandle.CombineDependencies(glyphHashToIdMap.Dispose(inputDeps), entries.Dispose(inputDeps));
            }
            return inputDeps;
        }

        public ref Entry GetEntryRW(uint glyphEntryID)
        {
            return ref entries.ElementAt((int)(glyphEntryID & 0x3fffffff));  // Lower 30 bits contain the entry index; upper bits are flags.
        }

        public Entry GetEntry(uint glyphEntryID)
        {
            return entries[(int)(glyphEntryID & 0x3fffffff)];  // Lower 30 bits contain the entry index; upper bits are flags.
        }
        public readonly ref Entry GetEntryRef(uint glyphEntryID)
        {
            return ref entries.ElementAt((int)(glyphEntryID & 0x3fffffff));  // Lower 30 bits contain the entry index; upper bits are flags. zero allocation access for IComparer
        }
    }

    internal partial struct GlyphGpuTable : ICollectionComponent
    {
        public NativeReference<uint> bufferSize;
        public NativeList<uint2>     residentGaps;
        public NativeList<uint2>     dispatchDynamicGaps;  // Deferred gaps when multiple dispatches need to skip over previous dynamic regions

        public JobHandle TryDispose(JobHandle inputDeps)
        {
            if (bufferSize.IsCreated)
            {
                return JobHandle.CombineDependencies(bufferSize.Dispose(inputDeps), residentGaps.Dispose(inputDeps), dispatchDynamicGaps.Dispose(inputDeps));
            }
            return inputDeps;
        }
    }

    internal partial struct AtlasTable : ICollectionComponent
    {
        public AtlasTable(AllocatorManager.AllocatorHandle allocator, int textureDimension, int shelfAlignment)
        {
            sdf8Shelves            = new NativeList<Shelf>(8, allocator);
            sdf16Shelves           = new NativeList<Shelf>(8, allocator);
            bitmapShelves          = new NativeList<Shelf>(8, allocator);
            atlasRemovalCandidates = new NativeHashSet<uint>(256, allocator);
            this.allocator         = allocator;
            dimension              = (uint)textureDimension;
            this.shelfAlignment    = shelfAlignment;
        }

        public JobHandle TryDispose(JobHandle inputDeps)
        {
            var jh  = new DisposeJob { atlasTable = this }.Schedule(inputDeps);
            var jh2                               = JobHandle.CombineDependencies(sdf8Shelves.Dispose(jh), sdf16Shelves.Dispose(jh));
            jh2                                   = JobHandle.CombineDependencies(jh2, atlasRemovalCandidates.Dispose(jh), bitmapShelves.Dispose(jh));
            return jh2;
        }

        public bool TryAllocateNoNewSlice(uint glyphEntryId, short width, short height, out short x, out short y, out short z)
        {
            var format  = (RenderFormat)Bits.GetBits(glyphEntryId, 30, 2);
            var shelves = sdf8Shelves;
            if (format == RenderFormat.SDF16)
                shelves = sdf16Shelves;
            else if (format == RenderFormat.Bitmap8888)
                shelves = bitmapShelves;

            var alignedHeight = CollectionHelper.Align(height, shelfAlignment);
            for (int i = 0; i < shelves.Length; i++)
            {
                var shelf = shelves[i];
                if (shelf.height == alignedHeight)
                {
                    if (shelf.requiresCoellescing)
                    {
                        shelf.reservedX = GapAllocator.CoalesceGaps(ref shelf.gaps, shelf.reservedX);
                    }
                    var found = GapAllocator.TryAllocate(ref shelf.gaps, (uint)width, ref shelf.reservedX, out var foundX, dimension);
                    if (found)
                        shelf.usedX += width;
                    shelves[i]       = shelf;
                    if (found)
                    {
                        x = (short)foundX;
                        y = shelf.y;
                        z = shelf.z;
                        return found;
                    }
                }
            }

            // We did not found a suitable shelf. Create a new one without creating a new Texture slice
            var previousMaxYPlus = (int)dimension + 1;
            var previousZ        = -1;

            for (int i = 0; i < shelves.Length; i++)
            {
                var nextShelf = shelves[i];
                if (nextShelf.z != previousZ)
                {
                    if (previousMaxYPlus + alignedHeight <= dimension)
                    {
                        var newShelf = new Shelf
                        {
                            y                   = (short)previousMaxYPlus,
                            z                   = (short)previousZ,
                            height              = (short)alignedHeight,
                            requiresCoellescing = false,
                            reservedX           = (uint)width,
                            usedX               = width,
                            gaps                = new UnsafeList<uint2>(8, allocator)
                        };
                        shelves.InsertRange(i, 1);
                        shelves[i] = newShelf;
                        x          = 0;
                        y          = newShelf.y;
                        z          = newShelf.z;
                        return true;
                    }
                    //else if (nextShelf.z > previousZ + 1)
                    //{
                    //    // Totally free texture array index
                    //    var newShelf = new Shelf
                    //    {
                    //        y = 0,
                    //        z = (short)(previousZ + 1),
                    //        height = (short)alignedHeight,
                    //        requiresCoellescing = false,
                    //        reservedX = (uint)width,
                    //        usedX = width,
                    //        gaps = new UnsafeList<uint2>(8, allocator)
                    //    };
                    //    shelves.InsertRange(i, 1);
                    //    shelves[i] = newShelf;
                    //    x = 0;
                    //    y = newShelf.y;
                    //    z = newShelf.z;
                    //    return;
                    //}
                    else if (nextShelf.y >= alignedHeight)
                    {
                        // Free shelf space on the same array index as the next
                        var newShelf = new Shelf
                        {
                            y                   = 0,
                            z                   = nextShelf.z,
                            height              = (short)alignedHeight,
                            requiresCoellescing = false,
                            reservedX           = (uint)width,
                            usedX               = width,
                            gaps                = new UnsafeList<uint2>(8, allocator)
                        };
                        shelves.InsertRange(i, 1);
                        shelves[i] = newShelf;
                        x          = 0;
                        y          = newShelf.y;
                        z          = newShelf.z;
                        return true;
                    }
                }
                else if (nextShelf.y >= previousMaxYPlus + alignedHeight)
                {
                    var newShelf = new Shelf
                    {
                        y                   = (short)previousMaxYPlus,
                        z                   = (short)previousZ,
                        height              = (short)alignedHeight,
                        requiresCoellescing = false,
                        reservedX           = (uint)width,
                        usedX               = width,
                        gaps                = new UnsafeList<uint2>(8, allocator)
                    };
                    shelves.InsertRange(i, 1);
                    shelves[i] = newShelf;
                    x          = 0;
                    y          = newShelf.y;
                    z          = newShelf.z;
                    return true;
                }
                previousMaxYPlus = nextShelf.y + nextShelf.height;
                previousZ        = nextShelf.z;
            }

            // We couldn't insert a shelf, so we have to append a new one.
            if (previousMaxYPlus + alignedHeight <= dimension)
            {
                // There's still some space in the last array index
                var newShelf = new Shelf
                {
                    y                   = (short)previousMaxYPlus,
                    z                   = (short)previousZ,
                    height              = (short)alignedHeight,
                    requiresCoellescing = false,
                    reservedX           = (uint)width,
                    usedX               = width,
                    gaps                = new UnsafeList<uint2>(8, allocator)
                };
                shelves.Add(newShelf);
                x = 0;
                y = newShelf.y;
                z = newShelf.z;
                return true;
            }
            else
            {
                //// We need a new array index
                //var newShelf = new Shelf
                //{
                //    y = 0,
                //    z = (short)(previousZ + 1),
                //    height = (short)alignedHeight,
                //    requiresCoellescing = false,
                //    reservedX = (uint)width,
                //    usedX = width,
                //    gaps = new UnsafeList<uint2>(8, allocator)
                //};
                //shelves.Add(newShelf);
                //x = 0;
                //y = newShelf.y;
                //z = newShelf.z;
                x = -1;
                y = -1;
                z = -1;
                return false;
            }
        }

        public void Allocate(uint glyphEntryId, short width, short height, out short x, out short y, out short z)
        {
            var format  = (RenderFormat)Bits.GetBits(glyphEntryId, 30, 2);
            var shelves = sdf8Shelves;
            if (format == RenderFormat.SDF16)
                shelves = sdf16Shelves;
            else if (format == RenderFormat.Bitmap8888)
                shelves = bitmapShelves;

            var alignedHeight = CollectionHelper.Align(height, shelfAlignment);
            for (int i = 0; i < shelves.Length; i++)
            {
                var shelf = shelves[i];
                if (shelf.height == alignedHeight)
                {
                    if (shelf.requiresCoellescing)
                    {
                        shelf.reservedX = GapAllocator.CoalesceGaps(ref shelf.gaps, shelf.reservedX);
                    }
                    var found = GapAllocator.TryAllocate(ref shelf.gaps, (uint)width, ref shelf.reservedX, out var foundX, dimension);
                    if (found)
                        shelf.usedX += width;
                    shelves[i]       = shelf;
                    if (found)
                    {
                        x = (short)foundX;
                        y = shelf.y;
                        z = shelf.z;
                        return;
                    }
                }
            }

            // We did not found a suitable shelf. Create a new one.
            var previousMaxYPlus = (int)dimension + 1;
            var previousZ        = -1;

            for (int i = 0; i < shelves.Length; i++)
            {
                var nextShelf = shelves[i];
                if (nextShelf.z != previousZ)
                {
                    if (previousMaxYPlus + alignedHeight <= dimension)
                    {
                        var newShelf = new Shelf
                        {
                            y                   = (short)previousMaxYPlus,
                            z                   = (short)previousZ,
                            height              = (short)alignedHeight,
                            requiresCoellescing = false,
                            reservedX           = (uint)width,
                            usedX               = width,
                            gaps                = new UnsafeList<uint2>(8, allocator)
                        };
                        shelves.InsertRange(i, 1);
                        shelves[i] = newShelf;
                        x          = 0;
                        y          = newShelf.y;
                        z          = newShelf.z;
                        return;
                    }
                    else if (nextShelf.z > previousZ + 1)
                    {
                        // Totally free texture array index
                        var newShelf = new Shelf
                        {
                            y                   = 0,
                            z                   = (short)(previousZ + 1),
                            height              = (short)alignedHeight,
                            requiresCoellescing = false,
                            reservedX           = (uint)width,
                            usedX               = width,
                            gaps                = new UnsafeList<uint2>(8, allocator)
                        };
                        shelves.InsertRange(i, 1);
                        shelves[i] = newShelf;
                        x          = 0;
                        y          = newShelf.y;
                        z          = newShelf.z;
                        return;
                    }
                    else if (nextShelf.y >= alignedHeight)
                    {
                        // Free shelf space on the same array index as the next
                        var newShelf = new Shelf
                        {
                            y                   = 0,
                            z                   = nextShelf.z,
                            height              = (short)alignedHeight,
                            requiresCoellescing = false,
                            reservedX           = (uint)width,
                            usedX               = width,
                            gaps                = new UnsafeList<uint2>(8, allocator)
                        };
                        shelves.InsertRange(i, 1);
                        shelves[i] = newShelf;
                        x          = 0;
                        y          = newShelf.y;
                        z          = newShelf.z;
                        return;
                    }
                }
                else if (nextShelf.y >= previousMaxYPlus + alignedHeight)
                {
                    var newShelf = new Shelf
                    {
                        y                   = (short)previousMaxYPlus,
                        z                   = (short)previousZ,
                        height              = (short)alignedHeight,
                        requiresCoellescing = false,
                        reservedX           = (uint)width,
                        usedX               = width,
                        gaps                = new UnsafeList<uint2>(8, allocator)
                    };
                    shelves.InsertRange(i, 1);
                    shelves[i] = newShelf;
                    x          = 0;
                    y          = newShelf.y;
                    z          = newShelf.z;
                    return;
                }
                previousMaxYPlus = nextShelf.y + nextShelf.height;
                previousZ        = nextShelf.z;
            }

            // We couldn't insert a shelf, so we have to append a new one.
            if (previousMaxYPlus + alignedHeight <= dimension)
            {
                // There's still some space in the last array index
                var newShelf = new Shelf
                {
                    y                   = (short)previousMaxYPlus,
                    z                   = (short)previousZ,
                    height              = (short)alignedHeight,
                    requiresCoellescing = false,
                    reservedX           = (uint)width,
                    usedX               = width,
                    gaps                = new UnsafeList<uint2>(8, allocator)
                };
                shelves.Add(newShelf);
                x = 0;
                y = newShelf.y;
                z = newShelf.z;
                return;
            }
            else
            {
                // We need a new array index
                var newShelf = new Shelf
                {
                    y                   = 0,
                    z                   = (short)(previousZ + 1),
                    height              = (short)alignedHeight,
                    requiresCoellescing = false,
                    reservedX           = (uint)width,
                    usedX               = width,
                    gaps                = new UnsafeList<uint2>(8, allocator)
                };
                shelves.Add(newShelf);
                x = 0;
                y = newShelf.y;
                z = newShelf.z;
                return;
            }
        }

        public void Free(ref GlyphTable glyphTable)
        {
            // We know for sure that these entry IDs are no longer referenced. Therefore, we can actually remove them.
            var entriesToRemove = atlasRemovalCandidates.ToNativeArray(Allocator.Temp);
            entriesToRemove.Sort();  // Determinism for debugging
            foreach (var id in entriesToRemove)
            {
                ref var entry         = ref glyphTable.GetEntryRW(id);
                var     doublePadding = 2 * entry.padding;
                Free(id, (short)(entry.width + doublePadding), (short)(entry.height + doublePadding), entry.x, entry.y, entry.z);
                //if (entry.key.format == RenderFormat.SDF8)
                //    UnityEngine.Debug.Log($"Freeing {entry.x} {entry.y}, width {entry.width}");
                entry.x = -1;
                entry.y = -1;
                entry.z = -1;
            }
        }
        public void Free(uint glyphEntryId, short width, short height, short x, short y, short z)
        {
            var format  = (RenderFormat)Bits.GetBits(glyphEntryId, 30, 2);
            var shelves = sdf8Shelves;
            if (format == RenderFormat.SDF16)
                shelves = sdf16Shelves;
            else if (format == RenderFormat.Bitmap8888)
                shelves = bitmapShelves;

            for (int i = 0; i < shelves.Length; i++)
            {
                var shelf = shelves[i];
                if (shelf.y == y && shelf.z == z)
                {
                    shelf.gaps.Add(new uint2((uint)x, (uint)width));
                    shelf.requiresCoellescing  = true;
                    shelf.usedX               -= width;
                    // Todo: We don't support reallocating removed shelves right now. So just leave it empty.
                    //if (shelf.usedX <= 0)
                    //{
                    //    // Shelf is empty now. Destroy it.
                    //    shelf.gaps.Dispose();
                    //    shelves.RemoveAt(i);
                    //}
                    shelves[i] = shelf;
                    return;
                }
            }
        }

        struct Shelf
        {
            public short y;
            public short z;
            public short height;
            public bool  requiresCoellescing;
            public uint  reservedX;
            public int   usedX;

            public UnsafeList<uint2> gaps;
        }

        AllocatorManager.AllocatorHandle allocator;
        uint                             dimension;
        int                              shelfAlignment;
        NativeList<Shelf>                sdf8Shelves;
        NativeList<Shelf>                sdf16Shelves;
        NativeList<Shelf>                bitmapShelves;
        public NativeHashSet<uint>       atlasRemovalCandidates;

        [BurstCompile]
        struct DisposeJob : IJob
        {
            public AtlasTable atlasTable;

            public void Execute()
            {
                foreach (var shelf in atlasTable.sdf8Shelves)
                    shelf.gaps.Dispose();
                foreach (var shelf in atlasTable.sdf16Shelves)
                    shelf.gaps.Dispose();
                foreach (var shelf in atlasTable.bitmapShelves)
                    shelf.gaps.Dispose();
            }
        }
    }
}

