using Latios.Transforms.Abstract;
using Unity.Entities;
using Unity.Entities.Editor;
using Unity.Rendering;
using UnityEditor;
using UnityEngine;

namespace Latios.Kinemation.Editor
{
    static class RuntimeEntitySceneViewFocus
    {
        private static Entity _lockedEntity;

        [UnityEditor.Callbacks.DidReloadScripts]
        private static void ScriptsHasBeenReloaded()
        {
            SceneView.duringSceneGui   += DuringSceneGui;
            Selection.selectionChanged += SelectionChanged;
        }

        private static void DuringSceneGui(SceneView sceneView)
        {
            var ev = Event.current;

            var proxy = GetCurrentSelectionProxy();

            if (!proxy) return;
            if (proxy.World is not LatiosWorld) return;

            if (ev.type == EventType.ExecuteCommand) HandleExecuteCommand(proxy, sceneView);
            if (ev.type == EventType.Repaint && _lockedEntity != default)
                LookAtEntity(proxy, sceneView);
        }

        private static void SelectionChanged()
        {
            if (_lockedEntity == default) return;

            if (GetCurrentSelectionProxy()?.Entity != _lockedEntity)
                _lockedEntity = default;
        }

        private static EntitySelectionProxy GetCurrentSelectionProxy()
        {
            if (Selection.objects.Length != 1) return null;

            // If a Runtime entity is selected, objects[0] will be EntitySelectionProxy. If a baked entity is selected, objects[0] will be a GO
            // and activeContext will contain the EntitySelectionProxy
            if (Selection.objects[0] is EntitySelectionProxy objProxy)
            {
                return objProxy;
            }

            if (Selection.activeContext is EntitySelectionProxy ctxProxy)
            {
                return ctxProxy;
            }

            return null;
        }

        private static void HandleExecuteCommand(EntitySelectionProxy selectionProxy, SceneView sceneView)
        {
            var ev = Event.current;

            var withLock = ev.commandName == "FrameSelectedWithLock";

            if (ev.commandName != "FrameSelected" && !withLock) return;

            LookAtEntity(selectionProxy, sceneView);

            if (withLock)
            {
                _lockedEntity = selectionProxy.Entity;
            }

            // consume the event
            ev.Use();
            ev.commandName = "";
        }

        private static void LookAtEntity(EntitySelectionProxy selectionProxy, SceneView sceneView)
        {
            var entity = selectionProxy.Entity;
            var em     = selectionProxy.World.EntityManager;

            if (!em.HasComponent(entity, QueryExtensions.GetAbstractWorldTransformROComponentType())) return;

            Bounds bounds;
            if (em.HasComponent<WorldRenderBounds>(entity))
            {
                var aabb = em.GetComponentData<WorldRenderBounds>(entity).Value;
                bounds = new Bounds(aabb.Center, aabb.Size);
            }
            else
            {
                var t = em.GetAspect<WorldTransformReadOnlyAspect>(entity);
                bounds = new Bounds(t.position, Vector3.one);
            }

            sceneView.LookAt(bounds.center, sceneView.rotation, sceneView.size);
            sceneView.FixNegativeSize();
        }
    }
}