using System;
using System.Collections.Generic;
using Latios.Authoring;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.Kinemation.Authoring
{
    /// <summary>
    /// Implement this interface on a MonoBehaviour and attach to a GameObject to disable normal Mesh Renderer or Skinned Mesh Renderer baking
    /// for possibly all descendants in the hierarchy. Each descendant's baker will query the ShouldOverride() method.
    /// </summary>
    public interface IOverrideHierarchyRenderer
    {
        /// <summary>
        /// Return true if default baking should be disabled for this renderer. False otherwise.
        /// </summary>
        public bool ShouldOverride(IBaker baker, Renderer renderer);
    }

    public static partial class RenderingBakingTools
    {
        static List<IOverrideHierarchyRenderer> s_overrideHCache = new List<IOverrideHierarchyRenderer>();
        static List<IOverrideMeshRenderer>      s_overrideMCache = new List<IOverrideMeshRenderer>();

        /// <summary>
        /// Test if the Renderer has an IOverrideMeshRenderer or IOverrideHierarchyRenderer that should apply to it
        /// and is not in the ignore list.
        /// </summary>
        /// <param name="baker">The current baker processing an authoring component</param>
        /// <param name="authoring">A renderer component that should be evaluated</param>
        /// <param name="overridesToIgnore">An ignore list of OverrideMeshRendererBase and OverrideHierarchyRendererBase (other types in the list do nothing).
        /// Use this to filter out overrides you already know about, such as one you are currently baking.</param>
        /// <returns>Returns true if a valid override was found</returns>
        public static bool IsOverridden(IBaker baker, Renderer authoring, Span<UnityObjectRef<MonoBehaviour> > overridesToIgnore = default)
        {
            if (overridesToIgnore.IsEmpty && baker.GetComponent<IOverrideMeshRenderer>(authoring) != null)
                return true;
            else if (!overridesToIgnore.IsEmpty)
            {
                s_overrideMCache.Clear();
                baker.GetComponents(authoring, s_overrideMCache);
                foreach (var o in s_overrideMCache)
                {
                    UnityObjectRef<MonoBehaviour> oref        = o as MonoBehaviour;
                    bool                          foundIgnore = false;
                    foreach (var i in overridesToIgnore)
                    {
                        if (oref == i)
                        {
                            foundIgnore = true;
                            break;
                        }
                    }
                    if (!foundIgnore)
                        return true;
                }
            }

            s_overrideHCache.Clear();
            baker.GetComponentsInParent(s_overrideHCache);
            foreach (var o in s_overrideHCache)
            {
                UnityObjectRef<MonoBehaviour> oref        = o as MonoBehaviour;
                bool                          foundIgnore = false;
                foreach (var i in overridesToIgnore)
                {
                    if (oref == i)
                    {
                        foundIgnore = true;
                        break;
                    }
                }

                if (!foundIgnore && o.ShouldOverride(baker, authoring))
                    return true;
            }

            return false;
        }
    }
}

