using UnityEngine;
using UnityEngine.Serialization;

namespace Abus.Runtime
{
    [CreateAssetMenu(menuName = "Abus/Aerosol Particle Profile")]
    public class AbusAerosolComponentProfile : ScriptableObject
    {
        [Header("Radius Distribution")]
        [Range(0.1f, 100.0f)]
        public float radiusMeanUm = 10.0f;
        [Range(0.1f, 100.0f)]
        public float radiusSigma = 10.0f;

        [Header("Refractive Index")] 
        public WavelengthRefractiveIndexData RefractiveIndexData;
    }
}