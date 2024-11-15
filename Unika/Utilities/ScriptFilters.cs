using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

using static Latios.Unika.ScriptTypeInfoManager;

namespace Latios.Unika
{
    public interface IScriptFilterBase
    {
        public bool PreFilterBase(UnikaScripts header);
        public bool FilterBase(Script candidate);
    }
    public interface IScriptFilterAnyable
    {
        public bool PreFilterBase(UnikaScripts header);
        public bool FilterBase(Script candidate);
    }
    public interface IScriptFilterAllable
    {
        public bool PreFilterBase(UnikaScripts header);
        public bool FilterBase(Script candidate);
    }

    /// <summary>
    /// Defines a filter which can be used to efficiently iterate through scripts.
    /// </summary>
    public interface IScriptFilter : IScriptFilterBase, IScriptFilterAnyable, IScriptFilterAllable
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

        bool IScriptFilterBase.PreFilterBase(UnikaScripts header) => PreFilter(header);
        bool IScriptFilterBase.FilterBase(Script candidate) => Filter(candidate);
        bool IScriptFilterAnyable.PreFilterBase(UnikaScripts header) => PreFilter(header);
        bool IScriptFilterAnyable.FilterBase(Script candidate) => Filter(candidate);
        bool IScriptFilterAllable.PreFilterBase(UnikaScripts header) => PreFilter(header);
        bool IScriptFilterAllable.FilterBase(Script candidate) => Filter(candidate);
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
        /// <summary>
        /// Only scripts that pass any of the specified filters will pass this overall filter.
        /// </summary>
        /// <typeparam name="TF0">The first type of filter</typeparam>
        /// <typeparam name="TF1">The second type of filter</typeparam>
        /// <param name="filter0">The first filter instance</param>
        /// <param name="filter1">The second filter instance</param>
        public static AnyFilter<TF0, TF1> Any<TF0, TF1>(TF0 filter0, TF1 filter1)
            where TF0 : unmanaged, IScriptFilterAnyable
            where TF1 : unmanaged, IScriptFilterAnyable => AnyFilter<TF0, TF1>.Create(filter0, filter1);
        /// <summary>
        /// Only scripts that pass any of the specified filters will pass this overall filter.
        /// </summary>
        /// <typeparam name="TF0">The first type of filter</typeparam>
        /// <typeparam name="TF1">The second type of filter</typeparam>
        /// <typeparam name="TF2">The third type of filter</typeparam>
        /// <param name="filter0">The first filter instance</param>
        /// <param name="filter1">The second filter instance</param>
        /// <param name="filter2">The third filter instance</param>
        public static AnyFilter<TF0, TF1, TF2> Any<TF0, TF1, TF2>(TF0 filter0, TF1 filter1, TF2 filter2)
            where TF0 : unmanaged, IScriptFilterAnyable
            where TF1 : unmanaged, IScriptFilterAnyable
            where TF2 : unmanaged, IScriptFilterAnyable => AnyFilter<TF0, TF1, TF2>.Create(filter0, filter1, filter2);
        /// <summary>
        /// Only scripts that pass any of the specified filters will pass this overall filter.
        /// </summary>
        /// <typeparam name="TF0">The first type of filter</typeparam>
        /// <typeparam name="TF1">The second type of filter</typeparam>
        /// <typeparam name="TF2">The third type of filter</typeparam>
        /// <typeparam name="TF3">The fourth type of filter</typeparam>
        /// <param name="filter0">The first filter instance</param>
        /// <param name="filter1">The second filter instance</param>
        /// <param name="filter2">The third filter instance</param>
        /// <param name="filter3">The fourth filter instance</param>
        public static AnyFilter<TF0, TF1, TF2, TF3> Any<TF0, TF1, TF2, TF3>(TF0 filter0, TF1 filter1, TF2 filter2, TF3 filter3)
            where TF0 : unmanaged, IScriptFilterAnyable
            where TF1 : unmanaged, IScriptFilterAnyable
            where TF2 : unmanaged, IScriptFilterAnyable
            where TF3 : unmanaged, IScriptFilterAnyable => AnyFilter<TF0, TF1, TF2, TF3>.Create(filter0, filter1, filter2, filter3);
        /// <summary>
        /// Only scripts that pass any of the specified filters will pass this overall filter.
        /// </summary>
        /// <typeparam name="TF0">The first type of filter</typeparam>
        /// <typeparam name="TF1">The second type of filter</typeparam>
        /// <param name="filter0">The first filter instance</param>
        /// <param name="filter1">The second filter instance</param>
        public static AllFilter<TF0, TF1> All<TF0, TF1>(TF0 filter0, TF1 filter1)
            where TF0 : unmanaged, IScriptFilterAllable
            where TF1 : unmanaged, IScriptFilterAllable => AllFilter<TF0, TF1>.Create(filter0, filter1);
        /// <summary>
        /// Only scripts that pass any of the specified filters will pass this overall filter.
        /// </summary>
        /// <typeparam name="TF0">The first type of filter</typeparam>
        /// <typeparam name="TF1">The second type of filter</typeparam>
        /// <typeparam name="TF2">The third type of filter</typeparam>
        /// <param name="filter0">The first filter instance</param>
        /// <param name="filter1">The second filter instance</param>
        /// <param name="filter2">The third filter instance</param>
        public static AllFilter<TF0, TF1, TF2> All<TF0, TF1, TF2>(TF0 filter0, TF1 filter1, TF2 filter2)
            where TF0 : unmanaged, IScriptFilterAllable
            where TF1 : unmanaged, IScriptFilterAllable
            where TF2 : unmanaged, IScriptFilterAllable => AllFilter<TF0, TF1, TF2>.Create(filter0, filter1, filter2);
        /// <summary>
        /// Only scripts that pass any of the specified filters will pass this overall filter.
        /// </summary>
        /// <typeparam name="TF0">The first type of filter</typeparam>
        /// <typeparam name="TF1">The second type of filter</typeparam>
        /// <typeparam name="TF2">The third type of filter</typeparam>
        /// <typeparam name="TF3">The fourth type of filter</typeparam>
        /// <param name="filter0">The first filter instance</param>
        /// <param name="filter1">The second filter instance</param>
        /// <param name="filter2">The third filter instance</param>
        /// <param name="filter3">The fourth filter instance</param>
        public static AllFilter<TF0, TF1, TF2, TF3> All<TF0, TF1, TF2, TF3>(TF0 filter0, TF1 filter1, TF2 filter2, TF3 filter3)
            where TF0 : unmanaged, IScriptFilterAllable
            where TF1 : unmanaged, IScriptFilterAllable
            where TF2 : unmanaged, IScriptFilterAllable
            where TF3 : unmanaged, IScriptFilterAllable => AllFilter<TF0, TF1, TF2, TF3>.Create(filter0, filter1, filter2, filter3);
    }

    public struct PassThroughScriptFilter : IScriptFilter
    {
        public static PassThroughScriptFilter Create() => new PassThroughScriptFilter();

        public bool Filter(Script candidate) => true;
    }

    public struct UserFlagATrueScriptFilter : IScriptFilter
    {
        public static UserFlagATrueScriptFilter Create() => new UserFlagATrueScriptFilter();

        public bool Filter(Script candidate) => candidate.userFlagA;
    }

    public struct UserFlagAFalseScriptFilter : IScriptFilter
    {
        public static UserFlagAFalseScriptFilter Create() => new UserFlagAFalseScriptFilter();

        public bool Filter(Script candidate) => !candidate.userFlagA;
    }

    public struct UserFlagBTrueScriptFilter : IScriptFilter
    {
        public static UserFlagBTrueScriptFilter Create() => new UserFlagBTrueScriptFilter();

        public bool Filter(Script candidate) => candidate.userFlagB;
    }

    public struct UserFlagBFalseScriptFilter : IScriptFilter
    {
        public static UserFlagBFalseScriptFilter Create() => new UserFlagBFalseScriptFilter();

        public bool Filter(Script candidate) => !candidate.userFlagB;
    }

    public struct UserByteEqualsScriptFilter : IScriptFilter
    {
        public byte userByte;

        public static UserByteEqualsScriptFilter Create(byte userByte) => new UserByteEqualsScriptFilter {
            userByte = userByte
        };

        public bool Filter(Script candidate) => candidate.userByte == userByte;
    }

    public struct UserByteNotEqualsScriptFilter : IScriptFilter
    {
        public byte userByte;

        public static UserByteNotEqualsScriptFilter Create(byte userByte) => new UserByteNotEqualsScriptFilter {
            userByte = userByte
        };

        public bool Filter(Script candidate) => candidate.userByte != userByte;
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
    }

    public struct AnyFilter<TF0, TF1> : IScriptFilterBase, IScriptFilterAllable
        where TF0 : IScriptFilterAnyable
        where TF1 : IScriptFilterAnyable
    {
        TF0 f0;
        TF1 f1;

        public static AnyFilter<TF0, TF1> Create(TF0 f0, TF1 f1) => new AnyFilter<TF0, TF1> {
            f0 = f0, f1 = f1
        };

        public bool FilterBase(Script candidate) => f0.FilterBase(candidate) || f1.FilterBase(candidate);

        public bool PreFilterBase(UnikaScripts header) => f0.PreFilterBase(header) || f1.PreFilterBase(header);
    }

    public struct AnyFilter<TF0, TF1, TF2> : IScriptFilterBase, IScriptFilterAllable
        where TF0 : IScriptFilterAnyable
        where TF1 : IScriptFilterAnyable
        where TF2 : IScriptFilterAnyable
    {
        TF0 f0;
        TF1 f1;
        TF2 f2;

        public static AnyFilter<TF0, TF1, TF2> Create(TF0 f0, TF1 f1, TF2 f2) => new AnyFilter<TF0, TF1, TF2> {
            f0 = f0, f1 = f1, f2 = f2
        };

        public bool FilterBase(Script candidate) => f0.FilterBase(candidate) || f1.FilterBase(candidate) || f2.FilterBase(candidate);

        public bool PreFilterBase(UnikaScripts header) => f0.PreFilterBase(header) || f1.PreFilterBase(header) || f2.PreFilterBase(header);
    }

    public struct AnyFilter<TF0, TF1, TF2, TF3> : IScriptFilterBase, IScriptFilterAllable
        where TF0 : IScriptFilterAnyable
        where TF1 : IScriptFilterAnyable
        where TF2 : IScriptFilterAnyable
        where TF3 : IScriptFilterAnyable
    {
        TF0 f0;
        TF1 f1;
        TF2 f2;
        TF3 f3;

        public static AnyFilter<TF0, TF1, TF2, TF3> Create(TF0 f0, TF1 f1, TF2 f2, TF3 f3) => new AnyFilter<TF0, TF1, TF2, TF3> {
            f0 = f0, f1 = f1, f2 = f2, f3 = f3
        };

        public bool FilterBase(Script candidate) => f0.FilterBase(candidate) || f1.FilterBase(candidate) || f2.FilterBase(candidate) || f3.FilterBase(candidate);

        public bool PreFilterBase(UnikaScripts header) => f0.PreFilterBase(header) || f1.PreFilterBase(header) || f2.PreFilterBase(header) || f3.PreFilterBase(header);
    }

    public struct AllFilter<TF0, TF1> : IScriptFilterBase, IScriptFilterAnyable
        where TF0 : IScriptFilterAllable
        where TF1 : IScriptFilterAllable
    {
        TF0 f0;
        TF1 f1;

        public static AllFilter<TF0, TF1> Create(TF0 f0, TF1 f1) => new AllFilter<TF0, TF1> {
            f0 = f0, f1 = f1
        };

        public bool FilterBase(Script candidate) => f0.FilterBase(candidate) && f1.FilterBase(candidate);

        public bool PreFilterBase(UnikaScripts header) => f0.PreFilterBase(header) && f1.PreFilterBase(header);
    }

    public struct AllFilter<TF0, TF1, TF2> : IScriptFilterBase, IScriptFilterAnyable
        where TF0 : IScriptFilterAllable
        where TF1 : IScriptFilterAllable
        where TF2 : IScriptFilterAllable
    {
        TF0 f0;
        TF1 f1;
        TF2 f2;

        public static AllFilter<TF0, TF1, TF2> Create(TF0 f0, TF1 f1, TF2 f2) => new AllFilter<TF0, TF1, TF2> {
            f0 = f0, f1 = f1, f2 = f2
        };

        public bool FilterBase(Script candidate) => f0.FilterBase(candidate) && f1.FilterBase(candidate) && f2.FilterBase(candidate);

        public bool PreFilterBase(UnikaScripts header) => f0.PreFilterBase(header) && f1.PreFilterBase(header) && f2.PreFilterBase(header);
    }

    public struct AllFilter<TF0, TF1, TF2, TF3> : IScriptFilterBase, IScriptFilterAnyable
        where TF0 : IScriptFilterAllable
        where TF1 : IScriptFilterAllable
        where TF2 : IScriptFilterAllable
        where TF3 : IScriptFilterAllable
    {
        TF0 f0;
        TF1 f1;
        TF2 f2;
        TF3 f3;

        public static AllFilter<TF0, TF1, TF2, TF3> Create(TF0 f0, TF1 f1, TF2 f2, TF3 f3) => new AllFilter<TF0, TF1, TF2, TF3> {
            f0 = f0, f1 = f1, f2 = f2, f3 = f3
        };

        public bool FilterBase(Script candidate) => f0.FilterBase(candidate) && f1.FilterBase(candidate) && f2.FilterBase(candidate) && f3.FilterBase(candidate);

        public bool PreFilterBase(UnikaScripts header) => f0.PreFilterBase(header) && f1.PreFilterBase(header) && f2.PreFilterBase(header) && f3.PreFilterBase(header);
    }
}

