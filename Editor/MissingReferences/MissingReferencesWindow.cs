﻿using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityObject = UnityEngine.Object;

namespace Unity.Labs.SuperScience
{
    /// <summary>
    /// Scans the project for serialized references to missing (deleted) assets and displays the results in an EditorWindow
    /// </summary>
    abstract class MissingReferencesWindow : EditorWindow
    {
        protected class GameObjectContainer
        {
            class ComponentContainer
            {
                readonly Component m_Component;
                public readonly List<SerializedProperty> PropertiesWithMissingReferences = new List<SerializedProperty>();

                public ComponentContainer(Component component, MissingReferencesWindow window)
                {
                    m_Component = component;
                    window.CheckForMissingRefs(component, PropertiesWithMissingReferences);
                }

                public void Draw(MissingReferencesWindow window)
                {
                    EditorGUILayout.ObjectField(m_Component, typeof(Component), false);
                    using (new EditorGUI.IndentLevelScope())
                    {
                        if (m_Component == null)
                        {
                            EditorGUILayout.LabelField("<color=red>Missing Script!</color>", window.m_MissingScriptStyle);
                            return;
                        }

                        foreach (var property in PropertiesWithMissingReferences)
                        {
                            switch (property.propertyType)
                            {
                                case SerializedPropertyType.Generic:
                                    EditorGUILayout.LabelField(string.Format("Missing Method: {0}", property.propertyPath));
                                    break;
                                case SerializedPropertyType.ObjectReference:
                                    EditorGUILayout.PropertyField(property, new GUIContent(property.propertyPath));
                                    break;
                            }
                        }
                    }
                }
            }

            readonly GameObject m_GameObject;
            readonly List<GameObjectContainer> m_Children = new List<GameObjectContainer>();
            readonly List<ComponentContainer> m_Components = new List<ComponentContainer>();
            bool m_Visible;
            bool m_ShowComponents;
            bool m_ShowChildren;

            public int Count { get; private set; }
            public GameObject GameObject { get { return m_GameObject; } }

            public GameObjectContainer() { }
            internal GameObjectContainer(GameObject gameObject, MissingReferencesWindow window)
            {
                m_GameObject = gameObject;
                foreach (var component in gameObject.GetComponents<Component>())
                {
                    var container = new ComponentContainer(component, window);
                    if (component == null)
                    {
                        m_Components.Add(container);
                        Count++;
                        continue;
                    }

                    var count = container.PropertiesWithMissingReferences.Count;
                    if (count > 0)
                    {
                        m_Components.Add(container);
                        Count += count;
                    }
                }

                foreach (Transform child in gameObject.transform)
                {
                    Add(window, child.gameObject);
                }
            }

            public void Clear()
            {
                m_Children.Clear();
                m_Components.Clear();
                Count = 0;
            }

            public void Add(MissingReferencesWindow window, GameObject gameObject)
            {
                var container = new GameObjectContainer(gameObject, window);
                Count += container.Count;

                if (container.Count > 0)
                    m_Children.Add(container);
            }

            public void Draw(MissingReferencesWindow window, string name)
            {
                var wasVisible = m_Visible;
                m_Visible = EditorGUILayout.Foldout(m_Visible, string.Format("{0}: {1}", name, Count));
                if (m_Visible != wasVisible && Event.current.alt)
                    SetVisibleRecursively(m_Visible);

                if (!m_Visible)
                    return;

                using (new EditorGUI.IndentLevelScope())
                {
                    if (m_GameObject == null)
                    {
                        foreach (var child in m_Children)
                        {
                            // Check for null in case  of destroyed object
                            if (child.GameObject)
                                child.Draw(window, child.m_GameObject.name);
                        }
                    }
                    else
                    {
                        if (m_Components.Count > 0)
                        {
                            EditorGUILayout.ObjectField(m_GameObject, typeof(GameObject), true);
                            m_ShowComponents = EditorGUILayout.Foldout(m_ShowComponents, "Components");
                            if (m_ShowComponents)
                            {
                                using (new EditorGUI.IndentLevelScope())
                                {
                                    foreach (var component in m_Components)
                                    {
                                        component.Draw(window);
                                    }
                                }
                            }

                            if (m_Children.Count > 0)
                            {
                                m_ShowChildren = EditorGUILayout.Foldout(m_ShowChildren, "Children");
                                if (m_ShowChildren)
                                {
                                    using (new EditorGUI.IndentLevelScope())
                                    {
                                        foreach (var child in m_Children)
                                        {
                                            // Check for null in case  of destroyed object
                                            if (child.m_GameObject)
                                                child.Draw(window, child.m_GameObject.name);
                                        }
                                    }
                                }
                            }
                        }
                        else if (m_Children.Count > 0)
                        {
                            foreach (var child in m_Children)
                            {
                                // Check for null in case  of destroyed object
                                if (child.m_GameObject)
                                    child.Draw(window, child.m_GameObject.name);
                            }
                        }
                    }
                }
            }

            public void SetVisibleRecursively(bool visible)
            {
                m_Visible = visible;
                m_ShowComponents = visible;
                m_ShowChildren = visible;
                foreach (var child in m_Children)
                {
                    child.SetVisibleRecursively(visible);
                }
            }
        }

        const float k_LabelWidthRatio = 0.5f;
        const string k_PersistentCallsSearchString = "m_PersistentCalls.m_Calls.Array.data[";
        const string k_TargetPropertyName = "m_Target";
        const string k_MethodNamePropertyName = "m_MethodName";

        readonly Dictionary<UnityObject, SerializedObject> m_SerializedObjects = new Dictionary<UnityObject, SerializedObject>();

        GUIStyle m_MissingScriptStyle;
        bool m_FindMissingMethods = true;

        void OnEnable()
        {
            m_MissingScriptStyle = new GUIStyle { richText = true };
            EditorSceneManager.activeSceneChangedInEditMode += OnActiveSceneChangedInEditMode;
        }

        void OnActiveSceneChangedInEditMode(Scene oldScene, Scene newScene) { Clear(); }

        void OnDisable()
        {
            EditorSceneManager.activeSceneChangedInEditMode -= OnActiveSceneChangedInEditMode;
        }

        protected abstract void Clear();

        /// <summary>
        /// Load all assets in the AssetDatabase and check them for missing serialized references
        /// </summary>
        protected virtual void Scan()
        {
            m_SerializedObjects.Clear();
        }

        protected virtual void OnGUI()
        {
            EditorGUIUtility.labelWidth = position.width * k_LabelWidthRatio;
            m_FindMissingMethods = EditorGUILayout.Toggle("Find Missing Methods", m_FindMissingMethods);
            if (GUILayout.Button("Refresh"))
                Scan();
        }

        /// <summary>
        /// Check a UnityObject for missing serialized references
        /// </summary>
        /// <param name="obj">The UnityObject to be scanned</param>
        /// <param name="properties">A list to which properties with missing references will be added</param>
        /// <returns>True if the object has any missing references</returns>
        public void CheckForMissingRefs(UnityObject obj, List<SerializedProperty> properties)
        {
            if (obj == null)
                return;

            var so = GetSerializedObjectForUnityObject(obj);

            var property = so.GetIterator();
            while (property.NextVisible(true))
            {
                if (CheckForMissingRefs(so, property))
                    properties.Add(property.Copy());
            }
        }

        bool CheckForMissingRefs(SerializedObject so, SerializedProperty property)
        {
            var propertyPath = property.propertyPath;
            switch (property.propertyType)
            {
                case SerializedPropertyType.Generic:
                    if (!m_FindMissingMethods)
                        return false;

                    if (propertyPath.Contains(k_PersistentCallsSearchString))
                    {
                        var targetProperty = property.FindPropertyRelative(k_TargetPropertyName);
                        var methodProperty = property.FindPropertyRelative(k_MethodNamePropertyName);

                        if (targetProperty != null && methodProperty != null)
                        {
                            if (targetProperty.objectReferenceValue == null)
                                return false;

                            var type = targetProperty.objectReferenceValue.GetType();
                            try
                            {
                                if (!type.GetMethods().Any(info => info.Name == methodProperty.stringValue))
                                    return true;
                            }
                            catch (Exception e)
                            {
                                Debug.LogException(e);
                                return true;
                            }
                        }
                    }
                    break;
                case SerializedPropertyType.ObjectReference:
                    // Some references may be null, which is to be expected--not every field is set
                    // Valid asset references will have a non-null objectReferenceValue
                    // Valid asset references will have some non-zero objectReferenceInstanceIDValue value
                    // References to missing assets will have a null objectReferenceValue, but will retain
                    // their non-zero objectReferenceInstanceIDValue
                    if (property.objectReferenceValue == null && property.objectReferenceInstanceIDValue != 0)
                        return true;

                    break;
            }

            return false;
        }

        /// <summary>
        /// For a given UnityObject, get the cached SerializedObject, or create one and cache it
        /// </summary>
        /// <param name="obj">The UnityObject</param>
        /// <returns>A cached SerializedObject wrapper for the given UnityObject</returns>
        SerializedObject GetSerializedObjectForUnityObject(UnityObject obj)
        {
            SerializedObject so;
            if (!m_SerializedObjects.TryGetValue(obj, out so))
            {
                so = new SerializedObject(obj);
                m_SerializedObjects[obj] = so;
            }

            return so;
        }
    }
}