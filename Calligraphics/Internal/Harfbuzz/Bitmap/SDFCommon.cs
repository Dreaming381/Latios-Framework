using System.IO;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace Latios.Calligraphics.HarfBuzz.Bitmap
{
    internal static class SDFCommon
    {
        // SPREAD represents the number of texels away from the closest edge before the texel values saturate.
        // 8 bit: distance can be from -128 (outside) to +127 (inside) --> store in 8 bit alpha channel.
        // 16 bit: distance can be from -32,768 (outside) to +32,767 (inside) -->store in 16 bit alpha
        // When converting to 8 bit alpha, we remap signed distances of [-SPREAD, SPREAD] to [0, 255];
        public const int DEFAULT_SPREAD_PER_64_SIZE = 8;  // SPREAD and Atlas padding are related, but do not set SPREAD too small

        /// /// <summary>
        /// positive area = CCW, negative area = CW (works for closed and open polygon (identical result))
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float SignedArea(NativeList<SDFEdge> data, int start, int end)
        {
            float area = default;
            for (int i = start, prev = end - 1; i < end; prev = i++) //from (0, prev) until (end, prev)
                area += (data[prev].start_pos.x - data[i].start_pos.x) * (data[i].start_pos.y + data[prev].start_pos.y);
            return area * 0.5f;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static PolyOrientation GetPolyOrientation(double signedArea)
        {
            if (signedArea < 0)
                return PolyOrientation.CW;
            else if (signedArea > 0)
                return PolyOrientation.CCW;
            else
                return PolyOrientation.None;
        }
        public enum PolyOrientation : byte
        {
            CW = 0,
            CCW = 1,
            None = 2,
        }

        public static void WriteGlyphOutlineToFile(string path, NativeList<SDFEdge> edges)
        {
            if(edges.Length == 0)
                return;
            StreamWriter writer = new StreamWriter(path, false);
            var          edge   = edges[0];
            writer.WriteLine($"{edge.start_pos.x} {edge.start_pos.y}");
            for (int i = 0, end = edges.Length; i < end; i++)
            {
                edge = edges[i];
                writer.WriteLine($"{edge.end_pos.x} {edge.end_pos.y}");
            }
            writer.WriteLine();
            writer.Close();
        }

        public static void WriteGlyphOutlineToFile(string path, DrawData drawData)
        {
            var edges = drawData.edges;
            if (edges.Length == 0)
                return;
            var startIDs = drawData.contourIDs;

            StreamWriter writer = new StreamWriter(path, false);
            for (int i = 0, ii = startIDs.Length - 1; i < ii; i++)
            {
                var startID     = startIDs[i];
                var nextStartID = startIDs[i + 1];
                for (int k = startID; k < nextStartID; k++)
                {
                    var edge = edges[k];
                    writer.WriteLine($"{edge.start_pos.x} {edge.start_pos.y}");
                }
                writer.WriteLine($"{edges[nextStartID-1].end_pos.x} {edges[nextStartID - 1].end_pos.y}");
                writer.WriteLine();
            }

            //var edge = edges[0];
            //writer.WriteLine($"{edge.start_pos.x} {edge.start_pos.y}");
            //for (int i = 0, end = edges.Length; i < end; i++)
            //{
            //    edge = edges[i];
            //    writer.WriteLine($"{edge.end_pos.x} {edge.end_pos.y}");
            //}

            writer.Close();
        }

        public static void WriteMinDistancesToFile(string path, in NativeArray<SDFDebug> sdfDebug)
        {
            if (sdfDebug.Length == 0)
                return;
            StreamWriter writer = new StreamWriter(path, false);
            for (int i = 0, end = sdfDebug.Length; i < end; i++)
            {
                writer.WriteLine($"{sdfDebug[i]}");
            }
            writer.WriteLine();
            writer.Close();
        }
        public static void WriteArrayToFile(string path, in NativeArray<float> array, int arrayWidth, int row)
        {
            if (array.Length == 0)
                return;
            StreamWriter writer = new StreamWriter(path, false);
            var          start  = arrayWidth * row;
            var          end    = start + arrayWidth;
            for (int i = start; i < end; i++)
            {
                writer.WriteLine($"{array[i]}");
            }
            writer.WriteLine();
            writer.Close();
        }
        public static void WriteArrayToFile(string path, in NativeArray<byte> array, int arrayWidth, int row)
        {
            if (array.Length == 0)
                return;
            StreamWriter writer = new StreamWriter(path, false);
            var          start  = arrayWidth * row;
            var          end    = start + arrayWidth;
            for (int i = start; i < end; i++)
            {
                writer.WriteLine($"{array[i]}");
            }
            writer.WriteLine();
            writer.Close();
        }
        public static void WriteArrayToFile(string path, in NativeArray<byte> array)
        {
            if (array.Length == 0)
                return;
            StreamWriter writer = new StreamWriter(path, false);
            for (int i = 0, end = array.Length; i < end; i++)
            {
                writer.WriteLine($"{array[i]}");
            }
            writer.WriteLine();
            writer.Close();
        }
        public static void WriteArrayToFile(string path, in NativeArray<ushort> array)
        {
            if (array.Length == 0)
                return;
            StreamWriter writer = new StreamWriter(path, false);
            for (int i = 0, end = array.Length; i < end; i++)
            {
                writer.WriteLine($"{array[i]}");
            }
            writer.WriteLine();
            writer.Close();
        }
        public static void WriteArrayToFile(string path, in NativeArray<int> array)
        {
            if (array.Length == 0)
                return;
            StreamWriter writer = new StreamWriter(path, false);
            for (int i = 0, end = array.Length; i < end; i++)
            {
                writer.WriteLine($"{array[i]}");
            }
            writer.WriteLine();
            writer.Close();
        }

        public static void WriteSDFDebugToFile(string path, NativeArray<SDFDebug> sdfDebug)
        {
            if (sdfDebug.Length == 0)
                return;
            StreamWriter writer = new StreamWriter(path, false);
            for (int i = 0, end = sdfDebug.Length; i < end; i++)
            {
                var c = sdfDebug[i];
                writer.WriteLine($"{c.row} {c.column} {c.distanceRaw} {c.signRaw} {c.currentSignRaw} {c.distance} {c.sign} {c.currentSign} {c.cross}");
            }
            writer.WriteLine();
            writer.Close();
        }
        public static void WriteGlyphOutlineToFile(string path, ref DrawData drawData, bool fullBezier = false)
        {
            var edges      = drawData.edges;
            var contourIDs = drawData.contourIDs;
            if (contourIDs.Length < 2 || edges.Length == 0)
                return;

            StreamWriter writer = new StreamWriter(path, false);
            SDFEdge      edge;
            for (int contourID = 0, end = contourIDs.Length - 1; contourID < end; contourID++)  //for each contour
            {
                int startID     = contourIDs[contourID];
                int nextStartID = contourIDs[contourID + 1];
                for (int edgeID = startID; edgeID < nextStartID; edgeID++)  //for each edge
                {
                    edge = edges[edgeID];
                    if(fullBezier)
                        writer.WriteLine($"{edge.start_pos.x} {edge.start_pos.y} {edge.control1.x} {edge.control1.y} {edge.end_pos.x} {edge.end_pos.y} {edge.edge_type}");
                    else
                        writer.WriteLine($"{edge.start_pos.x} {edge.start_pos.y}");
                }
                writer.WriteLine();
            }
            writer.Close();
        }
    }
    public struct SDFDebug
    {
        public int   row;
        public int   column;
        public float distanceRaw;
        public int   signRaw;
        public float distance;
        public int   sign;
        public float cross;
        public int   currentSignRaw;
        public int   currentSign;
        public SDFDebug(int row, int column, float distanceRaw, int signRaw, int currentSignRaw, float cross)
        {
            this.row            = row;
            this.column         = column;
            this.distanceRaw    = distanceRaw;
            this.signRaw        = signRaw;
            this.currentSignRaw = currentSignRaw;
            this.distance       = float.MinValue;
            this.sign           = int.MinValue;
            this.cross          = cross;
            this.currentSign    = 0;
        }
    }
}

