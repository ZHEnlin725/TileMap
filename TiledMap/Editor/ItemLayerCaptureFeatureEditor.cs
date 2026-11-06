using UnityEditor;
using UnityEngine;

namespace Demo.Water.EditorTools
{
    [CustomEditor(typeof(ItemLayerCaptureFeature))]
    public class ItemLayerCaptureFeatureEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var feature = (ItemLayerCaptureFeature)target;
            var settingsProperty = serializedObject.FindProperty("settings");

            var layers = SortingLayer.layers;
            var names = new string[layers.Length + 1];
            names[0] = "<All Sorting Layers>";
            int selectedIndex = 0;
            for (int i = 0; i < layers.Length; ++i)
            {
                names[i + 1] = layers[i].name;
                if (layers[i].name == feature.CurrentSettings.sortingLayerName)
                {
                    selectedIndex = i + 1;
                }
            }

            EditorGUI.BeginChangeCheck();
            int next = EditorGUILayout.Popup(
                new GUIContent("Sorting Layer", "Restrict the capture to one Sorting Layer, or capture every layer that matches the mask below."),
                selectedIndex, names);
            if (EditorGUI.EndChangeCheck())
            {
                feature.CurrentSettings.sortingLayerName = next == 0 ? "" : names[next];
                EditorUtility.SetDirty(feature);
            }

            EditorGUILayout.PropertyField(settingsProperty.FindPropertyRelative("captureLayers"));
            EditorGUILayout.PropertyField(settingsProperty.FindPropertyRelative("globalTextureName"));
            EditorGUILayout.PropertyField(settingsProperty.FindPropertyRelative("clearColor"));

            serializedObject.ApplyModifiedProperties();
        }
    }
}
