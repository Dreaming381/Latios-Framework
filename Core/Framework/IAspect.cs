using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Latios
{
    public interface IAspect
    {
        void Initialize(EntityManager entityManager, Entity entity);
    }

    public static class IAspectExtensions
    {
        public static T GetAspect<T>(this EntityManager entityManager, Entity entity) where T : IAspect
        {
            T t = default;
            t.Initialize(entityManager, entity);
            return t;
        }
    }
}

