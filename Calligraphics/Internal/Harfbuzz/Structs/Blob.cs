using System;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Collections;
using static Latios.Calligraphics.HarfBuzz.DrawDelegates;
using AOT;


namespace Latios.Calligraphics.HarfBuzz
{
    internal struct Blob : IDisposable
    {
        public IntPtr ptr;
        public int FaceCount => (int)Harfbuzz.hb_face_count(ptr);
        public uint Length => Harfbuzz.hb_blob_get_length(ptr);

        public Blob(string filename)
        {
            ptr = Harfbuzz.hb_blob_create_from_file(filename); //returned blob is immutable            
        }
        unsafe public Blob(void* data, uint length, MemoryMode memoryMode)
        {
            //FunctionPointer<ReleaseDelegate> releaseFunctionPointer = BurstCompiler.CompileFunctionPointer<ReleaseDelegate>(OnBlobDisposed);
            ReleaseDelegate releaseDelegate = new ReleaseDelegate(OnBlobDisposed);
            ptr = Harfbuzz.hb_blob_create(data, length, memoryMode, IntPtr.Zero, releaseDelegate); //returned blob is immutable
        }
        //public Blob(string filename, out bool success)
        //{
        //    ptr = HB.hb_blob_create_from_file_or_fail(filename); //returned blob is immutable
        //    success = ptr != IntPtr.Zero;
        //}
        //unsafe public Blob(void* data, uint length, MemoryMode memoryMode, out bool success)
        //{
        //    DrawDelegates.ReleaseDelegate releaseDelegate = null;
        //    //ReleaseDelegate releaseDelegate = new ReleaseDelegate(DelegateProxies.Test);
        //    ptr = HB.hb_blob_create_or_fail(data, length, memoryMode, IntPtr.Zero, releaseDelegate); //returned blob is immutable
        //    success = ptr != IntPtr.Zero;
        //}
        public NativeArray<byte> GetAsNativeArray()
        {
            uint length;
            NativeArray<byte> result;
            unsafe
            {
                var bytes = Harfbuzz.hb_blob_get_data(ptr, out length);
                result = Harfbuzz.GetNativeArray(bytes, (int)length);
            }
            return result;
        }
        public bool IsImmutable() => Harfbuzz.hb_blob_is_immutable(ptr);
        public void MakeImmutable()
        {
            Harfbuzz.hb_blob_make_immutable(ptr);
        }

        public void Dispose()
        {
            Harfbuzz.hb_blob_destroy(ptr);
        }

        [MonoPInvokeCallback(typeof(ReleaseDelegate))]
        public static void OnBlobDisposed()
        {
            //Debug.Log($"harfbuzz: blob was disposed");
        }
    }
}
