using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Unika
{
    public unsafe struct ScriptRef : IEquatable<ScriptRef>, IComparable<ScriptRef>
    {
        internal Entity m_entity;
        internal int    m_instanceId;
        internal int    m_cachedHeaderIndex;

        #region Main API
        public Entity entity => m_entity;
        #endregion

        #region Type operations
        public static bool operator ==(ScriptRef lhs, ScriptRef rhs)
        {
            return lhs.m_entity == rhs.m_entity && lhs.m_instanceId == rhs.m_instanceId;
        }

        public static bool operator !=(ScriptRef lhs, ScriptRef rhs) => !(lhs == rhs);

        public int CompareTo(ScriptRef other)
        {
            var result = m_entity.CompareTo(other.m_entity);
            if (result == 0)
                return m_instanceId.CompareTo(other.m_instanceId);
            return result;
        }

        public bool Equals(ScriptRef other) => this == other;

        public override bool Equals(object obj)
        {
            if (!typeof(Script).IsAssignableFrom(obj.GetType()))
                return false;
            Script other = (Script)obj;
            return Equals(other);
        }

        public override int GetHashCode() => new int2(m_entity.GetHashCode(), m_instanceId).GetHashCode();

        public override string ToString()
        {
            return Equals(Null) ? "ScriptRef.Null" : $"Script{{{m_entity.ToFixedString()}, id:{m_instanceId}}}";
        }

        public FixedString128Bytes ToFixedString()
        {
            if (Equals(Null))
                return (FixedString128Bytes)"Script.Null";
            FixedString128Bytes result = "Script{";
            result.Append(m_entity.ToFixedString());
            result.Append((FixedString32Bytes)", id:");
            result.Append(m_instanceId);
            result.Append('}');
            return result;
        }

        public static ScriptRef Null => default;
        #endregion
    }
}

