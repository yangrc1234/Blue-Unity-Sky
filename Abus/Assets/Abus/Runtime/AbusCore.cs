using System;
using System.Collections.Generic;
using System.IO;
using Rcying.Atmosphere;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

namespace Abus.Runtime
{
    public enum EAerosolHeightType
    {
        PlanetBoundaryLayer,
        Transported,
        Troposphere,
        Stratosphere,
    }
    
    /// <summary>
    /// Describes ONE aerosol component(soot, water soluble etc.),
    /// Including its density(total geometry cross section), particle profile and its vertical profile.
    /// </summary>
    [System.Serializable]
    public class InstancedAtmosphereAerosolComponent
    {
        [Header("Particle Property")] 
        public RefractiveIndex RefractiveIndex = new RefractiveIndex(){real = 1.41f, imag = -0.0016f};
        [Range(0.01f, 20.0f)]
        public float radiusUm = 0.1f;

        public float radiusGeometricDeviation = 0.0f;
        [Range(0.0f, 1.0f)]
        public float geometryCrossSection = 0.01f;

        [Header("Vertical Profile")] [Range(0.01f, 8.0f)]
        public EAerosolHeightType heightType;
        public float scaleHeightKM = 8.0f;  // Value above PBL layer.
    }
    
    /// <summary>
    /// Core of whole system.
    /// Defines atmosphere, solar irradiance.
    /// </summary>
    [ExecuteAlways]
    public class AbusCore : MonoBehaviour
    {
        #region Inspector
        /// <summary>
        /// A csv file containing solar irradiance data,
        /// each line should be (wavelength in NM, irradiance) format.
        /// </summary>
        [Header("Sun Irradiance")]
        [SerializeField]
        private WavelengthFloatData solarIrradianceTable;
        /// <summary>
        /// Calculated irradiance values will be multiplied by this.
        /// </summary>
        public float solarIrradianceScale = 1.0f;
        /// <summary>
        /// Bound light for the system. Light properties will be updated, and light rotation will be used as sun direction.
        /// </summary>
        public Light boundLight;
        /// <summary>
        /// Angular diameter of sun.
        /// </summary>
        public float sunAngularDiameter = 0.533f;

        public float HalfSunAngularDiameterRad =>  Mathf.Deg2Rad * sunAngularDiameter * 0.5f;

        [Header("Ground Albedo")] 
        [SerializeField]
        private WavelengthFloatData groundAlbedo;

        [Header("Rayleigh")]
        [Range(0.1f, 10.0f)]
        public float RayleighScaleHeight = 7.994f;
        [Range(0.01f, 10.0f)]
        public float RayleighDensityScale = 1.0f;
        [SerializeField]
        private WavelengthFloatData RayleighDepolarizationFactor;

        /// <summary>
        /// We give OZone a fixed vertical profile.
        /// Below 10KM, Troposphere, configurable constant ozone. Standard value is 6.21e17/m^3 (Density at 0.2mp)
        /// 10KM - 35KM(stratosphere), has a spike(density configurable) at 20KM and decrease linearly to 30KM/10KM. Standard value at spike is 4.6e18/m^3
        /// 35KM+, exponential decay. 1e10 at 60KM. Standard value at 35KM is 1.242e18/m^3. The ScaleHeight is ~1.341388
        /// </summary>
        [Header("OZone Layer")]
        [SerializeField]
        private WavelengthFloatData ozoneAbsorptionCrossSection;
        // Overall density control
        [SerializeField]
        [Range(0.0f, 2.0f)]
        public float oZoneDensityScale = 1.0f;

        // /M^3
        public float OZoneLowerDensity => oZoneDensityScale * 6.21e17f;
        // /M^3
        public float OZoneStratosphereMidDensity => oZoneDensityScale * 4.6e18f;
        // /M^3
        public float OZoneStratosphereTopDensity => oZoneDensityScale * 1.242e18f;
        public float OZoneUpperHeightScale => 1.341388f;

        public float GetOzoneDensityAtHeight(float AltitudeKM)
        {
            if (AltitudeKM < 10.0f)
                return OZoneLowerDensity;
            else if (AltitudeKM < 20.0f)
                return Mathf.Lerp(OZoneLowerDensity, OZoneStratosphereMidDensity, (AltitudeKM - 10.0f) / 10.0f);
            else if (AltitudeKM < 35.0f)
                return Mathf.Lerp(OZoneStratosphereMidDensity, OZoneStratosphereTopDensity, (AltitudeKM - 20.0f) / 15.0f);
            else
                return OZoneStratosphereTopDensity * Mathf.Exp(-(AltitudeKM - 35.0f) / OZoneUpperHeightScale);
        }
        
        [FormerlySerializedAs("MieParticles")] [Header("Aerosols")]
        public List<InstancedAtmosphereAerosolComponent> Aerosols;

        public float PlanetBoundaryLayerHeight;
        #endregion
        
        #if UNITY_EDITOR
        private WavelengthFloatData cachedSolarIrradianceTable;
        private void OnValidate()
        {
            if (solarIrradianceTable != cachedSolarIrradianceTable)
            {
                cachedSolarIrradianceTable = solarIrradianceTable;
                
                ReadAndProcessSolarIrradianceTable();
            }

            MarkSettingsDirty();
        }
        #endif

        public void MarkSettingsDirty()
        {
            GetComponent<AbusLutUpdater>()?.MarkSettingsDirty(AbusLutUpdater.EDirtyFlags.Atmosphere);
        }

        private bool initialized = false;

        private void Update()
        {
            TryInitialize();
            if (boundLight)
                UpdateBoundLight();
        }

        private void UpdateBoundLight()
        {
            
        }

        private void TryInitialize()
        {
            if (initialized)
                return;
            initialized = true;
            
            ReadAndProcessSolarIrradianceTable();
        }

        private void OnEnable()
        {
            initialized = false;
            TryInitialize();
        }

        #region Sun Irradiance
        /// <summary>
        /// Get sun color mapped to SRGB.
        /// Note this is the color on outer atmosphere,
        /// Shouldn't be used for scene lighting!
        /// </summary>
        public Color SRGBSolarIrradiance => cachedSRGBSolarIrradiance * solarIrradianceScale;

        struct SolarWavelengthEntry
        {
            public float wavelength;
            public float irradiance;
            public float width;
        }
        
        private Color cachedSRGBSolarIrradiance;

        private void ReadAndProcessSolarIrradianceTable()
        {
            // Cache SRGB Solar Irradiance
            if (solarIrradianceTable == null || solarIrradianceTable.Data == null)
                return;
            
            Vector3 XYZ = Vector3.zero;
            for (int i = 1; i < solarIrradianceTable.Data.Count; i++)
            {
                var waveLength = solarIrradianceTable.Data[i].wavelength;
                var dw = solarIrradianceTable.Data[i].wavelength - solarIrradianceTable.Data[i - 1].wavelength;
                var irradiance = solarIrradianceTable.Data[i].value;

                XYZ += CommonUtils.MapWaveLengthToXYZ(waveLength) * irradiance * dw;
            }

            cachedSRGBSolarIrradiance = CommonUtils.ConvertXyzToSRGB(XYZ);
        }
        
        #endregion

        #region Atmosphere Properties

        public float CalculateAverageIrradianceOfWavelength(float waveLength, float dw)
        {
            if (solarIrradianceTable == null)
                return 0.0f;
            return solarIrradianceTable.CalculateValueAtWavelength(waveLength, dw) * solarIrradianceScale;
        }
        
        public float CalculateGroundAlbedoOfWavelength(float waveLength, float dw)
        {
            if (groundAlbedo == null)
                return 0.0f;
            return groundAlbedo.CalculateValueAtWavelength(waveLength, dw);
        }
        
        public float CalculateOZoneAbsorptionCrossSectionM2(float waveLength, float dw)
        {
            if (ozoneAbsorptionCrossSection == null)
                return 0.0f;
            return ozoneAbsorptionCrossSection.CalculateValueAtWavelength(waveLength, dw);
        }

        public float CalculateRayleighDepolarizationFactor(float waveLength)
        {
            if (RayleighDepolarizationFactor == null)
            {
                return 0.0f;
            }

            return RayleighDepolarizationFactor.CalculateValueAtWavelength(waveLength);
        }

        #endregion

        public static void NotifyProfileChanged(object changedObj)
        {
            foreach (var core in FindObjectsOfType<AbusCore>())
            {
                core.MarkSettingsDirty();
            }
        }
    }
}