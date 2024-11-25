using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Unika
{
    /// <summary>
    /// A typed reference to another script, which must be resolved before use.
    /// </summary>
    /// <typeparam name="T">The type of script referenced</typeparam>
    public unsafe struct ScriptRef<T> : IScriptRefTypedExtensionsApi,
                                        IEquatable<ScriptRef<T> >, IEquatable<ScriptRef>,
                                        IComparable<ScriptRef<T> >, IComparable<ScriptRef>
        where T : unmanaged, IUnikaScript, IUnikaScriptGen
    {
        internal Entity m_entity;
        internal int    m_instanceId;
        internal int    m_cachedHeaderIndex;

        #region Main API
        /// <summary>
        /// The entity the referenced script belongs to
        /// </summary>
        public Entity entity => m_entity;

        public static implicit operator ScriptRef(ScriptRef<T> script)
        {
            return new ScriptRef
            {
                m_entity            = script.m_entity,
                m_instanceId        = script.m_instanceId,
                m_cachedHeaderIndex = script.m_cachedHeaderIndex
            };
        }
        #endregion

        #region Type operations
        public static bool operator ==(ScriptRef<T> lhs, ScriptRef<T> rhs)
        {
            return lhs.m_entity == rhs.m_entity && lhs.m_instanceId == rhs.m_instanceId;
        }

        public static bool operator !=(ScriptRef<T> lhs, ScriptRef<T> rhs) => !(lhs == rhs);

        public int CompareTo(ScriptRef<T> other) => ((ScriptRef)this).CompareTo((ScriptRef)other);

        public bool Equals(ScriptRef<T> other) => this == other;

        public override bool Equals(object obj) => ((ScriptRef)this).Equals(obj);

        public override int GetHashCode() => new int2(m_entity.GetHashCode(), m_instanceId).GetHashCode();

        public override string ToString() => ((ScriptRef)this).ToString();

        /// <summary>
        /// Gets a Burst-compatible string representation of the script for debug logging purposes
        /// </summary>
        public FixedString128Bytes ToFixedString() => ((ScriptRef)this).ToFixedString();

        /// <summary>
        /// A null ScriptRef to assign or compare to
        /// </summary>
        public static ScriptRef<T> Null => default;
        #endregion

        #region External Type Operations
        public static bool operator ==(ScriptRef<T> lhs, ScriptRef rhs)
        {
            return lhs.m_entity == rhs.m_entity && lhs.m_instanceId == rhs.m_instanceId;
        }

        public static bool operator !=(ScriptRef<T> lhs, ScriptRef rhs) => !(lhs == rhs);

        public int CompareTo(ScriptRef other) => ((ScriptRef)this).CompareTo(other);

        public bool Equals(ScriptRef other) => this == other;
        #endregion

        ScriptRef IScriptRefTypedExtensionsApi.ToScriptRef() => this;
    }
}

