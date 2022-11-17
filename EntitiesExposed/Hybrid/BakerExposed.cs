
using System;

namespace Unity.Entities.Exposed
{
    public static class IBakerExposedExtensions
    {
        public static int GetAuthoringInstancedID(this IBaker baker)
        {
            return baker._State.AuthoringSource.GetInstanceID();
        }
    }

    public static class UnityObjectRefExtensions
    {
        public static int GetInstanceID<T>(this UnityObjectRef<T> objectRef) where T : UnityEngine.Object => objectRef.instanceId;
    }

    /// <summary>
    /// Overrides the global list of bakers either adding new ones or replacing old ones.
    /// This is used for tests. Always make sure to dispose to revert the global state back to what it was.
    /// </summary>
    public struct OverrideBakers : IDisposable
    {
        BakerDataUtility.OverrideBakers m_override;

        public OverrideBakers(bool replaceExistingBakers, params Type[] bakerTypes)
        {
            m_override = new BakerDataUtility.OverrideBakers(replaceExistingBakers, bakerTypes);
        }

        public void Dispose()
        {
            m_override.Dispose();
        }
    }
}

