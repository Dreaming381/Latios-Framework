using System.Runtime.CompilerServices;
using Unity.Collections;

namespace Latios.Calci.Clipper2
{
    internal static class ClipperExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void EnsureCapacity<T>(this NativeList<T> list, int minCapacity) where T : unmanaged
        {
            if (list.Capacity < minCapacity)
                list.Capacity = minCapacity;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int AddVertex(ref this NativeList<Vertex> vertices, long2 vertex, VertexFlags flag, bool firstVertex, int firstVertexID = 0)
        {
            int currentID = vertices.Length;
            if (!firstVertex)
            {
                int    prevID = currentID - 1;
                Vertex tmp    = new Vertex(vertex, flag, prevID);
                tmp.next      = firstVertexID;  //set next of tail  = head
                vertices.Add(tmp);

                ref var prev = ref vertices.ElementAt(prevID);
                prev.next    = currentID;  //set next of prev to tail

                //head and tail will be fixed after whole polygon component has been added.
                //ref var first = ref vertices.ElementAt(firstVertexID);
                //first.prev = currentID; //set prev of head  = tail
            }
            else
            {
                Vertex tmp = new Vertex(vertex, flag, -1);
                //head and tail will be fixed after whole polygon component has been added.
                //Vertex tmp = new Vertex(vertex, flag, currentID);
                //tmp.next = currentID; //set next of tail  = head
                vertices.Add(tmp);
            }
            return currentID;
        }

        public static void Reverse<T>(this NativeList<T> list, int first, int last) where T : unmanaged
        {
            int i = first, j = last;
            while (i < j)
            {
                ref var a = ref list.ElementAt(j);
                ref var b = ref list.ElementAt(i);
                (a, b)    = (b, a);
                i++;
                j--;
            }
        }
    };
}  //namespace

