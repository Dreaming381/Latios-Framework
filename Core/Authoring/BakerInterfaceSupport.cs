using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Exposed;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.Authoring
{
    public static class BakerInterfaceSupport
    {
        /// <summary>
        /// Retrieves the component of Type T in the GameObject
        /// </summary>
        /// <typeparam name="T">The type of component to retrieve</typeparam>
        /// <returns>The component if a component matching the type is found, null otherwise</returns>
        /// <remarks>This will take a dependency on the component</remarks>
        public static T GetComponent<T>(this IBaker baker)
        {
            var gameObject = baker.GetAuthoringGameObjectWithoutDependency();
            return GetComponent<T>(baker, gameObject);
        }

        /// <summary>
        /// Retrieves the component of Type T in the GameObject
        /// </summary>
        /// <param name="component">The Object to get the component from</param>
        /// <typeparam name="T">The type of component to retrieve</typeparam>
        /// <returns>The component if a component matching the type is found, null otherwise</returns>
        /// <remarks>This will take a dependency on the component</remarks>
        public static T GetComponent<T>(this IBaker baker, Component component)
        {
            var gameObject = component.gameObject;
            return GetComponent<T>(baker, gameObject);
        }

        /// <summary>
        /// Retrieves the component of Type T in the GameObject
        /// </summary>
        /// <param name="gameObject">The GameObject to get the component from</param>
        /// <typeparam name="T">The type of component to retrieve</typeparam>
        /// <returns>The component if a component matching the type is found, null otherwise</returns>
        /// <remarks>This will take a dependency on the component</remarks>
        public static T GetComponent<T>(this IBaker baker, GameObject gameObject)
        {
            foreach (var type in InterfaceState<T>.implementingComponents)
                type.GetComponent(baker, gameObject);
            gameObject.TryGetComponent<T>(out var result);
            return result;
        }

        /// <summary>
        /// Returns all components of Type T in the GameObject
        /// </summary>
        /// <param name="components">The components of Type T</param>
        /// <typeparam name="T">The type of components to retrieve</typeparam>
        /// <remarks>This will take a dependency on the components</remarks>
        public static void GetComponents<T>(this IBaker baker, List<T> components)
        {
            var gameObject = baker.GetAuthoringGameObjectWithoutDependency();
            GetComponents(baker, gameObject, components);
        }

        /// <summary>
        /// Returns all components of Type T in the GameObject
        /// </summary>
        /// <param name="component">The Object to get the components from</param>
        /// <param name="components">The components of Type T</param>
        /// <typeparam name="T">The type of components to retrieve</typeparam>
        /// <remarks>This will take a dependency on the components</remarks>
        public static void GetComponents<T>(this IBaker baker, Component component, List<T> components)
        {
            var gameObject = component.gameObject;
            GetComponents(baker, gameObject, components);
        }

        /// <summary>
        /// Returns all components of Type T in the GameObject
        /// </summary>
        /// <param name="gameObject">The GameObject to get the components from</param>
        /// <param name="components">The components of Type T</param>
        /// <typeparam name="T">The type of components to retrieve</typeparam>
        /// <remarks>This will take a dependency on the components</remarks>
        public static void GetComponents<T>(this IBaker baker, GameObject gameObject, List<T> components)
        {
            foreach (var type in InterfaceState<T>.implementingComponents)
                type.GetComponents(baker, gameObject);
            gameObject.GetComponents(components);
        }

        /// <summary>
        /// Retrieves the component of Type T in the GameObject or any of its parents
        /// </summary>
        /// <typeparam name="T">The type of Component to retrieve</typeparam>
        /// <returns>Returns a component if a component matching the type is found, null otherwise</returns>
        /// <remarks>This will take a dependency on the component</remarks>
        public static T GetComponentInParent<T>(this IBaker baker)
        {
            var gameObject = baker.GetAuthoringGameObjectWithoutDependency();
            return GetComponentInParent<T>(baker, gameObject);
        }

        /// <summary>
        /// Retrieves the component of Type T in the GameObject or any of its parents
        /// </summary>
        /// <param name="component">The Object to get the component from</param>
        /// <typeparam name="T">The type of component to retrieve</typeparam>
        /// <returns>The component if a component matching the type is found, null otherwise</returns>
        /// <remarks>This will take a dependency on the component</remarks>
        public static T GetComponentInParent<T>(this IBaker baker, Component component)
        {
            var gameObject = component.gameObject;
            return GetComponentInParent<T>(baker, gameObject);
        }

        /// <summary>
        /// Retrieves the component of Type T in the GameObject or any of its parents
        /// </summary>
        /// <param name="gameObject">The GameObject to get the component from</param>
        /// <typeparam name="T">The type of component to retrieve</typeparam>
        /// <returns>The component if a component matching the type is found, null otherwise</returns>
        /// <remarks>This will take a dependency on the component</remarks>
        public static T GetComponentInParent<T>(this IBaker baker, GameObject gameObject)
        {
            foreach (var type in InterfaceState<T>.implementingComponents)
                type.GetComponents(baker, gameObject);
            return gameObject.GetComponentInParent<T>(true);
        }

        /// <summary>
        /// Returns all components of Type T in the GameObject or any of its parents. Works recursively.
        /// </summary>
        /// <param name="components">The components of Type T</param>
        /// <typeparam name="T">The type of components to retrieve</typeparam>
        /// <remarks>This will take a dependency on the components</remarks>
        public static void GetComponentsInParent<T>(this IBaker baker, List<T> components)
        {
            var gameObject = baker.GetAuthoringGameObjectWithoutDependency();
            GetComponentsInParent(baker, gameObject, components);
        }

        /// <summary>
        /// Returns all components of Type T in the GameObject or any of its parents. Works recursively.
        /// </summary>
        /// <param name="component">The Object to get the components from</param>
        /// <param name="components">The components of Type T</param>
        /// <typeparam name="T">The type of components to retrieve</typeparam>
        /// <remarks>This will take a dependency on the components</remarks>
        public static void GetComponentsInParent<T>(this IBaker baker, Component component, List<T> components)
        {
            var gameObject = component.gameObject;
            GetComponentsInParent(baker, gameObject, components);
        }

        /// <summary>
        /// Returns all components of Type T in the GameObject or any of its parents. Works recursively.
        /// </summary>
        /// <param name="gameObject">The GameObject to get the components from</param>
        /// <param name="components">The components of Type T</param>
        /// <typeparam name="T">The type of components to retrieve</typeparam>
        /// <remarks>This will take a dependency on the components</remarks>
        public static void GetComponentsInParent<T>(this IBaker baker, GameObject gameObject, List<T> components)
        {
            foreach (var type in InterfaceState<T>.implementingComponents)
                type.GetComponents(baker, gameObject);
            gameObject.GetComponentsInParent(true, components);
        }

        /// <summary>
        /// Returns the component of Type T in the GameObject or any of its children using depth first search
        /// </summary>
        /// <typeparam name="T">The type of component to retrieve</typeparam>
        /// <returns>The component if a component matching the type is found, null otherwise</returns>
        /// <remarks>This will take a dependency on the component</remarks>
        public static T GetComponentInChildren<T>(this IBaker baker)
        {
            var gameObject = baker.GetAuthoringGameObjectWithoutDependency();
            return GetComponentInChildren<T>(baker, gameObject);
        }

        /// <summary>
        /// Returns the component of Type T in the GameObject or any of its children using depth first search
        /// </summary>
        /// <param name="component">The Object to get the component from</param>
        /// <typeparam name="T">The type of component to retrieve</typeparam>
        /// <returns>The component if a component matching the type is found, null otherwise</returns>
        /// <remarks>This will take a dependency on the component</remarks>
        public static T GetComponentInChildren<T>(this IBaker baker, Component component)
        {
            var gameObject = component.gameObject;
            return GetComponentInChildren<T>(baker, gameObject);
        }

        /// <summary>
        /// Returns the component of Type T in the GameObject or any of its children using depth first search
        /// </summary>
        /// <param name="gameObject">The GameObject to get the component from</param>
        /// <typeparam name="T">The type of component to retrieve</typeparam>
        /// <returns>The component if a component matching the type is found, null otherwise</returns>
        /// <remarks>This will take a dependency on the component</remarks>
        public static T GetComponentInChildren<T>(this IBaker baker, GameObject gameObject)
        {
            foreach (var type in InterfaceState<T>.implementingComponents)
                type.GetComponents(baker, gameObject);
            return gameObject.GetComponentInChildren<T>(true);
        }

        /// <summary>
        /// Returns all components of Type type in the GameObject or any of its children using depth first search. Works recursively.
        /// </summary>
        /// <param name="components">The components of Type T</param>
        /// <typeparam name="T">The type of component to retrieve</typeparam>
        public static void GetComponentsInChildren<T>(this IBaker baker, List<T> components)
        {
            var gameObject = baker.GetAuthoringGameObjectWithoutDependency();
            GetComponentsInChildren(baker, gameObject, components);
        }

        /// <summary>
        /// Returns all components of Type type in the GameObject or any of its children using depth first search. Works recursively.
        /// </summary>
        /// <param name="refComponent">The Object to get the components from</param>
        /// <param name="components">The components of Type T</param>
        /// <typeparam name="T">The type of component to retrieve</typeparam>
        /// <remarks>This will take a dependency on the components</remarks>
        public static void GetComponentsInChildren<T>(this IBaker baker, Component refComponent, List<T> components)
        {
            var gameObject = refComponent.gameObject;
            GetComponentsInChildren(baker, gameObject, components);
        }

        /// <summary>
        /// Returns all components of Type type in the GameObject or any of its children using depth first search. Works recursively.
        /// </summary>
        /// <param name="gameObject">The GameObject to get the components from</param>
        /// <param name="components">The components of Type T</param>
        /// <typeparam name="T">The type of component to retrieve</typeparam>
        /// <remarks>This will take a dependency on the components</remarks>
        public static void GetComponentsInChildren<T>(this IBaker baker, GameObject gameObject, List<T> components)
        {
            foreach (var type in InterfaceState<T>.implementingComponents)
                type.GetComponents(baker, gameObject);
            gameObject.GetComponentsInChildren(true, components);
        }

        static class InterfaceState<TInterface>
        {
            public static List<ComponentStateBase> implementingComponents;

            static InterfaceState()
            {
#if UNITY_EDITOR
                implementingComponents = new List<ComponentStateBase>();
                var derivingTypes             = UnityEditor.TypeCache.GetTypesDerivedFrom<TInterface>();
                var componentType             = typeof(UnityEngine.Component);
                var componentStateGenericType = typeof(ComponentState<>);
                var interfaceType             = typeof(TInterface);
                foreach (var type in derivingTypes)
                {
                    if (type.IsAbstract)
                        continue;
                    if (type.ContainsGenericParameters)
                        continue;
                    if (componentType.IsAssignableFrom(type))
                    {
                        var concrete = componentStateGenericType.MakeGenericType(interfaceType, type);
                        implementingComponents.Add(System.Activator.CreateInstance(concrete) as ComponentStateBase);
                    }
                }
#endif
            }

            public abstract class ComponentStateBase
            {
                public abstract void GetComponent(IBaker baker, GameObject gameObject);
                public abstract void GetComponentInParent(IBaker baker, GameObject gameObject);
                public abstract void GetComponentInChildren(IBaker baker, GameObject gameObject);
                public abstract void GetComponents(IBaker baker, GameObject gameObject);
                public abstract void GetComponentsInParent(IBaker baker, GameObject gameObject);
                public abstract void GetComponentsInChildren(IBaker baker, GameObject gameObject);
            }

            class ComponentState<TComponent> : ComponentStateBase where TComponent : Component
            {
                List<TComponent> cache = new List<TComponent>();

                public override void GetComponent(IBaker baker, GameObject gameObject)
                {
                    baker.GetComponent<TComponent>(gameObject);
                }

                public override void GetComponentInChildren(IBaker baker, GameObject gameObject)
                {
                    baker.GetComponentInChildren<TComponent>(gameObject);
                }

                public override void GetComponentInParent(IBaker baker, GameObject gameObject)
                {
                    baker.GetComponentInParent<TComponent>(gameObject);
                }

                public override void GetComponents(IBaker baker, GameObject gameObject)
                {
                    baker.GetComponents(gameObject, cache);
                }

                public override void GetComponentsInChildren(IBaker baker, GameObject gameObject)
                {
                    baker.GetComponentsInChildren(gameObject, cache);
                }

                public override void GetComponentsInParent(IBaker baker, GameObject gameObject)
                {
                    baker.GetComponentsInParent(gameObject, cache);
                }
            }
        }
    }
}

