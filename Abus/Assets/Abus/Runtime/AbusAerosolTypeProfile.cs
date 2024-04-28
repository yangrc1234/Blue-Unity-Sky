using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace Abus.Runtime
{
    [CreateAssetMenu(menuName = "Abus/Aerosol Type Profile")]
    public class AbusAerosolTypeProfile : ScriptableObject
    {
        [System.Serializable]
        public struct Entry
        {
            [FormerlySerializedAs("particle")] public AbusAerosolComponentProfile component;
            [FormerlySerializedAs("Number")] public float NumberPerCM3;
        }
        
        public Entry[] entries;
        
        [FormerlySerializedAs("Thickness")] [Header("Vertical Profile")]
        public float PlanetBoundaryLayerHeight = 2.0f;
        [Range(0.01f, 100.0f)]
        public float scaleHeightKM = 8.0f;

        private void OnValidate()
        {
            AbusAerosolMixer.NotifyProfileChanged(this);
        }
    }
}