using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Unika
{
    /// <summary>
    /// A resolved strongly-typed script which can be operated on
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public unsafe struct Script<T> : IScriptTypedExtensionsApi,
                                     IEquatable<Script<T> >, IEquatable<Script>, IEquatable<ScriptRef<T> >, IEquatable<ScriptRef>,
                                     IComparable<Script<T> >, IComparable<Script>, IComparable<ScriptRef<T> >, IComparable<ScriptRef>
        where T : unmanaged, IUnikaScript, IUnikaScriptGen
    {
        internal NativeArray<ScriptHeader> m_scriptBuffer;
        internal Entity                    m_entity;
        internal int                       m_headerOffset;
        internal int                       m_byteOffset;

        internal ref ScriptHeader m_header => ref *(ScriptHeader*)((byte*)m_scriptBuffer.GetUnsafePtr() + m_headerOffset);
        internal ref readonly ScriptHeader m_headerRO => ref *(ScriptHeader*)((byte*)m_scriptBuffer.GetUnsafeReadOnlyPtr() + m_headerOffset);

        #region Main API
        /// <summary>
        /// Obtains a read-write reference to the script struct stored directly in the script buffer
        /// </summary>
        public ref T valueRW => ref *(T*)((byte*)m_scriptBuffer.GetUnsafePtr() + m_byteOffset);
        /// <summary>
        /// Obtains a read-only reference to the script struct stored directly in the script buffer
        /// </summary>
        public ref readonly T valueRO => ref *(T*)((byte*)m_scriptBuffer.GetUnsafeReadOnlyPtr() + m_byteOffset);

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

        public static implicit operator Script(Script<T> script)
        {
            return new Script
            {
                m_scriptBuffer = script.m_scriptBuffer,
                m_entity       = script.m_entity,
                m_headerOffset = script.m_headerOffset,
                m_byteOffset   = script.m_byteOffset,
            };
        }

        public static implicit operator ScriptRef<T>(Script<T> script) => new ScriptRef<T>
        {
            m_entity            = script.m_entity,
            m_instanceId        = script.m_headerRO.instanceId,
            m_cachedHeaderIndex = (script.m_headerOffset / UnsafeUtility.SizeOf<ScriptHeader>()) - 1
        };

        public static implicit operator ScriptRef(Script<T> script) => new ScriptRef
        {
            m_entity            = script.m_entity,
            m_instanceId        = script.m_headerRO.instanceId,
            m_cachedHeaderIndex = (script.m_headerOffset / UnsafeUtility.SizeOf<ScriptHeader>()) - 1
        };
        #endregion

        #region Type operations
        public static bool operator ==(Script<T> lhs, Script<T> rhs)
        {
            return lhs.m_entity == rhs.m_entity && lhs.m_headerRO.instanceId == rhs.m_headerRO.instanceId;
        }

        public static bool operator !=(Script<T> lhs, Script<T> rhs) => !(lhs == rhs);

        public int CompareTo(Script<T> other) => ((Script)this).CompareTo((Script)other);

        public bool Equals(Script<T> other) => this == other;

        public override bool Equals(object obj) => ((Script)this).Equals(obj);

        public override int GetHashCode() => new int2(m_entity.GetHashCode(), m_headerRO.instanceId).GetHashCode();

        public override string ToString() => ((Script)this).ToString();

        /// <summary>
        /// Gets a Burst-compatible string representation of the script for debug logging purposes
        /// </summary>
        public FixedString128Bytes ToFixedString() => ((Script)this).ToFixedString();

        /// <summary>
        /// A null unresolved script to assign or compare to
        /// </summary>
        public static Script<T> Null => default;
        #endregion

        #region External Type Operations
        public static bool operator ==(Script<T> lhs, Script rhs)
        {
            return lhs.m_entity == rhs.m_entity && lhs.m_headerRO.instanceId == rhs.m_headerRO.instanceId;
        }

        public static bool operator ==(Script<T> lhs, ScriptRef<T> rhs)
        {
            return lhs.m_entity == rhs.m_entity && lhs.m_headerRO.instanceId == rhs.m_instanceId;
        }

        public static bool operator ==(Script<T> lhs, ScriptRef rhs)
        {
            return lhs.m_entity == rhs.m_entity && lhs.m_headerRO.instanceId == rhs.m_instanceId;
        }

        public static bool operator !=(Script<T> lhs, Script rhs) => !(lhs == rhs);

        public static bool operator !=(Script<T> lhs, ScriptRef<T> rhs) => !(lhs == rhs);

        public static bool operator !=(Script<T> lhs, ScriptRef rhs) => !(lhs == rhs);

        public int CompareTo(Script other) => ((Script)this).CompareTo(other);

        public int CompareTo(ScriptRef<T> other) => ((ScriptRef<T>) this).CompareTo(other);

        public int CompareTo(ScriptRef other) => ((ScriptRef)this).CompareTo(other);

        public bool Equals(Script other) => this == other;

        public bool Equals(ScriptRef<T> other) => this == other;

        public bool Equals(ScriptRef other) => this == other;
        #endregion

        ScriptRef IScriptExtensionsApi.ToRef() => this;

        bool IScriptTypedExtensionsApi.Is(in Script script) => ScriptCast.IsScript<T>(in script);

        bool IScriptTypedExtensionsApi.TryCastInit(in Script script, IScriptTypedExtensionsApi.WrappedThisPtr thisPtr)
        {
            var result = ScriptCast.TryCastScript(in script, out this);
            UnsafeUtility.CopyStructureToPtr(ref this, thisPtr.ptr);
            return result;
        }

        IScriptTypedExtensionsApi.WrappedIdAndMask IScriptTypedExtensionsApi.GetIdAndMask() => new IScriptTypedExtensionsApi.WrappedIdAndMask
        {
            idAndMask = ScriptTypeInfoManager.GetScriptRuntimeIdAndMask<T>()
        };

        /// <summary>
        /// Gets an untyped but resolved reference to this script
        /// </summary>
        public Script ToScript() => this;
    }
}

