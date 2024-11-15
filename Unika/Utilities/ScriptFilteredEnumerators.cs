using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios.Unika
{
    public struct UntypedScriptFilteredEnumerator<TF0, TF1, TF2, TF3, TF4, TF5, TF6, TF7>
        where TF0 : unmanaged, IScriptFilterBase
        where TF1 : unmanaged, IScriptFilterBase
        where TF2 : unmanaged, IScriptFilterBase
        where TF3 : unmanaged, IScriptFilterBase
        where TF4 : unmanaged, IScriptFilterBase
        where TF5 : unmanaged, IScriptFilterBase
        where TF6 : unmanaged, IScriptFilterBase
        where TF7 : unmanaged, IScriptFilterBase
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
                bool prefilter = this.f0.PreFilterBase(header) && this.f1.PreFilterBase(header) && this.f2.PreFilterBase(header) && this.f3.PreFilterBase(header) &&
                                 this.f4.PreFilterBase(header) && this.f5.PreFilterBase(header) && this.f6.PreFilterBase(header) && this.f7.PreFilterBase(header);
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
                bool filter = f0.FilterBase(c) && f1.FilterBase(c) && f2.FilterBase(c) && f3.FilterBase(c) && f4.FilterBase(c) && f5.FilterBase(c) && f6.FilterBase(c) &&
                              f7.FilterBase(c);
                if (filter)
                    return true;
            }
            return false;
        }
    }

    public struct TypedScriptFilteredEnumerator<TType, TF0, TF1, TF2, TF3, TF4, TF5, TF6, TF7>
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
                prefilter      = prefilter && this.f0.PreFilterBase(header) && this.f1.PreFilterBase(header) && this.f2.PreFilterBase(header) && this.f3.PreFilterBase(header) &&
                                 this.f4.PreFilterBase(header) && this.f5.PreFilterBase(header) && this.f6.PreFilterBase(header) && this.f7.PreFilterBase(header);
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
                filter      = filter && f0.FilterBase(c) && f1.FilterBase(c) && f2.FilterBase(c) && f3.FilterBase(c) && f4.FilterBase(c) && f5.FilterBase(c) && f6.FilterBase(c) &&
                              f7.FilterBase(c);
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

