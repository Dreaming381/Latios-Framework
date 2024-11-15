using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

using Noop = Latios.Unika.PassThroughScriptFilter;

namespace Latios.Unika
{
    public interface IUntypedScriptFilterCascade<TF0, TF1, TF2, TF3, TF4, TF5, TF6, TF7>
        where TF0 : unmanaged, IScriptFilterBase
        where TF1 : unmanaged, IScriptFilterBase
        where TF2 : unmanaged, IScriptFilterBase
        where TF3 : unmanaged, IScriptFilterBase
        where TF4 : unmanaged, IScriptFilterBase
        where TF5 : unmanaged, IScriptFilterBase
        where TF6 : unmanaged, IScriptFilterBase
        where TF7 : unmanaged, IScriptFilterBase
    {
        public UntypedScriptFilteredEnumerator<TF0, TF1, TF2, TF3, TF4, TF5, TF6, TF7> GetEnumerator();
    }

    public interface ITypedScriptFilterCascade<TType, TF0, TF1, TF2, TF3, TF4, TF5, TF6, TF7>
        where TType : unmanaged, IScriptTypedExtensionsApi
        where TF0 : unmanaged, IScriptFilterBase
        where TF1 : unmanaged, IScriptFilterBase
        where TF2 : unmanaged, IScriptFilterBase
        where TF3 : unmanaged, IScriptFilterBase
        where TF4 : unmanaged, IScriptFilterBase
        where TF5 : unmanaged, IScriptFilterBase
        where TF6 : unmanaged, IScriptFilterBase
        where TF7 : unmanaged, IScriptFilterBase
    {
        public TypedScriptFilteredEnumerator<TType, TF0, TF1, TF2, TF3, TF4, TF5, TF6, TF7> GetEnumerator();
    }

    public struct UntypedScriptFilterCascade<TF0, TF1, TF2, TF3, TF4, TF5, TF6, TF7> : IUntypedScriptFilterCascade<TF0, TF1, TF2, TF3, TF4, TF5, TF6, TF7>
        where TF0 : unmanaged, IScriptFilterBase
        where TF1 : unmanaged, IScriptFilterBase
        where TF2 : unmanaged, IScriptFilterBase
        where TF3 : unmanaged, IScriptFilterBase
        where TF4 : unmanaged, IScriptFilterBase
        where TF5 : unmanaged, IScriptFilterBase
        where TF6 : unmanaged, IScriptFilterBase
        where TF7 : unmanaged, IScriptFilterBase
    {
        EntityScriptCollection allScripts;
        TF0                    f0;
        TF1                    f1;
        TF2                    f2;
        TF3                    f3;
        TF4                    f4;
        TF5                    f5;
        TF6                    f6;
        TF7                    f7;

        public UntypedScriptFilterCascade(EntityScriptCollection allScripts, TF0 f0, TF1 f1, TF2 f2, TF3 f3, TF4 f4, TF5 f5, TF6 f6, TF7 f7)
        {
            this.allScripts = allScripts;
            this.f0         = f0;
            this.f1         = f1;
            this.f2         = f2;
            this.f3         = f3;
            this.f4         = f4;
            this.f5         = f5;
            this.f6         = f6;
            this.f7         = f7;
        }

        public UntypedScriptFilteredEnumerator<TF0, TF1, TF2, TF3, TF4, TF5, TF6, TF7> GetEnumerator() =>
        new UntypedScriptFilteredEnumerator<TF0, TF1, TF2, TF3, TF4, TF5, TF6, TF7>(allScripts, f0, f1, f2, f3, f4, f5, f6, f7);
    }

    public struct UntypedScriptFilterCascade<TF0, TF1, TF2, TF3, TF4, TF5, TF6> : IUntypedScriptFilterCascade<TF0, TF1, TF2, TF3, TF4, TF5, TF6, Noop>
        where TF0 : unmanaged, IScriptFilterBase
        where TF1 : unmanaged, IScriptFilterBase
        where TF2 : unmanaged, IScriptFilterBase
        where TF3 : unmanaged, IScriptFilterBase
        where TF4 : unmanaged, IScriptFilterBase
        where TF5 : unmanaged, IScriptFilterBase
        where TF6 : unmanaged, IScriptFilterBase
    {
        EntityScriptCollection allScripts;
        TF0                    f0;
        TF1                    f1;
        TF2                    f2;
        TF3                    f3;
        TF4                    f4;
        TF5                    f5;
        TF6                    f6;

        public UntypedScriptFilterCascade(EntityScriptCollection allScripts, TF0 f0, TF1 f1, TF2 f2, TF3 f3, TF4 f4, TF5 f5, TF6 f6)
        {
            this.allScripts = allScripts;
            this.f0         = f0;
            this.f1         = f1;
            this.f2         = f2;
            this.f3         = f3;
            this.f4         = f4;
            this.f5         = f5;
            this.f6         = f6;
        }

        public UntypedScriptFilteredEnumerator<TF0, TF1, TF2, TF3, TF4, TF5, TF6, Noop> GetEnumerator()
        {
            var noop = Noop.Create();
            return new UntypedScriptFilteredEnumerator<TF0, TF1, TF2, TF3, TF4, TF5, TF6, Noop>(allScripts, f0, f1, f2, f3, f4, f5, f6, noop);
        }

        /// <summary>
        /// Appends an IScriptFilterBase which applies filtering to the scripts in the collection. Filters are applied from first to last.
        /// </summary>
        /// <typeparam name="TNew">The type of new filter to append</typeparam>
        /// <param name="filter">The new filter instance to append</param>
        /// <returns>A continuation of this cascade builder API</returns>
        public UntypedScriptFilterCascade<TF0, TF1, TF2, TF3, TF4, TF5, TF6, TNew> Where<TNew>(TNew filter) where TNew : unmanaged, IScriptFilterBase =>
        new UntypedScriptFilterCascade<TF0, TF1, TF2, TF3, TF4, TF5, TF6, TNew>(allScripts, f0, f1, f2, f3, f4, f5, f6, filter);
    }

    public struct UntypedScriptFilterCascade<TF0, TF1, TF2, TF3, TF4, TF5> : IUntypedScriptFilterCascade<TF0, TF1, TF2, TF3, TF4, TF5, Noop, Noop>
        where TF0 : unmanaged, IScriptFilterBase
        where TF1 : unmanaged, IScriptFilterBase
        where TF2 : unmanaged, IScriptFilterBase
        where TF3 : unmanaged, IScriptFilterBase
        where TF4 : unmanaged, IScriptFilterBase
        where TF5 : unmanaged, IScriptFilterBase
    {
        EntityScriptCollection allScripts;
        TF0                    f0;
        TF1                    f1;
        TF2                    f2;
        TF3                    f3;
        TF4                    f4;
        TF5                    f5;

        public UntypedScriptFilterCascade(EntityScriptCollection allScripts, TF0 f0, TF1 f1, TF2 f2, TF3 f3, TF4 f4, TF5 f5)
        {
            this.allScripts = allScripts;
            this.f0         = f0;
            this.f1         = f1;
            this.f2         = f2;
            this.f3         = f3;
            this.f4         = f4;
            this.f5         = f5;
        }

        public UntypedScriptFilteredEnumerator<TF0, TF1, TF2, TF3, TF4, TF5, Noop, Noop> GetEnumerator()
        {
            var noop = Noop.Create();
            return new UntypedScriptFilteredEnumerator<TF0, TF1, TF2, TF3, TF4, TF5, Noop, Noop>(allScripts, f0, f1, f2, f3, f4, f5, noop, noop);
        }

        /// <summary>
        /// Appends an IScriptFilterBase which applies filtering to the scripts in the collection. Filters are applied from first to last.
        /// </summary>
        /// <typeparam name="TNew">The type of new filter to append</typeparam>
        /// <param name="filter">The new filter instance to append</param>
        /// <returns>A continuation of this cascade builder API</returns>
        public UntypedScriptFilterCascade<TF0, TF1, TF2, TF3, TF4, TF5, TNew> Where<TNew>(TNew filter) where TNew : unmanaged, IScriptFilterBase =>
        new UntypedScriptFilterCascade<TF0, TF1, TF2, TF3, TF4, TF5, TNew>(allScripts, f0, f1, f2, f3, f4, f5, filter);
    }

    public struct UntypedScriptFilterCascade<TF0, TF1, TF2, TF3, TF4> : IUntypedScriptFilterCascade<TF0, TF1, TF2, TF3, TF4, Noop, Noop, Noop>
        where TF0 : unmanaged, IScriptFilterBase
        where TF1 : unmanaged, IScriptFilterBase
        where TF2 : unmanaged, IScriptFilterBase
        where TF3 : unmanaged, IScriptFilterBase
        where TF4 : unmanaged, IScriptFilterBase
    {
        EntityScriptCollection allScripts;
        TF0                    f0;
        TF1                    f1;
        TF2                    f2;
        TF3                    f3;
        TF4                    f4;

        public UntypedScriptFilterCascade(EntityScriptCollection allScripts, TF0 f0, TF1 f1, TF2 f2, TF3 f3, TF4 f4)
        {
            this.allScripts = allScripts;
            this.f0         = f0;
            this.f1         = f1;
            this.f2         = f2;
            this.f3         = f3;
            this.f4         = f4;
        }

        public UntypedScriptFilteredEnumerator<TF0, TF1, TF2, TF3, TF4, Noop, Noop, Noop> GetEnumerator()
        {
            var noop = Noop.Create();
            return new UntypedScriptFilteredEnumerator<TF0, TF1, TF2, TF3, TF4, Noop, Noop, Noop>(allScripts, f0, f1, f2, f3, f4, noop, noop, noop);
        }

        /// <summary>
        /// Appends an IScriptFilterBase which applies filtering to the scripts in the collection. Filters are applied from first to last.
        /// </summary>
        /// <typeparam name="TNew">The type of new filter to append</typeparam>
        /// <param name="filter">The new filter instance to append</param>
        /// <returns>A continuation of this cascade builder API</returns>
        public UntypedScriptFilterCascade<TF0, TF1, TF2, TF3, TF4, TNew> Where<TNew>(TNew filter) where TNew : unmanaged, IScriptFilterBase =>
        new UntypedScriptFilterCascade<TF0, TF1, TF2, TF3, TF4, TNew>(allScripts, f0, f1, f2, f3, f4, filter);
    }

    public struct UntypedScriptFilterCascade<TF0, TF1, TF2, TF3> : IUntypedScriptFilterCascade<TF0, TF1, TF2, TF3, Noop, Noop, Noop, Noop>
        where TF0 : unmanaged, IScriptFilterBase
        where TF1 : unmanaged, IScriptFilterBase
        where TF2 : unmanaged, IScriptFilterBase
        where TF3 : unmanaged, IScriptFilterBase
    {
        EntityScriptCollection allScripts;
        TF0                    f0;
        TF1                    f1;
        TF2                    f2;
        TF3                    f3;

        public UntypedScriptFilterCascade(EntityScriptCollection allScripts, TF0 f0, TF1 f1, TF2 f2, TF3 f3)
        {
            this.allScripts = allScripts;
            this.f0         = f0;
            this.f1         = f1;
            this.f2         = f2;
            this.f3         = f3;
        }

        public UntypedScriptFilteredEnumerator<TF0, TF1, TF2, TF3, Noop, Noop, Noop, Noop> GetEnumerator()
        {
            var noop = Noop.Create();
            return new UntypedScriptFilteredEnumerator<TF0, TF1, TF2, TF3, Noop, Noop, Noop, Noop>(allScripts, f0, f1, f2, f3, noop, noop, noop, noop);
        }

        /// <summary>
        /// Appends an IScriptFilterBase which applies filtering to the scripts in the collection. Filters are applied from first to last.
        /// </summary>
        /// <typeparam name="TNew">The type of new filter to append</typeparam>
        /// <param name="filter">The new filter instance to append</param>
        /// <returns>A continuation of this cascade builder API</returns>
        public UntypedScriptFilterCascade<TF0, TF1, TF2, TF3, TNew> Where<TNew>(TNew filter) where TNew : unmanaged, IScriptFilterBase =>
        new UntypedScriptFilterCascade<TF0, TF1, TF2, TF3, TNew>(allScripts, f0, f1, f2, f3, filter);
    }

    public struct UntypedScriptFilterCascade<TF0, TF1, TF2> : IUntypedScriptFilterCascade<TF0, TF1, TF2, Noop, Noop, Noop, Noop, Noop>
        where TF0 : unmanaged, IScriptFilterBase
        where TF1 : unmanaged, IScriptFilterBase
        where TF2 : unmanaged, IScriptFilterBase
    {
        EntityScriptCollection allScripts;
        TF0                    f0;
        TF1                    f1;
        TF2                    f2;

        public UntypedScriptFilterCascade(EntityScriptCollection allScripts, TF0 f0, TF1 f1, TF2 f2)
        {
            this.allScripts = allScripts;
            this.f0         = f0;
            this.f1         = f1;
            this.f2         = f2;
        }

        public UntypedScriptFilteredEnumerator<TF0, TF1, TF2, Noop, Noop, Noop, Noop, Noop> GetEnumerator()
        {
            var noop = Noop.Create();
            return new UntypedScriptFilteredEnumerator<TF0, TF1, TF2, Noop, Noop, Noop, Noop, Noop>(allScripts, f0, f1, f2, noop, noop, noop, noop, noop);
        }

        /// <summary>
        /// Appends an IScriptFilterBase which applies filtering to the scripts in the collection. Filters are applied from first to last.
        /// </summary>
        /// <typeparam name="TNew">The type of new filter to append</typeparam>
        /// <param name="filter">The new filter instance to append</param>
        /// <returns>A continuation of this cascade builder API</returns>
        public UntypedScriptFilterCascade<TF0, TF1, TF2, TNew> Where<TNew>(TNew filter) where TNew : unmanaged, IScriptFilterBase =>
        new UntypedScriptFilterCascade<TF0, TF1, TF2, TNew>(allScripts, f0, f1, f2, filter);
    }

    public struct UntypedScriptFilterCascade<TF0, TF1> : IUntypedScriptFilterCascade<TF0, TF1, Noop, Noop, Noop, Noop, Noop, Noop>
        where TF0 : unmanaged, IScriptFilterBase
        where TF1 : unmanaged, IScriptFilterBase
    {
        EntityScriptCollection allScripts;
        TF0                    f0;
        TF1                    f1;

        public UntypedScriptFilterCascade(EntityScriptCollection allScripts, TF0 f0, TF1 f1)
        {
            this.allScripts = allScripts;
            this.f0         = f0;
            this.f1         = f1;
        }

        public UntypedScriptFilteredEnumerator<TF0, TF1, Noop, Noop, Noop, Noop, Noop, Noop> GetEnumerator()
        {
            var noop = Noop.Create();
            return new UntypedScriptFilteredEnumerator<TF0, TF1, Noop, Noop, Noop, Noop, Noop, Noop>(allScripts, f0, f1, noop, noop, noop, noop, noop, noop);
        }

        /// <summary>
        /// Appends an IScriptFilterBase which applies filtering to the scripts in the collection. Filters are applied from first to last.
        /// </summary>
        /// <typeparam name="TNew">The type of new filter to append</typeparam>
        /// <param name="filter">The new filter instance to append</param>
        /// <returns>A continuation of this cascade builder API</returns>
        public UntypedScriptFilterCascade<TF0, TF1, TNew> Where<TNew>(TNew filter) where TNew : unmanaged, IScriptFilterBase =>
        new UntypedScriptFilterCascade<TF0, TF1, TNew>(allScripts, f0, f1, filter);
    }

    public struct UntypedScriptFilterCascade<TF0> : IUntypedScriptFilterCascade<TF0, Noop, Noop, Noop, Noop, Noop, Noop, Noop>
        where TF0 : unmanaged, IScriptFilterBase
    {
        EntityScriptCollection allScripts;
        TF0                    f0;

        public UntypedScriptFilterCascade(EntityScriptCollection allScripts, TF0 f0)
        {
            this.allScripts = allScripts;
            this.f0         = f0;
        }

        public UntypedScriptFilteredEnumerator<TF0, Noop, Noop, Noop, Noop, Noop, Noop, Noop> GetEnumerator()
        {
            var noop = Noop.Create();
            return new UntypedScriptFilteredEnumerator<TF0, Noop, Noop, Noop, Noop, Noop, Noop, Noop>(allScripts, f0, noop, noop, noop, noop, noop, noop, noop);
        }

        /// <summary>
        /// Appends an IScriptFilterBase which applies filtering to the scripts in the collection. Filters are applied from first to last.
        /// </summary>
        /// <typeparam name="TNew">The type of new filter to append</typeparam>
        /// <param name="filter">The new filter instance to append</param>
        /// <returns>A continuation of this cascade builder API</returns>
        public UntypedScriptFilterCascade<TF0, TNew> Where<TNew>(TNew filter) where TNew : unmanaged, IScriptFilterBase =>
        new UntypedScriptFilterCascade<TF0, TNew>(allScripts, f0, filter);
    }

    public struct TypedScriptFilterCascade<TType, TF0, TF1, TF2, TF3, TF4, TF5, TF6, TF7> : ITypedScriptFilterCascade<TType, TF0, TF1, TF2, TF3, TF4, TF5, TF6, TF7>
        where TType : unmanaged, IScriptTypedExtensionsApi
        where TF0 : unmanaged, IScriptFilterBase
        where TF1 : unmanaged, IScriptFilterBase
        where TF2 : unmanaged, IScriptFilterBase
        where TF3 : unmanaged, IScriptFilterBase
        where TF4 : unmanaged, IScriptFilterBase
        where TF5 : unmanaged, IScriptFilterBase
        where TF6 : unmanaged, IScriptFilterBase
        where TF7 : unmanaged, IScriptFilterBase
    {
        EntityScriptCollection allScripts;
        TF0                    f0;
        TF1                    f1;
        TF2                    f2;
        TF3                    f3;
        TF4                    f4;
        TF5                    f5;
        TF6                    f6;
        TF7                    f7;

        public TypedScriptFilterCascade(EntityScriptCollection allScripts, TF0 f0, TF1 f1, TF2 f2, TF3 f3, TF4 f4, TF5 f5, TF6 f6, TF7 f7)
        {
            this.allScripts = allScripts;
            this.f0         = f0;
            this.f1         = f1;
            this.f2         = f2;
            this.f3         = f3;
            this.f4         = f4;
            this.f5         = f5;
            this.f6         = f6;
            this.f7         = f7;
        }

        public TypedScriptFilteredEnumerator<TType, TF0, TF1, TF2, TF3, TF4, TF5, TF6, TF7> GetEnumerator() =>
        new TypedScriptFilteredEnumerator<TType, TF0, TF1, TF2, TF3, TF4, TF5, TF6, TF7>(default(TType), allScripts, f0, f1, f2, f3, f4, f5, f6, f7);
    }

    public struct TypedScriptFilterCascade<TType, TF0, TF1, TF2, TF3, TF4, TF5, TF6> : ITypedScriptFilterCascade<TType, TF0, TF1, TF2, TF3, TF4, TF5, TF6, Noop>
        where TType : unmanaged, IScriptTypedExtensionsApi
        where TF0 : unmanaged, IScriptFilterBase
        where TF1 : unmanaged, IScriptFilterBase
        where TF2 : unmanaged, IScriptFilterBase
        where TF3 : unmanaged, IScriptFilterBase
        where TF4 : unmanaged, IScriptFilterBase
        where TF5 : unmanaged, IScriptFilterBase
        where TF6 : unmanaged, IScriptFilterBase
    {
        EntityScriptCollection allScripts;
        TF0                    f0;
        TF1                    f1;
        TF2                    f2;
        TF3                    f3;
        TF4                    f4;
        TF5                    f5;
        TF6                    f6;

        public TypedScriptFilterCascade(EntityScriptCollection allScripts, TF0 f0, TF1 f1, TF2 f2, TF3 f3, TF4 f4, TF5 f5, TF6 f6)
        {
            this.allScripts = allScripts;
            this.f0         = f0;
            this.f1         = f1;
            this.f2         = f2;
            this.f3         = f3;
            this.f4         = f4;
            this.f5         = f5;
            this.f6         = f6;
        }

        public TypedScriptFilteredEnumerator<TType, TF0, TF1, TF2, TF3, TF4, TF5, TF6, Noop> GetEnumerator()
        {
            var noop = Noop.Create();
            return new TypedScriptFilteredEnumerator<TType, TF0, TF1, TF2, TF3, TF4, TF5, TF6, Noop>(default(TType), allScripts, f0, f1, f2, f3, f4, f5, f6, noop);
        }

        /// <summary>
        /// Appends an IScriptFilterBase which applies filtering to the scripts in the collection. Filters are applied from first to last.
        /// Warning: Filters may be applied to scripts of the wrong type, as the final determination of whether the script matches the type is performed last.
        /// </summary>
        /// <typeparam name="TNew">The type of new filter to append</typeparam>
        /// <param name="filter">The new filter instance to append</param>
        /// <returns>A continuation of this cascade builder API</returns>
        public TypedScriptFilterCascade<TType, TF0, TF1, TF2, TF3, TF4, TF5, TF6, TNew> Where<TNew>(TNew filter) where TNew : unmanaged, IScriptFilterBase =>
        new TypedScriptFilterCascade<TType, TF0, TF1, TF2, TF3, TF4, TF5, TF6, TNew>(allScripts, f0, f1, f2, f3, f4, f5, f6, filter);
    }

    public struct TypedScriptFilterCascade<TType, TF0, TF1, TF2, TF3, TF4, TF5> : ITypedScriptFilterCascade<TType, TF0, TF1, TF2, TF3, TF4, TF5, Noop, Noop>
        where TType : unmanaged, IScriptTypedExtensionsApi
        where TF0 : unmanaged, IScriptFilterBase
        where TF1 : unmanaged, IScriptFilterBase
        where TF2 : unmanaged, IScriptFilterBase
        where TF3 : unmanaged, IScriptFilterBase
        where TF4 : unmanaged, IScriptFilterBase
        where TF5 : unmanaged, IScriptFilterBase
    {
        EntityScriptCollection allScripts;
        TF0                    f0;
        TF1                    f1;
        TF2                    f2;
        TF3                    f3;
        TF4                    f4;
        TF5                    f5;

        public TypedScriptFilterCascade(EntityScriptCollection allScripts, TF0 f0, TF1 f1, TF2 f2, TF3 f3, TF4 f4, TF5 f5)
        {
            this.allScripts = allScripts;
            this.f0         = f0;
            this.f1         = f1;
            this.f2         = f2;
            this.f3         = f3;
            this.f4         = f4;
            this.f5         = f5;
        }

        public TypedScriptFilteredEnumerator<TType, TF0, TF1, TF2, TF3, TF4, TF5, Noop, Noop> GetEnumerator()
        {
            var noop = Noop.Create();
            return new TypedScriptFilteredEnumerator<TType, TF0, TF1, TF2, TF3, TF4, TF5, Noop, Noop>(default(TType), allScripts, f0, f1, f2, f3, f4, f5, noop, noop);
        }

        /// <summary>
        /// Appends an IScriptFilterBase which applies filtering to the scripts in the collection. Filters are applied from first to last.
        /// Warning: Filters may be applied to scripts of the wrong type, as the final determination of whether the script matches the type is performed last.
        /// </summary>
        /// <typeparam name="TNew">The type of new filter to append</typeparam>
        /// <param name="filter">The new filter instance to append</param>
        /// <returns>A continuation of this cascade builder API</returns>
        public TypedScriptFilterCascade<TType, TF0, TF1, TF2, TF3, TF4, TF5, TNew> Where<TNew>(TNew filter) where TNew : unmanaged, IScriptFilterBase =>
        new TypedScriptFilterCascade<TType, TF0, TF1, TF2, TF3, TF4, TF5, TNew>(allScripts, f0, f1, f2, f3, f4, f5, filter);
    }

    public struct TypedScriptFilterCascade<TType, TF0, TF1, TF2, TF3, TF4> : ITypedScriptFilterCascade<TType, TF0, TF1, TF2, TF3, TF4, Noop, Noop, Noop>
        where TType : unmanaged, IScriptTypedExtensionsApi
        where TF0 : unmanaged, IScriptFilterBase
        where TF1 : unmanaged, IScriptFilterBase
        where TF2 : unmanaged, IScriptFilterBase
        where TF3 : unmanaged, IScriptFilterBase
        where TF4 : unmanaged, IScriptFilterBase
    {
        EntityScriptCollection allScripts;
        TF0                    f0;
        TF1                    f1;
        TF2                    f2;
        TF3                    f3;
        TF4                    f4;

        public TypedScriptFilterCascade(EntityScriptCollection allScripts, TF0 f0, TF1 f1, TF2 f2, TF3 f3, TF4 f4)
        {
            this.allScripts = allScripts;
            this.f0         = f0;
            this.f1         = f1;
            this.f2         = f2;
            this.f3         = f3;
            this.f4         = f4;
        }

        public TypedScriptFilteredEnumerator<TType, TF0, TF1, TF2, TF3, TF4, Noop, Noop, Noop> GetEnumerator()
        {
            var noop = Noop.Create();
            return new TypedScriptFilteredEnumerator<TType, TF0, TF1, TF2, TF3, TF4, Noop, Noop, Noop>(default(TType), allScripts, f0, f1, f2, f3, f4, noop, noop, noop);
        }

        /// <summary>
        /// Appends an IScriptFilterBase which applies filtering to the scripts in the collection. Filters are applied from first to last.
        /// Warning: Filters may be applied to scripts of the wrong type, as the final determination of whether the script matches the type is performed last.
        /// </summary>
        /// <typeparam name="TNew">The type of new filter to append</typeparam>
        /// <param name="filter">The new filter instance to append</param>
        /// <returns>A continuation of this cascade builder API</returns>
        public TypedScriptFilterCascade<TType, TF0, TF1, TF2, TF3, TF4, TNew> Where<TNew>(TNew filter) where TNew : unmanaged, IScriptFilterBase =>
        new TypedScriptFilterCascade<TType, TF0, TF1, TF2, TF3, TF4, TNew>(allScripts, f0, f1, f2, f3, f4, filter);
    }

    public struct TypedScriptFilterCascade<TType, TF0, TF1, TF2, TF3> : ITypedScriptFilterCascade<TType, TF0, TF1, TF2, TF3, Noop, Noop, Noop, Noop>
        where TType : unmanaged, IScriptTypedExtensionsApi
        where TF0 : unmanaged, IScriptFilterBase
        where TF1 : unmanaged, IScriptFilterBase
        where TF2 : unmanaged, IScriptFilterBase
        where TF3 : unmanaged, IScriptFilterBase
    {
        EntityScriptCollection allScripts;
        TF0                    f0;
        TF1                    f1;
        TF2                    f2;
        TF3                    f3;

        public TypedScriptFilterCascade(EntityScriptCollection allScripts, TF0 f0, TF1 f1, TF2 f2, TF3 f3)
        {
            this.allScripts = allScripts;
            this.f0         = f0;
            this.f1         = f1;
            this.f2         = f2;
            this.f3         = f3;
        }

        public TypedScriptFilteredEnumerator<TType, TF0, TF1, TF2, TF3, Noop, Noop, Noop, Noop> GetEnumerator()
        {
            var noop = Noop.Create();
            return new TypedScriptFilteredEnumerator<TType, TF0, TF1, TF2, TF3, Noop, Noop, Noop, Noop>(default(TType), allScripts, f0, f1, f2, f3, noop, noop, noop, noop);
        }

        /// <summary>
        /// Appends an IScriptFilterBase which applies filtering to the scripts in the collection. Filters are applied from first to last.
        /// Warning: Filters may be applied to scripts of the wrong type, as the final determination of whether the script matches the type is performed last.
        /// </summary>
        /// <typeparam name="TNew">The type of new filter to append</typeparam>
        /// <param name="filter">The new filter instance to append</param>
        /// <returns>A continuation of this cascade builder API</returns>
        public TypedScriptFilterCascade<TType, TF0, TF1, TF2, TF3, TNew> Where<TNew>(TNew filter) where TNew : unmanaged, IScriptFilterBase =>
        new TypedScriptFilterCascade<TType, TF0, TF1, TF2, TF3, TNew>(allScripts, f0, f1, f2, f3, filter);
    }

    public struct TypedScriptFilterCascade<TType, TF0, TF1, TF2> : ITypedScriptFilterCascade<TType, TF0, TF1, TF2, Noop, Noop, Noop, Noop, Noop>
        where TType : unmanaged, IScriptTypedExtensionsApi
        where TF0 : unmanaged, IScriptFilterBase
        where TF1 : unmanaged, IScriptFilterBase
        where TF2 : unmanaged, IScriptFilterBase
    {
        EntityScriptCollection allScripts;
        TF0                    f0;
        TF1                    f1;
        TF2                    f2;

        public TypedScriptFilterCascade(EntityScriptCollection allScripts, TF0 f0, TF1 f1, TF2 f2)
        {
            this.allScripts = allScripts;
            this.f0         = f0;
            this.f1         = f1;
            this.f2         = f2;
        }

        public TypedScriptFilteredEnumerator<TType, TF0, TF1, TF2, Noop, Noop, Noop, Noop, Noop> GetEnumerator()
        {
            var noop = Noop.Create();
            return new TypedScriptFilteredEnumerator<TType, TF0, TF1, TF2, Noop, Noop, Noop, Noop, Noop>(default(TType), allScripts, f0, f1, f2, noop, noop, noop, noop, noop);
        }

        /// <summary>
        /// Appends an IScriptFilterBase which applies filtering to the scripts in the collection. Filters are applied from first to last.
        /// Warning: Filters may be applied to scripts of the wrong type, as the final determination of whether the script matches the type is performed last.
        /// </summary>
        /// <typeparam name="TNew">The type of new filter to append</typeparam>
        /// <param name="filter">The new filter instance to append</param>
        /// <returns>A continuation of this cascade builder API</returns>
        public TypedScriptFilterCascade<TType, TF0, TF1, TF2, TNew> Where<TNew>(TNew filter) where TNew : unmanaged, IScriptFilterBase =>
        new TypedScriptFilterCascade<TType, TF0, TF1, TF2, TNew>(allScripts, f0, f1, f2, filter);
    }

    public struct TypedScriptFilterCascade<TType, TF0, TF1> : ITypedScriptFilterCascade<TType, TF0, TF1, Noop, Noop, Noop, Noop, Noop, Noop>
        where TType : unmanaged, IScriptTypedExtensionsApi
        where TF0 : unmanaged, IScriptFilterBase
        where TF1 : unmanaged, IScriptFilterBase
    {
        EntityScriptCollection allScripts;
        TF0                    f0;
        TF1                    f1;

        public TypedScriptFilterCascade(EntityScriptCollection allScripts, TF0 f0, TF1 f1)
        {
            this.allScripts = allScripts;
            this.f0         = f0;
            this.f1         = f1;
        }

        public TypedScriptFilteredEnumerator<TType, TF0, TF1, Noop, Noop, Noop, Noop, Noop, Noop> GetEnumerator()
        {
            var noop = Noop.Create();
            return new TypedScriptFilteredEnumerator<TType, TF0, TF1, Noop, Noop, Noop, Noop, Noop, Noop>(default(TType), allScripts, f0, f1, noop, noop, noop, noop, noop, noop);
        }

        /// <summary>
        /// Appends an IScriptFilterBase which applies filtering to the scripts in the collection. Filters are applied from first to last.
        /// Warning: Filters may be applied to scripts of the wrong type, as the final determination of whether the script matches the type is performed last.
        /// </summary>
        /// <typeparam name="TNew">The type of new filter to append</typeparam>
        /// <param name="filter">The new filter instance to append</param>
        /// <returns>A continuation of this cascade builder API</returns>
        public TypedScriptFilterCascade<TType, TF0, TF1, TNew> Where<TNew>(TNew filter) where TNew : unmanaged, IScriptFilterBase =>
        new TypedScriptFilterCascade<TType, TF0, TF1, TNew>(allScripts, f0, f1, filter);
    }

    public struct TypedScriptFilterCascade<TType, TF0> : ITypedScriptFilterCascade<TType, TF0, Noop, Noop, Noop, Noop, Noop, Noop, Noop>
        where TType : unmanaged, IScriptTypedExtensionsApi
        where TF0 : unmanaged, IScriptFilterBase
    {
        EntityScriptCollection allScripts;
        TF0                    f0;

        public TypedScriptFilterCascade(EntityScriptCollection allScripts, TF0 f0)
        {
            this.allScripts = allScripts;
            this.f0         = f0;
        }

        public TypedScriptFilteredEnumerator<TType, TF0, Noop, Noop, Noop, Noop, Noop, Noop, Noop> GetEnumerator()
        {
            var noop = Noop.Create();
            return new TypedScriptFilteredEnumerator<TType, TF0, Noop, Noop, Noop, Noop, Noop, Noop, Noop>(default(TType), allScripts, f0, noop, noop, noop, noop, noop, noop,
                                                                                                           noop);
        }

        /// <summary>
        /// Appends an IScriptFilterBase which applies filtering to the scripts in the collection. Filters are applied from first to last.
        /// Warning: Filters may be applied to scripts of the wrong type, as the final determination of whether the script matches the type is performed last.
        /// </summary>
        /// <typeparam name="TNew">The type of new filter to append</typeparam>
        /// <param name="filter">The new filter instance to append</param>
        /// <returns>A continuation of this cascade builder API</returns>
        public TypedScriptFilterCascade<TType, TF0, TNew> Where<TNew>(TNew filter) where TNew : unmanaged, IScriptFilterBase =>
        new TypedScriptFilterCascade<TType, TF0, TNew>(allScripts, f0, filter);
    }

    public struct TypedScriptFilterCascade<TType> : ITypedScriptFilterCascade<TType, Noop, Noop, Noop, Noop, Noop, Noop, Noop, Noop>
        where TType : unmanaged, IScriptTypedExtensionsApi
    {
        EntityScriptCollection allScripts;

        public TypedScriptFilterCascade(EntityScriptCollection allScripts)
        {
            this.allScripts = allScripts;
        }

        public TypedScriptFilteredEnumerator<TType, Noop, Noop, Noop, Noop, Noop, Noop, Noop, Noop> GetEnumerator()
        {
            var noop = Noop.Create();
            return new TypedScriptFilteredEnumerator<TType, Noop, Noop, Noop, Noop, Noop, Noop, Noop, Noop>(default(TType),
                                                                                                            allScripts,
                                                                                                            noop,
                                                                                                            noop,
                                                                                                            noop,
                                                                                                            noop,
                                                                                                            noop,
                                                                                                            noop,
                                                                                                            noop,
                                                                                                            noop);
        }

        /// <summary>
        /// Appends an IScriptFilterBase which applies filtering to the scripts in the collection. Filters are applied from first to last.
        /// Warning: Filters may be applied to scripts of the wrong type, as the final determination of whether the script matches the type is performed last.
        /// </summary>
        /// <typeparam name="TNew">The type of new filter to append</typeparam>
        /// <param name="filter">The new filter instance to append</param>
        /// <returns>A continuation of this cascade builder API</returns>
        public TypedScriptFilterCascade<TType, TNew> Where<TNew>(TNew filter) where TNew : unmanaged, IScriptFilterBase =>
        new TypedScriptFilterCascade<TType, TNew>(allScripts, filter);
    }
}

