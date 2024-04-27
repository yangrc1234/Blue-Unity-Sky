using System.Collections;
using System.Collections.Generic;
using Abus.Runtime;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(WavelengthRefractiveIndexData))]
public class WavelengthRefractiveIndexDataEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var data = target as WavelengthRefractiveIndexData;
        
        // Data comment.
        // Use text field to edit.
        EditorGUILayout.PropertyField(serializedObject.FindProperty("dataComment"));
        
        // Data info section.
        // Draw a header.
        GUILayout.Label("Loaded Data Info", EditorStyles.boldLabel);
        // Show basic data, (Cover range, count).
        if (data.Data.Count > 0)
        {
            GUILayout.Label($"Data count: {data.Data.Count}");
            // Wavelength range
            GUILayout.Label($"Wavelength range: {data.Data[0].wavelength}nm - {data.Data[^1].wavelength}nm");
        }
        else
        {
            GUILayout.Label("No data.");
        }
        
        // Space.
        GUILayout.Space(10);
        
        // Import data section.
        // Draw a header.
        GUILayout.Label("Import Data", EditorStyles.boldLabel);
        
        // Wavelength scale.
        EditorGUILayout.PropertyField(serializedObject.FindProperty("wavelengthImportScale"));
        
        EditorGUILayout.PropertyField(serializedObject.FindProperty("sourceCsv"));
        if (data.sourceCsv)
        {
            if (GUILayout.Button("Load from CSV"))
            {
                serializedObject.ApplyModifiedProperties();
                data.ImportFromCSV(data.sourceCsv);
                serializedObject.Update();
            }
        }

        serializedObject.ApplyModifiedProperties();
    }
}