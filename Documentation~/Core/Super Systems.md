# Super Systems

A Super System is a subclass of `ComponentSystemGroup` which provides
specialized functionality. Super Systems power the explicit system ordering
mechanism as well as the custom system update criteria mechanism.

## Explicit System Ordering

When using the Explicit System Ordering workflow, you must specify your system
order in the `CreateSystems()` method of your `SuperSystem` subclass. To do
so, simply make calls to `GetOrCreateAndAddSystem<T>()` in the order you
want the systems to run.

```csharp
public class ExampleSuperSystem : SuperSystem
{
    protected override void CreateSystems()
    {
        GetOrCreateAndAddSystem<YourSubSystem1>();
        GetOrCreateAndAddSystem<YourSubSystem2>();
        GetOrCreateAndAddSystem<AnotherSuperSystem>();

        //Automatically reuses the existing TransformSystemGroup to prevent ChangeFilter fighting.
        GetOrCreateAndAddSystem<TransformSystemGroup>();

        //You can dynamically generate your systems here too!
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        foreach (var assembly in assemblies)
        {
            foreach (var type in assembly.GetTypes())
            {
                if (typeof(ICustomInterface).IsAssignableFrom(type))
                {
                    GetOrCreateAndAddSystem(typeof(YourGenericSystem<>).MakeGenericType(type));
                }
            }
        }

        //If you would like to sort your systems using attributes after explicitly creating them, you can call this here:
        SortSystemsUsingAttributes();
    }
}
```


## Hierarchical Update Culling

You can cull updates of `SubSystem`s or `SuperSystem`s by overriding
`ShouldUpdateSystem()`.

When a `SuperSystem` is iterating through the list of systems to update, if it
detects a system is a `SuperSystem` or `SubSystem`, it will call
`ShouldUpdateSystem()` on the system and set the system’s `Enabled` property
appropriately. Setting the `Enabled` property triggers proper invocation and
propagation of `OnStartRunning()` and `OnStopRunning()`.

If a `SuperSystem` is disabled, its `OnUpdate()` will not be called and it
will not iterate through its children systems, potentially saving crucial main
thread milliseconds.

In the following example, the first frame `ShouldUpdateSystem()` returns
false, the three children systems will immediately have `OnStopRunning()`
called on them, but will not have `Update()` called on them. In the following
frames where `ShouldUpdateSystem()` returns false, the children systems will
be left untouched. This may lead to some non-obvious behavior if you are relying
on `OnStopRunning()` for a system that is a child of multiple
`SuperSystem`s.

```csharp
public class BeastSuperSystem : SuperSystem
{
    EntityQuery m_query;
        
    protected override void CreateSystems()
    {
        GetOrCreateAndAddSystem<BeastHuntSystem>();
        GetOrCreateAndAddSystem<BeastEatSystem>();
        GetOrCreateAndAddSystem<BeastSleepSystem>();

        m_query = Fluent.WithAll<BeastTag>(true).Build();
    }

    public override bool ShouldUpdateSystem()
    {
        var scene = worldGlobalEntity.GetComponentData<CurrentScene>();
        return scene.current != "Beast Dungeon" || !m_query.IsEmptyIgnoreFilter;
    }
}
```


Note: You can use hierarchical update culling while also using the system
injection workflow. When doing so, simply do not call
`GetOrCreateAndAddSystem<T>()`.

## Root Super Systems

Unlike regular `SuperSystem`s, `RootSuperSystem`s are designed to be
injected into traditional `ComponentSystemGroup`s. They serve as entry-points
relative to Unity and perhaps third-party systems from which explicit system
ordering can take over.

If a `RootSuperSystem` uses a custom `ShouldUpdateSystem()` implementation,
how that information is relayed in the Editor may differ from `SuperSystem`.
