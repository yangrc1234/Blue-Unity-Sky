using System.Collections;
using System.Collections.Generic;
using Abus.Runtime;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ReferenceImageCapture))]
public class ReferenceImageCaptureEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        
        ReferenceImageCapture capture = (ReferenceImageCapture)target;
        if (GUILayout.Button("Capture"))
        {
            capture.Execute();
        }
        
        if (GUILayout.Button("Difference Pass"))
        {
            capture.RunDifferencePass();
        }
        
        // Button for open the folder. 
        if (GUILayout.Button("Open Folder"))
        {
            System.Diagnostics.Process.Start(capture.AbsoluteFolderPath);
        }
    }
}
