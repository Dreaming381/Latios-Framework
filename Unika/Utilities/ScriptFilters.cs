using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

using static Latios.Unika.ScriptTypeInfoManager;

namespace Latios.Unika
{
    /// <summary>
    /// Defines a filter which can be used to efficiently iterate through scripts.
    /// </summary>
    public interface IScriptFilter
    {
        /// <summary>
        /// Defines a prefilter which can be used to rule out the whole collection of scripts.
        /// The header contains bloom filter information which is internal to Unika. It is not
        /// expected you use this data directly. However, you can construct several of the
        /// built-in filters and forward the header to them to create combinations.
        /// </summary>
        /// <param name="header">A header containing a bloom filter which can be forwarded to
        /// one of the built-in filters.</param>
        /// <returns>True if the collection of scripts maybe contains a script of interest,
        /// false if it definitely does not.</returns>
        public bool PreFilter(UnikaScripts header) => true;

        /// <summary>
        /// Defines a filter method that evaluates the script.
        /// </summary>
        /// <param name="candidate">The script to consider</param>
        /// <returns>True if the script passes the filter and should be further evaluated, false if it should be rejected</returns>
        public bool Filter(Script candidate);
    }

    /// <summary>
    /// Defines convenient built-in filter instances that can be quickly dropped into an expression.
    /// </summary>
    public static class ScriptFilter
    {
        /// <summary>
        /// Only scripts with UserFlagA having a true value will pass the filter
        /// </summary>
        public static UserFlagATrueScriptFilter UserFlagATrue => UserFlagATrueScriptFilter.Create();
        /// <summary>
        /// Only scripts with UserFlagA having a false value will pass the filter
        /// </summary>
        public static UserFlagAFalseScriptFilter UserFlagAFalse => UserFlagAFalseScriptFilter.Create();
        /// <summary>
        /// Only scripts with UserFlagB having a true value will pass the filter
        /// </summary>
        public static UserFlagBTrueScriptFilter UserFlagBTrue => UserFlagBTrueScriptFilter.Create();
        /// <summary>
        /// Only scripts with UserFlagB having a false value will pass the filter
        /// </summary>
        public static UserFlagBFalseScriptFilter UserFlagBFalse => UserFlagBFalseScriptFilter.Create();
        /// <summary>
        /// Only scripts with this userByte value will pass the filter
        /// </summary>
        /// <param name="userByte">The value that each script's userByte must have to pass</param>
        public static UserByteEqualsScriptFilter UserByteIs(byte userByte) => UserByteEqualsScriptFilter.Create(userByte);
        /// <summary>
        /// Only scripts that do not have this userByte value will pass the filter
        /// </summary>
        /// <param name="userByte">The value that each script's userByte must not have to pass</param>
        public static UserByteNotEqualsScriptFilter UserByteIsNot(byte userByte) => UserByteNotEqualsScriptFilter.Create(userByte);
        /// <summary>
        /// Only scripts that are of this type will pass the filter
        /// </summary>
        /// <typeparam name="T">The type the script must be</typeparam>
        public static IsTypeScriptFilter<T> IsOfType<T>() where T : unmanaged, IUnikaScript, IUnikaScriptGen => IsTypeScriptFilter<T>.Create();
        /// <summary>
        /// Only scripts that are not of this type will pass the filter
        /// </summary>
        /// <typeparam name="T">The type the script must not be</typeparam>
        public static IsNotTypeScriptFilter<T> IsNotOfType<T>() where T : unmanaged, IUnikaScript, IUnikaScriptGen => IsNotTypeScriptFilter<T>.Create();
        /// <summary>
        /// Only scripts that implement this interface will pass the filter
        /// </summary>
        /// <typeparam name="T">The type of interfacethe script must implement</typeparam>
        public static IsInterfaceFilter<T> HasInterface<T>() where T : IUnikaInterface, IUnikaInterfaceGen => IsInterfaceFilter<T>.Create();
        /// <summary>
        /// Only scripts that do not implement this interface will pass the filter
        /// </summary>
        /// <typeparam name="T">The type of interfacethe script must not implement</typeparam>
        public static IsNotInterfaceFilter<T> DoesNotHaveInterface<T>() where T : IUnikaInterface, IUnikaInterfaceGen => IsNotInterfaceFilter<T>.Create();
    }

    public struct PassThroughScriptFilter : IScriptFilter
    {
        public static PassThroughScriptFilter Create() => new PassThroughScriptFilter();

        public bool Filter(Script candidate) => true;
        public bool PreFilter(UnikaScripts header) => true;
    }

    public struct UserFlagATrueScriptFilter : IScriptFilter
    {
        public static UserFlagATrueScriptFilter Create() => new UserFlagATrueScriptFilter();

        public bool Filter(Script candidate) => candidate.userFlagA;
        public bool PreFilter(UnikaScripts header) => true;
    }

    public struct UserFlagAFalseScriptFilter : IScriptFilter
    {
        public static UserFlagAFalseScriptFilter Create() => new UserFlagAFalseScriptFilter();

        public bool Filter(Script candidate) => !candidate.userFlagA;
        public bool PreFilter(UnikaScripts header) => true;
    }

    public struct UserFlagBTrueScriptFilter : IScriptFilter
    {
        public static UserFlagBTrueScriptFilter Create() => new UserFlagBTrueScriptFilter();

        public bool Filter(Script candidate) => candidate.userFlagB;
        public bool PreFilter(UnikaScripts header) => true;
    }

    public struct UserFlagBFalseScriptFilter : IScriptFilter
    {
        public static UserFlagBFalseScriptFilter Create() => new UserFlagBFalseScriptFilter();

        public bool Filter(Script candidate) => !candidate.userFlagB;
        public bool PreFilter(UnikaScripts header) => true;
    }

    public struct UserByteEqualsScriptFilter : IScriptFilter
    {
        public byte userByte;

        public static UserByteEqualsScriptFilter Create(byte userByte) => new UserByteEqualsScriptFilter {
            userByte = userByte
        };

        public bool Filter(Script candidate) => candidate.userByte == userByte;
        public bool PreFilter(UnikaScripts header) => true;
    }

    public struct UserByteNotEqualsScriptFilter : IScriptFilter
    {
        public byte userByte;

        public static UserByteNotEqualsScriptFilter Create(byte userByte) => new UserByteNotEqualsScriptFilter {
            userByte = userByte
        };

        public bool Filter(Script candidate) => candidate.userByte != userByte;
        public bool PreFilter(UnikaScripts header) => true;
    }

    public struct IsTypeScriptFilter<T> : IScriptFilter where T : unmanaged, IUnikaScript, IUnikaScriptGen
    {
        internal IdAndMask meta;

        public static IsTypeScriptFilter<T> Create() => new IsTypeScriptFilter<T> {
            meta = GetScriptRuntimeIdAndMask<T>()
        };

        public bool PreFilter(UnikaScripts header) => (header.header.bloomMask & meta.bloomMask) == meta.bloomMask;

        public bool Filter(Script candidate) => candidate.m_headerRO.scriptType == meta.runtimeId;
    }

    public struct IsNotTypeScriptFilter<T> : IScriptFilter where T : unmanaged, IUnikaScript, IUnikaScriptGen
    {
        internal IdAndMask meta;

        public static IsNotTypeScriptFilter<T> Create() => new IsNotTypeScriptFilter<T> {
            meta = GetScriptRuntimeIdAndMask<T>()
        };

        public bool Filter(Script candidate) => candidate.m_headerRO.scriptType != meta.runtimeId;
        public bool PreFilter(UnikaScripts header) => true;
    }

    public struct IsInterfaceFilter<T> : IScriptFilter where T : IUnikaInterface, IUnikaInterfaceGen
    {
        internal IdAndMask meta;

        public static IsInterfaceFilter<T> Create() => new IsInterfaceFilter<T> {
            meta = GetInterfaceRuntimeIdAndMask<T>()
        };

        public bool PreFilter(UnikaScripts header) => (header.header.bloomMask & meta.bloomMask) == meta.bloomMask;

        public bool Filter(Script candidate) => (candidate.m_headerRO.bloomMask & meta.bloomMask) == meta.bloomMask && ScriptVTable.Contains((short)candidate.m_headerRO.scriptType,
                                                                                                                                             meta.runtimeId);
    }

    public struct IsNotInterfaceFilter<T> : IScriptFilter where T : IUnikaInterface, IUnikaInterfaceGen
    {
        internal IdAndMask meta;

        public static IsNotInterfaceFilter<T> Create() => new IsNotInterfaceFilter<T> {
            meta = GetInterfaceRuntimeIdAndMask<T>()
        };

        public bool Filter(Script candidate) => (candidate.m_headerRO.bloomMask & meta.bloomMask) != meta.bloomMask || !ScriptVTable.Contains(
            (short)candidate.m_headerRO.scriptType,
            meta.runtimeId);
        public bool PreFilter(UnikaScripts header) => true;
    }
}

