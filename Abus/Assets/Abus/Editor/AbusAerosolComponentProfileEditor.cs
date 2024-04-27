using Abus.Runtime;
using UnityEditor;

namespace Abus.Editor
{
    [CustomEditor(typeof(AbusAerosolComponentProfile))]
    public class AbusAerosolComponentProfileEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var profile = (AbusAerosolComponentProfile) target;
            
            // User input is geometry mean and deviation. Show display derived parameters on inspector. 
            
            // Log-Normal parameters. 
            EditorGUILayout.LabelField("Log-Normal Parameters", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Mean", profile.Mean.ToString());
            EditorGUILayout.LabelField("Deviation", profile.Deviation.ToString());
            
            // Number distribution
            EditorGUILayout.LabelField("Size Distribution Mean(Number)", profile.geometricMean.ToString());

            // Surface distribution
            EditorGUILayout.LabelField("Size Distribution Mean(Surface)", profile.GetSurfaceAreaDistributionGeometricMean().ToString());
            
            // Volume distribution.
            EditorGUILayout.LabelField("Size Distribution Mean(Volume)", profile.GetVolumeDistributionGeometricMean().ToString());
        }
    }
}