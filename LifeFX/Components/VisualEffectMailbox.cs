using System.Diagnostics;
using Latios.Unsafe;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.LifeFX
{
    public interface IVisualEffectMailbox<TMessageType> : IComponentData, IVisualEffectMailboxType where TMessageType : unmanaged
    {
        FixedString64Bytes bufferPropertyName { get; }
        FixedString64Bytes bufferStartPropertyName { get; }
        FixedString64Bytes bufferCountPropertyName { get; }

        /// <summary>
        /// Back this up with a field. Otherwise, don't touch.
        /// </summary>
        MailboxStorage mailboxStorage { get; set; }

        /// <summary>
        /// Assign a new instance of VisualEffectMailboxHelper of your concrete type
        /// </summary>
        /// <returns></returns>
        IVisualEffectMailboxHelper<TMessageType> Register();

        System.Type IVisualEffectMailboxType.messageType => typeof(TMessageType);
    }

    public static class VisualEffectMailboxAPIExtensions
    {
        public static void GetMailDrop<TMailboxType, TMessageType>(this TMailboxType component, out VisualEffectMailDrop<TMessageType> mailDrop)
            where TMailboxType : unmanaged, IVisualEffectMailbox<TMessageType>
            where TMessageType : unmanaged
        {
            var storage = component.mailboxStorage.storage;
            CheckMailDropValid(storage);
            mailDrop = new VisualEffectMailDrop<TMessageType>(storage);
        }

        public static void Add<TMailboxType, TMessageType>(this TMailboxType component, TMessageType message) where TMailboxType : unmanaged,
        IVisualEffectMailbox<TMessageType> where TMessageType : unmanaged
        {
            component.GetMailDrop(out VisualEffectMailDrop<TMessageType> mailbox);
            mailbox.Add(message);
        }

        struct TestVEM : IVisualEffectMailbox<float>
        {
            public FixedString64Bytes bufferPropertyName => "";
            public FixedString64Bytes bufferStartPropertyName => "";
            public FixedString64Bytes bufferCountPropertyName => "";

            public MailboxStorage mailboxStorage { get; set; }
            public IVisualEffectMailboxHelper<float> Register() => new VisualEffectMailboxHelper<TestVEM, float>();
        }

        static void TestExtension()
        {
            TestVEM t = default;
            t.Add(5f);
            t.GetMailDrop(out VisualEffectMailDrop<float> mailDrop);
            //t.GetMessageDrop<TestVEM, float>();
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        static void CheckMailDropValid(UnsafeParallelBlockList storage)
        {
            if (!storage.isCreated)
                throw new System.InvalidOperationException(
                    "Unable to access the Visual Effect Mailbox's mail drop at this time. Either the mail drop has not yet been initialized, or is being accessed too late in the Presentation Update such that the mailboxes are already being processed.");
        }
    }

    [ChunkSerializable]
    public struct MailboxStorage
    {
        internal UnsafeParallelBlockList storage;
    }

    public interface IVisualEffectMailboxType
    {
        public System.Type messageType { get; }
    }

    public interface IVisualEffectMailboxHelper<TMessageType> where TMessageType : unmanaged
    {
        public System.Type GetMailboxType();
    }

    public partial struct VisualEffectMailboxHelper<TMailboxType, TMessageType> : IVisualEffectMailboxHelper<TMessageType> where TMailboxType : unmanaged,
           IVisualEffectMailbox<TMessageType> where TMessageType : unmanaged
    {
        public System.Type GetMailboxType() => typeof(TMailboxType);
    }

    /// <summary>
    /// This struct is exposed so that you can modify parameters on the runtime-instantiated VisualEffect instance.
    /// This component is attached directly to prefab entities, so be sure to use the IncludePrefabs option in your queries.
    /// </summary>
    public partial struct ManagedVisualEffect : IManagedStructComponent
    {
        public UnityEngine.VFX.VisualEffect effect;
        public bool                         isFromScene;

        public void Dispose()
        {
            if (effect != null && !isFromScene)
            {
                UnityEngine.Object.Destroy(effect.gameObject);
            }
        }
    }
}

