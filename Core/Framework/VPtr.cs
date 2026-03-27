
namespace Latios.Unsafe
{
    /// <summary>
    /// An interface which another interface can derive from to allow Burst-compatible virtual calls.
    /// Any interface deriving from this must be declared as partial, and any struct implementing such
    /// interface must also be marked partial and implement this interface directly. This allows source
    /// generators to generate the necessary virtualization code.
    ///
    /// The interface will have a generated VPtr nested type which allows you to wrap a pointer behind
    /// the interface, allowing you to store various implementors of the interface within a single collection.
    /// </summary>
    public interface IVInterface
    {
        void __ThisMethodIsSupposedToBeGeneratedByASourceGenerator();
    }

    /// <summary>
    /// An interface generated on an IVInterface.VPtr, which can be used as a generic constraint to ensure
    /// a VPtr is a VPtr and for the correct generic interface.
    /// </summary>
    public interface IVPtrFor<T> where T : IVInterface
    {
    }

    /// <summary>
    /// A struct which contains a void*, and is implicitly castable from one. This allows for source generators
    /// to create an API method that accepts a void*, even if the assembly does not allow unsafe code.
    /// </summary>
    public unsafe struct UnsafeApiPointer
    {
        void* m_ptr;
        public void* ptr
        {
            get => m_ptr;
            set => m_ptr = value;
        }

        public static implicit operator UnsafeApiPointer(void* ptr) => new UnsafeApiPointer
        {
            m_ptr = ptr
        };
    }

    /// <summary>
    /// A struct which contains a void*, and is implicitly castable from T*. This allows for source generators
    /// to create an API method that accepts a T*, even if the assembly does not allow unsafe code.
    /// </summary>
    public unsafe struct UnsafeApiPointer<T> where T : unmanaged
    {
        void* m_ptr;
        public void* ptr
        {
            get => m_ptr;
            set => m_ptr = value;
        }

        public static implicit operator UnsafeApiPointer<T>(T* ptr) => new UnsafeApiPointer<T> {
            m_ptr = ptr
        };
        public static implicit operator UnsafeApiPointer(UnsafeApiPointer<T> ptr) => new UnsafeApiPointer {
            ptr = ptr.ptr
        };
    }
}

