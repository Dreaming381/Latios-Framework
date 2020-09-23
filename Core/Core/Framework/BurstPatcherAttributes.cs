using System;

namespace Latios
{
    [AttributeUsage(AttributeTargets.Struct)]
    public class BurstPatcherAttribute : Attribute
    {
        public Type m_interfaceType;
        public BurstPatcherAttribute(Type interfaceType)
        {
            m_interfaceType = interfaceType;
        }
    }

    [AttributeUsage(AttributeTargets.Struct)]
    public class IgnoreBurstPatcherAttribute : Attribute
    {
    }
}

