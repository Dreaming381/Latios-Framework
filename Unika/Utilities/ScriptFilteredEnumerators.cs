using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Unika
{
    public struct UntypedScriptFilteredEnumerator<TF0, TF1, TF2, TF3, TF4, TF5, TF6, TF7>
        where TF0 : unmanaged, IScriptFilter
        where TF1 : unmanaged, IScriptFilter
        where TF2 : unmanaged, IScriptFilter
        where TF3 : unmanaged, IScriptFilter
        where TF4 : unmanaged, IScriptFilter
        where TF5 : unmanaged, IScriptFilter
        where TF6 : unmanaged, IScriptFilter
        where TF7 : unmanaged, IScriptFilter
    {
        EntityScriptCollection.Enumerator enumerator;
        TF0                               f0;
        TF1                               f1;
        TF2                               f2;
        TF3                               f3;
        TF4                               f4;
        TF5                               f5;
        TF6                               f6;
        TF7                               f7;

        public UntypedScriptFilteredEnumerator(EntityScriptCollection allScripts, TF0 f0, TF1 f1, TF2 f2, TF3 f3, TF4 f4, TF5 f5, TF6 f6, TF7 f7)
        {
            enumerator = allScripts.GetEnumerator();
            this.f0    = f0;
            this.f1    = f1;
            this.f2    = f2;
            this.f3    = f3;
            this.f4    = f4;
            this.f5    = f5;
            this.f6    = f6;
            this.f7    = f7;
            if (!allScripts.isEmpty)
            {
                var  header    = allScripts.m_buffer.Reinterpret<UnikaScripts>()[0];
                bool prefilter = this.f0.PreFilter(header) && this.f1.PreFilter(header) && this.f2.PreFilter(header) && this.f3.PreFilter(header) &&
                                 this.f4.PreFilter(header) && this.f5.PreFilter(header) && this.f6.PreFilter(header) && this.f7.PreFilter(header);
                if (!prefilter)
                {
                    enumerator = default;
                }
            }
        }

        public Script Current => enumerator.Current;

        public bool MoveNext()
        {
            while (enumerator.MoveNext())
            {
                var  c      = enumerator.Current;
                bool filter = f0.Filter(c) && f1.Filter(c) && f2.Filter(c) && f3.Filter(c) && f4.Filter(c) && f5.Filter(c) && f6.Filter(c) &&
                              f7.Filter(c);
                if (filter)
                    return true;
            }
            return false;
        }
    }

    public struct TypedScriptFilteredEnumerator<TType, TF0, TF1, TF2, TF3, TF4, TF5, TF6, TF7>
        where TType : unmanaged, IScriptTypedExtensionsApi
        where TF0 : unmanaged, IScriptFilter
        where TF1 : unmanaged, IScriptFilter
        where TF2 : unmanaged, IScriptFilter
        where TF3 : unmanaged, IScriptFilter
        where TF4 : unmanaged, IScriptFilter
        where TF5 : unmanaged, IScriptFilter
        where TF6 : unmanaged, IScriptFilter
        where TF7 : unmanaged, IScriptFilter
    {
        TType                             currentCache;
        ScriptTypeInfoManager.IdAndMask   idAndMask;
        EntityScriptCollection.Enumerator enumerator;
        TF0                               f0;
        TF1                               f1;
        TF2                               f2;
        TF3                               f3;
        TF4                               f4;
        TF5                               f5;
        TF6                               f6;
        TF7                               f7;

        public TypedScriptFilteredEnumerator(TType defaultOfType, EntityScriptCollection allScripts, TF0 f0, TF1 f1, TF2 f2, TF3 f3, TF4 f4, TF5 f5, TF6 f6, TF7 f7)
        {
            currentCache = defaultOfType;
            idAndMask    = defaultOfType.GetIdAndMask().idAndMask;
            enumerator   = allScripts.GetEnumerator();
            this.f0      = f0;
            this.f1      = f1;
            this.f2      = f2;
            this.f3      = f3;
            this.f4      = f4;
            this.f5      = f5;
            this.f6      = f6;
            this.f7      = f7;
            if (!allScripts.isEmpty)
            {
                var  header    = allScripts.m_buffer.Reinterpret<UnikaScripts>()[0];
                bool prefilter = (idAndMask.bloomMask & header.header.bloomMask) == idAndMask.bloomMask;
                prefilter      = prefilter && this.f0.PreFilter(header) && this.f1.PreFilter(header) && this.f2.PreFilter(header) && this.f3.PreFilter(header) &&
                                 this.f4.PreFilter(header) && this.f5.PreFilter(header) && this.f6.PreFilter(header) && this.f7.PreFilter(header);
                if (!prefilter)
                {
                    enumerator = default;
                }
            }
        }

        public TType Current => currentCache;

        public bool MoveNext()
        {
            while (enumerator.MoveNext())
            {
                var  c      = enumerator.Current;
                bool filter = (idAndMask.bloomMask & c.m_headerRO.bloomMask) == idAndMask.bloomMask;
                filter      = filter && f0.Filter(c) && f1.Filter(c) && f2.Filter(c) && f3.Filter(c) && f4.Filter(c) && f5.Filter(c) && f6.Filter(c) &&
                              f7.Filter(c);
                if (filter && c.TryCast(out TType casted))
                {
                    currentCache = casted;
                    return true;
                }
            }
            return false;
        }
    }
}

