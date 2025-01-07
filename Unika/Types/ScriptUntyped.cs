using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Unika
{
    /// <summary>
    /// A resolved untyped script which can be operated on
    /// </summary>
    public unsafe struct Script : IScriptExtensionsApi, IEquatable<Script>, IComparable<Script>, IEquatable<ScriptRef>, IComparable<ScriptRef>
    {
        internal NativeArray<ScriptHeader> m_scriptBuffer;
        internal Entity                    m_entity;
        internal int                       m_byteOffset;
        internal int                       m_headerOffset;

        internal ref ScriptHeader m_header => ref *(ScriptHeader*)((byte*)m_scriptBuffer.GetUnsafePtr() + m_headerOffset);
        internal ref readonly ScriptHeader m_headerRO => ref *(ScriptHeader*)((byte*)m_scriptBuffer.GetUnsafeReadOnlyPtr() + m_headerOffset);

        #region Main API
        /// <summary>
        /// The entity this script belongs to
        /// </summary>
        public Entity entity => m_entity;

        /// <summary>
        /// Obtains the full set of scripts attached to the entity this script belongs to
        /// </summary>
        public EntityScriptCollection allScripts => new EntityScriptCollection { m_buffer = m_scriptBuffer, m_entity = m_entity };

        /// <summary>
        /// Obtains the index of this script within the full set of scripts attached to the entity
        /// </summary>
        public int indexInEntity => (m_headerOffset / UnsafeUtility.SizeOf<ScriptHeader>()) - 1;

        /// <summary>
        /// A user byte value which can be used for fast early-out operations without having to load the full script state
        /// </summary>
        public byte userByte
        {
            get => m_headerRO.userByte;
            set => m_header.userByte = value;
        }

        /// <summary>
        /// The first of two user flag values which can be used for fast early-out operations without having to load the full script state
        /// </summary>
        public bool userFlagA
        {
            get => m_headerRO.userFlagA;
            set => m_header.userFlagA = value;
        }

        /// <summary>
        /// The second of two user flag values which can be used for fast early-out operations without having to load the full script state
        /// </summary>
        public bool userFlagB
        {
            get => m_headerRO.userFlagB;
            set => m_header.userFlagB = value;
        }

        public static implicit operator ScriptRef(Script script) => new ScriptRef
        {
            m_entity            = script.m_entity,
            m_instanceId        = script.m_headerRO.instanceId,
            m_cachedHeaderIndex = (script.m_headerOffset / UnsafeUtility.SizeOf<ScriptHeader>()) - 1
        };
        #endregion

        #region Type operations
        public static bool operator==(Script lhs, Script rhs)
        {
            return lhs.m_entity == rhs.m_entity && (lhs.m_entity == Entity.Null || lhs.m_headerRO.instanceId == rhs.m_headerRO.instanceId);
        }

        public static bool operator!=(Script lhs, Script rhs) => !(lhs == rhs);

        public int CompareTo(Script other)
        {
            var result = m_entity.CompareTo(other.m_entity);
            if (result == 0)
                return m_headerRO.instanceId.CompareTo(other.m_headerRO.instanceId);
            return result;
        }

        public bool Equals(Script other) => this == other;

        public override bool Equals(object obj)
        {
            if (!typeof(Script).IsAssignableFrom(obj.GetType()))
                return false;
            Script other = (Script)obj;
            return Equals(other);
        }

        public override int GetHashCode() => new int2(m_entity.GetHashCode(), m_headerRO.instanceId).GetHashCode();

        public override string ToString()
        {
            return Equals(Null) ? "Script.Null" : $"Script{{{m_entity.ToFixedString()}, id:{m_headerRO.instanceId}, type:{m_headerRO.scriptType}}}";
        }

        /// <summary>
        /// Gets a Burst-compatible string representation of the script for debug logging purposes
        /// </summary>
        public FixedString128Bytes ToFixedString()
        {
            if (Equals(Null))
                return (FixedString128Bytes)"Script.Null";
            FixedString128Bytes result = "Script{";
            result.Append(m_entity.ToFixedString());
            result.Append((FixedString32Bytes)", id:");
            result.Append(m_headerRO.instanceId);
            result.Append((FixedString32Bytes)", type:");
            result.Append(m_headerRO.scriptType);
            result.Append('}');
            return result;
        }

        /// <summary>
        /// A null unresolved script to assign or compare to
        /// </summary>
        public static Script Null => default;
        #endregion

        #region External Type Operations
        public static bool operator ==(Script lhs, ScriptRef rhs)
        {
            return lhs.m_entity == rhs.m_entity && (lhs.m_entity == Entity.Null || lhs.m_headerRO.instanceId == rhs.m_instanceId);
        }

        public static bool operator !=(Script lhs, ScriptRef rhs) => !(lhs == rhs);

        public int CompareTo(ScriptRef other) => ((ScriptRef)this).CompareTo(other);

        public bool Equals(ScriptRef other) => this == other;
        #endregion

        internal byte* GetUnsafePtrAsBytePtr()
        {
            return (byte*)m_scriptBuffer.GetUnsafePtr() + m_byteOffset;
        }

        internal byte* GetUnsafeROPtrAsBytePtr()
        {
            return (byte*)m_scriptBuffer.GetUnsafeReadOnlyPtr() + m_byteOffset;
        }

        ScriptRef IScriptExtensionsApi.ToRef()
        {
            return this;
        }
    }
}

