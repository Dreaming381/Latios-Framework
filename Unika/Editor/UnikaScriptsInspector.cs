using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Entities.Editor;
using Unity.Entities.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Latios.Unika.Editor
{
    /// <summary>
    /// Draws the UnikaScripts buffer in the Entity Inspector's Components tab as one foldout per script, instead of the raw headers.
    /// Unity.Entities.UI discovers this through TypeCache, and accesses its internals via the InternalsVisibleTo
    /// grants in Editor/EditorInternals and Editor/UIInternals.
    /// </summary>
    /// <remarks>
    /// Scripts move within the buffer whenever any script is added or removed, so nothing here holds onto a resolved script.
    /// Every edit resolves its script again by instance ID, and Update() refreshes the displayed values from the live buffer.
    /// </remarks>
    class UnikaScriptsInspector : PropertyInspector<DynamicBufferContainer<UnikaScripts> >
    {
        static readonly Dictionary<(Entity, int), bool> s_foldoutExpanded = new Dictionary<(Entity, int), bool>();

        EntityInspectorContext    m_context;
        VisualElement             m_root;
        readonly List<ScriptItem> m_items = new List<ScriptItem>();

        public override VisualElement Build()
        {
            m_context = GetContext<EntityInspectorContext>();
            m_root    = new VisualElement();
            Rebuild();
            return m_root;
        }

        public override void Update()
        {
            if (!TryGetScripts(true, out var scripts))
                return;

            if (!LayoutMatches(scripts))
            {
                Rebuild();
                return;
            }

            int i = 0;
            foreach (var view in scripts.managedView)
                m_items[i++].Refresh(view, m_context.IsReadOnly);
        }

        bool TryGetScripts(bool readOnly, out EntityScriptCollection scripts)
        {
            scripts = default;
            if (m_context == null || !m_context.TargetExists() || !m_context.EntityManager.HasBuffer<UnikaScripts>(m_context.Entity))
                return false;
            scripts = m_context.EntityManager.GetBuffer<UnikaScripts>(m_context.Entity, readOnly).AllScripts(m_context.Entity);
            return true;
        }

        bool LayoutMatches(EntityScriptCollection scripts)
        {
            if (scripts.length != m_items.Count)
                return false;
            int i = 0;
            foreach (var view in scripts.managedView)
            {
                if (view.instanceId != m_items[i].instanceId || view.scriptType != m_items[i].scriptType)
                    return false;
                i++;
            }
            return true;
        }

        void Rebuild()
        {
            m_root.Clear();
            m_items.Clear();
            if (!TryGetScripts(true, out var scripts))
                return;

            if (scripts.length == 0)
            {
                m_root.Add(new Label("No scripts"));
                return;
            }

            foreach (var view in scripts.managedView)
            {
                var item = new ScriptItem(this, view);
                m_items.Add(item);
                m_root.Add(item.foldout);
            }
        }

        void Write(int instanceId, Action<ManagedScriptView> write)
        {
            if (m_context.IsReadOnly || !TryGetScripts(false, out var scripts))
                return;
            foreach (var view in scripts.managedView)
            {
                if (view.instanceId == instanceId)
                {
                    write(view);
                    return;
                }
            }
        }

        class ScriptItem
        {
            public readonly int     instanceId;
            public readonly Type    scriptType;
            public readonly Foldout foldout;

            readonly Toggle          m_flagA;
            readonly Toggle          m_flagB;
            readonly IntegerField    m_userByte;
            readonly PropertyElement m_content;

            public ScriptItem(UnikaScriptsInspector inspector, ManagedScriptView view)
            {
                instanceId = view.instanceId;
                scriptType = view.scriptType;
                var id     = instanceId;
                var key    = (inspector.m_context.Entity, instanceId);

                foldout = new Foldout
                {
                    text  = $"{scriptType.Name} ID:{instanceId}",
                    value = s_foldoutExpanded.TryGetValue(key, out var expanded) && expanded,
                };
                foldout.RegisterValueChangedCallback(evt =>
                {
                    if (evt.target == foldout)
                        s_foldoutExpanded[key] = evt.newValue;
                });
                Unity.Entities.Editor.Resources.Templates.Inspector.ComponentHeader.AddStyles(foldout);
                foldout.AddToClassList(Unity.Entities.Editor.UssClasses.Inspector.Component.Header);
                foldout.contentContainer.AddToClassList(Unity.Entities.Editor.UssClasses.Inspector.Component.Container);

                const float kSpacing = 20f;
                var         row      = new VisualElement { style = { flexDirection = FlexDirection.Row } };
                m_flagA                = new Toggle("Flag A") { style = { marginRight = kSpacing } };
                m_flagB                = new Toggle("Flag B") { style = { marginRight = kSpacing } };
                // Flex-grows so that the field doesn't resize as digits are typed.
                m_userByte = new IntegerField("User Byte") { style = { flexGrow = 1, flexBasis = 0 } };
                m_flagA.RegisterValueChangedCallback(evt => inspector.Write(id, v => v.userFlagA = evt.newValue));
                m_flagB.RegisterValueChangedCallback(evt => inspector.Write(id, v => v.userFlagB = evt.newValue));
                m_userByte.RegisterValueChangedCallback(evt =>
                {
                    var clamped = Mathf.Clamp(evt.newValue, 0, 255);
                    if (clamped != evt.newValue)
                        m_userByte.SetValueWithoutNotify(clamped);
                    inspector.Write(id, v => v.userByte = (byte)clamped);
                });
                row.Add(m_flagA);
                row.Add(m_flagB);
                row.Add(m_userByte);
                foldout.Add(row);

                m_content = new PropertyElement();
                if (EditorApplication.isPlaying)
                    m_content.userData = m_content;
                m_content.AddContext(inspector.m_context);
                m_content.SetTarget(view.contents);
                m_content.OnChanged += (element, _) => inspector.Write(id, v => v.contents = element.GetTarget<object>());
                // Scoped to the content so that the compact flags row keeps its narrow labels.
                m_content.RegisterCallback<GeometryChangedEvent, VisualElement>((_, elem) => StylingUtility.AlignInspectorLabelWidth(elem), m_content);
                foldout.Add(m_content);

                Refresh(view, inspector.m_context.IsReadOnly);
            }

            public void Refresh(ManagedScriptView view, bool readOnly)
            {
                foldout.contentContainer.SetEnabled(!readOnly);
                if (HasFocus(foldout))
                    return;
                m_flagA.SetValueWithoutNotify(view.userFlagA);
                m_flagB.SetValueWithoutNotify(view.userFlagB);
                m_userByte.SetValueWithoutNotify(view.userByte);
                m_content.SetTarget(view.contents);
            }

            static bool HasFocus(VisualElement element) => element.panel?.focusController?.focusedElement is VisualElement focused && element.Contains(focused);
        }
    }
}
