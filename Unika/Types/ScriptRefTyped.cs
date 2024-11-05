using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Unika
{
    public unsafe struct ScriptRef<T> : IEquatable<ScriptRef<T> >, IEquatable<ScriptRef>,
                                        IComparable<ScriptRef<T> >, IComparable<ScriptRef>
        where T : unmanaged, IUnikaScript, IUnikaScriptGen
    {
        internal Entity m_entity;
        internal int    m_instanceId;
        internal int    m_cachedHeaderIndex;

        #region Main API
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

        public FixedString128Bytes ToFixedString() => ((ScriptRef)this).ToFixedString();

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
    }
}

