using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Latios.Kinemation.Authoring
{
    /// <summary>
    /// Inherit this class and attach to a GameObject to disable normal Mesh Renderer or Skinned Mesh Renderer baking
    /// for possibly all descendants in the hierarchy. Each descendant's baker will query the ShouldOverride() method.
    /// </summary>
    public abstract class OverrideHierarchyRendererBase : MonoBehaviour
    {
        /// <summary>
        /// Return true if default baking should be disabled for this renderer. False otherwise.
        /// </summary>
        public abstract bool ShouldOverride(IBaker baker, Renderer renderer);
    }

    public static partial class RenderingBakingTools
    {
        static List<OverrideHierarchyRendererBase> s_overrideHCache = new List<OverrideHierarchyRendererBase>();
        static List<OverrideMeshRendererBase>      s_overrideMCache = new List<OverrideMeshRendererBase>();

        /// <summary>
        /// Test if the Renderer has an OverrideMeshRendererBase or OverrideHierarchyRendererBase that should apply to it
        /// and is not in the ignore list.
        /// </summary>
        /// <param name="baker">The current baker processing an authoring component</param>
        /// <param name="authoring">A renderer component that should be evaluated</param>
        /// <param name="overridesToIgnore">An ignore list of OverrideMeshRendererBase and OverrideHierarchyRendererBase (other types in the list do nothing).
        /// Use this to filter out overrides you already know about, such as one you are currently baking.</param>
        /// <returns>Returns true if a valid override was found</returns>
        public static bool IsOverridden(IBaker baker, Renderer authoring, Span<UnityObjectRef<MonoBehaviour> > overridesToIgnore = default)
        {
            if (overridesToIgnore.IsEmpty && baker.GetComponent<OverrideMeshRendererBase>(authoring) != null)
                return true;
            else if (!overridesToIgnore.IsEmpty)
            {
                s_overrideMCache.Clear();
                baker.GetComponents(authoring, s_overrideMCache);
                foreach (var o in s_overrideMCache)
                {
                    UnityObjectRef<MonoBehaviour> oref        = o;
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
                UnityObjectRef<MonoBehaviour> oref        = o;
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

