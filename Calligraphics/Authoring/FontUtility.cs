#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using Font = Latios.Calligraphics.HarfBuzz.Font;
using Latios.Calligraphics.HarfBuzz;
using UnityEditor;
using UnityEngine;

namespace Latios.Calligraphics.Authoring
{
    [CreateAssetMenu(fileName = "FontUtility", menuName = "Latios/Calligraphics/FontUtility")]

    // Use this utility to get the information requiered for spawning TextRenderer at runtime vai FontRequest. See
    // RuntimeSpawner/RuntimeSingleFontTextRendererSpawner and
    // RuntimeSpawner/RuntimeMultiFontTextRendererSpawner
    // Drag and drop a font object into the font field
    public class FontUtility : ScriptableObject
    {
        public UnityEngine.Object font;

        /// <summary>Path to the font asset file (shared by all faces/instances).</summary>
        public string fontAssetPath;

        /// <summary>All faces and named instances found in the font file.</summary>
        public List<FontFaceInfo> faces;

        public unsafe void OnValidate()
        {
            if (font == null)
                return;

            fontAssetPath = AssetDatabase.GetAssetPath(font);
            bool isTrueType           = fontAssetPath.EndsWith("ttf", StringComparison.OrdinalIgnoreCase);
            bool isTrueTypeCollection = fontAssetPath.EndsWith("ttc", StringComparison.OrdinalIgnoreCase);
            bool isOpentype           = fontAssetPath.EndsWith("otf", StringComparison.OrdinalIgnoreCase);

            if (!isOpentype && !isTrueType && !isTrueTypeCollection)
            {
                Debug.LogWarning("Ensure you only have files ending with 'ttf', 'ttc', or 'otf' (case insensitive) in font field");
                return;
            }

            if (faces == null)
                faces = new List<FontFaceInfo>();

            faces.Clear();

            var fontBytes = File.ReadAllBytes(fontAssetPath);
            Blob blob;
            fixed (byte* bytes = fontBytes)
            {
                blob = new Blob(bytes, (uint)fontBytes.Length, MemoryMode.READONLY);

                var language = Language.English;

                for (int i = 0, ii = blob.FaceCount; i < ii; i++)
                {
                    var face       = new Face(blob, i);
                    var fontHandle = new Font(face);

                    // Base face entry
                    var faceInfo = new FontFaceInfo
                    {
                        faceIndex          = i,
                        namedInstanceIndex = -1,
                        instanceName       = string.Empty,
                        designCoords       = null,

                        fontFamily           = face.GetName(NameID.FONT_FAMILY, language).ToString(),
                        fontSubFamily        = face.GetName(NameID.FONT_SUBFAMILY, language).ToString(),
                        typographicFamily    = face.GetName(NameID.TYPOGRAPHIC_FAMILY, language).ToString(),
                        typographicSubfamily = face.GetName(NameID.TYPOGRAPHIC_SUBFAMILY, language).ToString(),

                        weight   = fontHandle.GetStyleTag(StyleTag.WEIGHT),
                        width    = fontHandle.GetStyleTag(StyleTag.WIDTH),
                        isItalic = fontHandle.GetStyleTag(StyleTag.ITALIC) == 1,
                        slant    = fontHandle.GetStyleTag(StyleTag.SLANT_ANGLE),
                    };
                    faces.Add(faceInfo);

                    // For variable fonts, enumerate named instances
                    if (face.HasVarData)
                    {
                        var instanceCount = face.NamedInstanceCount;
                        var axisCount     = face.AxisCount;

                        // Fetch axis info to map design coordinates to weight/width/italic/slant
                        Span<AxisInfo> axisInfos = new AxisInfo[axisCount];
                        face.GetAxisInfos(0, 0, ref axisInfos, out _);

                        for (int inst = 0; inst < instanceCount; inst++)
                        {
                            // Get design coordinates for this named instance
                            Span<float> coords = new float[axisCount];
                            face.GetNamedInstanceDesignCoords(inst, ref coords, out uint coordLength);

                            // Resolve design coordinates to style properties
                            float instWeight = fontHandle.GetStyleTag(StyleTag.WEIGHT);
                            float instWidth  = fontHandle.GetStyleTag(StyleTag.WIDTH);
                            bool instItalic = fontHandle.GetStyleTag(StyleTag.ITALIC) == 1;
                            float instSlant  = fontHandle.GetStyleTag(StyleTag.SLANT_ANGLE);

                            for (int f = 0; f < coordLength; f++)
                            {
                                switch (axisInfos[f].axisTag)
                                {
                                    case AxisTag.WEIGHT:
                                        instWeight = coords[f]; break;
                                    case AxisTag.WIDTH:
                                        instWidth = coords[f]; break;
                                    case AxisTag.ITALIC:
                                        instItalic = coords[f] == 1f; break;
                                    case AxisTag.SLANT:
                                        instSlant = coords[f]; break;
                                }
                            }

                            // Get the named instance's subfamily name
                            var instanceSubFamilyNameId = face.GetNamedInstanceSubFamilyNameID(inst);
                            string instanceName            = string.Empty;
                            if (instanceSubFamilyNameId != default)
                                instanceName = face.GetName(instanceSubFamilyNameId, language).ToString();

                            var instanceInfo = new FontFaceInfo
                            {
                                faceIndex          = i,
                                namedInstanceIndex = inst,
                                instanceName       = instanceName,
                                designCoords       = BuildAxisCoordList(axisInfos[..(int)coordLength], coords[..(int)coordLength]),

                                // Named instances inherit the face-level family names
                                fontFamily           = faceInfo.fontFamily,
                                fontSubFamily        = faceInfo.fontSubFamily,
                                typographicFamily    = faceInfo.typographicFamily,
                                typographicSubfamily = faceInfo.typographicSubfamily,

                                // Override with instance-resolved style values
                                weight   = instWeight,
                                width    = instWidth,
                                isItalic = instItalic,
                                slant    = instSlant,
                            };
                            faces.Add(instanceInfo);
                        }
                    }

                    fontHandle.Dispose();
                    face.Dispose();
                }

                blob.Dispose();
            }
        }

        static List<AxisCoordPair> BuildAxisCoordList(Span<AxisInfo> axisInfos, Span<float> coords)
        {
            var list = new List<AxisCoordPair>(axisInfos.Length);
            for (int i = 0; i < axisInfos.Length; i++)
                list.Add(new AxisCoordPair { axisTag = axisInfos[i].axisTag.ToString(), value = coords[i] });
            return list;
        }
    }

    /// <summary>
    /// Serializable key-value pair mapping a variation axis tag to its design coordinate value.
    /// </summary>
    [System.Serializable]
    public struct AxisCoordPair
    {
        public string axisTag;
        public float value;
    }

    /// <summary>
    /// Serializable data for a single face or named instance extracted from a font file.
    /// </summary>
    [System.Serializable]
    public class FontFaceInfo
    {
        public int faceIndex;
        public string fontFamily;
        public string fontSubFamily;
        public string typographicFamily;
        public string typographicSubfamily;
        public float weight;
        public float width;
        public bool isItalic;
        public float slant;

        /// <summary>
        /// If this entry represents a named instance of a variable font, the instance index.
        /// -1 for the base face.
        /// </summary>
        public int namedInstanceIndex;

        /// <summary>
        /// Named instance name (from the subfamily name ID of the instance).
        /// Empty for the base face.
        /// </summary>
        public string instanceName;

        /// <summary>
        /// Design coordinates for each variation axis (only populated for named instances).
        /// </summary>
        public List<AxisCoordPair> designCoords;
    }
}
#endif

