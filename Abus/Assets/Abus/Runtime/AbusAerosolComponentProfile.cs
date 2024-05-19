using UnityEngine;
using UnityEngine.Serialization;

namespace Abus.Runtime
{
    [CreateAssetMenu(menuName = "Abus/Aerosol Particle Profile")]
    public class AbusAerosolComponentProfile : ScriptableObject
    {
        [Header("Size(Radius) Distribution(Number)")]
        [Range(0.01f, 100.0f)]
        public float geometricMean = 1.0f;
        [Range(0.01f, 100.0f)]
        public float geometricDeviation = 2.0f;

        [Header("Refractive Index")] 
        public WavelengthRefractiveIndexData RefractiveIndexData;
        
        public float Mean => Mathf.Log(geometricMean);
        public float Deviation => Mathf.Log(geometricDeviation);
        
        public float Variance
        {
            get
            {
                var deviation = Deviation;
                return deviation * deviation;
            }
        }

        public float GetNumberDistributionGeometricMean()
        {
            return geometricMean;
        }

        public float GetSurfaceAreaDistributionGeometricMean()
        {
            return Mathf.Exp(Mean + 2.0f * Variance);
        }

        public float GetVolumeDistributionGeometricMean()
        {
            // return geometricMean * Mathf.Pow(10.0f,
            //     3.0f * Mathf.Log10(geometricDeviation) * Mathf.Log10(geometricDeviation) * Mathf.Log(10.0f));
            return Mathf.Exp(Mean + 3.0f * Variance);
        }

        private void OnValidate()
        {
            AbusAerosolMixer.NotifyProfileChanged(this);
        }
    }
}