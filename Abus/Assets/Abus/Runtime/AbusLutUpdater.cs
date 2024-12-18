﻿using System;
using System.Collections.Generic;
using Rcying.Atmosphere;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.GlobalIllumination;
using UnityEngine.Pool;
using UnityEngine.Rendering;
using UnityEngine.Serialization;
using RenderSettings = UnityEngine.RenderSettings;

namespace Abus.Runtime
{
    [ExecuteAlways]
    [RequireComponent(typeof(AbusCore))]
    public class AbusLutUpdater : MonoBehaviour
    {
        [Header("General")]
        public Vector2Int skyViewLutSize = new Vector2Int(128, 128);
        public Vector2Int transmittanceTextureSize = new Vector2Int(512, 64);
        public Vector2Int multipleScatteringTextureSize = new Vector2Int(64, 64);
        public Vector3Int aerialPerspectiveTextureSize = new Vector3Int(16, 16, 64);
        [Range(80.0f, 100.0f)]
        public float TraceTopAltitude = 100.0f;
        [Range(0.0f, 30.0f)]
        public float captureAltitude = 0.0f;
        [Range(0.0f, 300.0f)]
        public float aerialPerspectiveLutDistance = 100.0f;

        [Header("Compute Shaders")]
        public ComputeShader TransmittanceCS;
        public ComputeShader MultipleScatteringCS;
        public ComputeShader SkyViewCS;
        public ComputeShader UtilShaders;
        public ComputeShader SceneLightingCS;
        public ComputeShader AerialPerspectiveCS;
        
        [Header("Spectrum Rendering")]
        [SerializeField] private int numWavelengths = 8;
        public int NumWavelengths => numWavelengths;
        public const float MaxWavelength = 700;
        public const float MinWavelength = 380;

        private bool initialized = false;
        private AbusCore core;
        
        protected virtual void InitializeIfNot()
        {
            if (initialized)
                return;
            initialized = true;
            DirtyFlags = EDirtyFlags.All;
        }

        private void ClearRT(RenderTexture Target)
        {
            if (Target.volumeDepth > 1)
            {
                UtilShaders.SetTexture(1, "ClearVolumeTarget",  Target);
                UtilShaders.Dispatch(1, CommonUtils.GetDispatchGroup(new Vector3Int(Target.width, Target.height, Target.volumeDepth), new Vector3Int(4, 4, 4)));
            }
            else
            {
                UtilShaders.SetTexture(0, "ClearTarget",  Target);
                UtilShaders.Dispatch(0, CommonUtils.GetDispatchGroup(new Vector2Int(Target.width, Target.height), new Vector2Int(8, 8)));
            }
        }

        public float GetIterateWavelengthNM(int Index)
        {
            // We supports ranges from 400-700 (Visible light range).
            return MinWavelength + (MaxWavelength - MinWavelength) * ((Index + 0.5f) / NumWavelengths);
        }

        public float GetWavelengthDW()
        {
            return (MaxWavelength - MinWavelength) / NumWavelengths;
        }

        private void OnEnable()
        {
            DirtyFlags = EDirtyFlags.All;
        }

        private void OnValidate()
        {
            MarkSettingsDirty(EDirtyFlags.All);
        }

        protected virtual void Update()
        {
            UpdateLuts();
        }

        public virtual void UpdateLuts(bool forceFlushReadback = false)
        {
            InitializeIfNot();

            if (!PrepareRenderingLuts())
            {
                return;
            }
            DoRenderLuts(forceFlushReadback);

            SetupGlobalSRGBTextures();
        }

        private void SetupGlobalSRGBTextures()
        {
            Shader.SetGlobalTexture("SRGBSkyViewTexture", SRGBSkyViewLut);
            Shader.SetGlobalTexture("SRGBAerialPerspectiveLut", SRGBAerialPerspectiveLut);
            Shader.SetGlobalTexture("SRGBTransmittanceTexture", SRGBTransmittanceLut);
            Shader.SetGlobalTexture("SRGBMultipleScatteringTexture", SRGBMultipleScatteringLut);
        }

        private RenderTexture _transmittanceRT;
        private RenderTexture _srgbtransmittanceRT;
        private RenderTexture _multipleScatteringLut;
        private RenderTexture _srgbMultipleScatteringLut;
        private RenderTexture _srgbSkyViewLut;
        private RenderTexture _aerialPerspectiveScattering;
        private RenderTexture _aerialPerspectiveExtinction;
        private RenderTexture _srgbAerialPerspectiveLut;
        
        public RenderTexture SRGBSkyViewLut => _srgbSkyViewLut;
        public RenderTexture SRGBAerialPerspectiveLut => _srgbAerialPerspectiveLut;
        public RenderTexture SRGBTransmittanceLut => _srgbtransmittanceRT;
        public RenderTexture SRGBMultipleScatteringLut => _srgbMultipleScatteringLut;

        public void DoRenderLuts(bool forceFlushReadback = false)
        {
            UpdateGlobalAtmosphereShaderParameters();

            TryFinishReadbackRequest();

            bool bUpdateReadbackBuffer = ReadbackRequest.hasError || ReadbackRequest.done || forceFlushReadback;
            
            // Clear sky view LUT.
            ClearRT(_srgbSkyViewLut);
            ClearRT(_srgbtransmittanceRT);
            ClearRT(_srgbMultipleScatteringLut);
            ClearRT(_srgbAerialPerspectiveLut);

            if (bUpdateReadbackBuffer)
                ClearReadbackBuffer();
            
            for (int iWavelength = 0; iWavelength < NumWavelengths; iWavelength += 4)
            {
                SetupWavelengthIteratingShaderParameters(iWavelength);
                RenderTransmittanceLut();
                RenderMultipleScatteringLut();
                RenderSkyViewLut();
                RenderAerialPerspectiveLut();
            }

            if (bUpdateReadbackBuffer)
            {
                RunSceneLightingUpdatePass();
                ReadbackRequest = AsyncGPUReadback.Request(ReadbackBuffer, 10 * 16, 0);
            }

            if (forceFlushReadback)
            {
                ReadbackRequest.WaitForCompletion();
                TryFinishReadbackRequest();
            }
        }

        private AsyncGPUReadbackRequest ReadbackRequest;
        private GraphicsBuffer ReadbackBuffer;
        public Vector3 SunDiscIrradiance;
        public SphericalHarmonicsL2 SkyIrradiance;

        private void OnDestroy()
        {
            if (ReadbackBuffer != null)
                ReadbackBuffer.Dispose();
        }

        private void TryFinishReadbackRequest()
        {
            if (!ReadbackRequest.hasError && ReadbackRequest.done)
            {
                // Readback the data.
                NativeArray<Vector4> nativeArray = ReadbackRequest.GetData<Vector4>(0);
                // 0-2 Skylight R SH2
                // 3-5 Skylight G SH2
                // 6-8 Skylight B SH2
                // 9 Directional Light.
                
                // R:
                SkyIrradiance[0, 0] = nativeArray[0].x;
                SkyIrradiance[0, 1] = nativeArray[0].y;
                SkyIrradiance[0, 2] = nativeArray[0].z;
                SkyIrradiance[0, 3] = nativeArray[0].w;
                SkyIrradiance[0, 4] = nativeArray[1].x;
                SkyIrradiance[0, 5] = nativeArray[1].y;
                SkyIrradiance[0, 6] = nativeArray[1].z;
                SkyIrradiance[0, 7] = nativeArray[1].w;
                SkyIrradiance[0, 8] = nativeArray[2].x;
                
                // G:
                SkyIrradiance[1, 0] = nativeArray[3].x;
                SkyIrradiance[1, 1] = nativeArray[3].y;
                SkyIrradiance[1, 2] = nativeArray[3].z;
                SkyIrradiance[1, 3] = nativeArray[3].w;
                SkyIrradiance[1, 4] = nativeArray[4].x;
                SkyIrradiance[1, 5] = nativeArray[4].y;
                SkyIrradiance[1, 6] = nativeArray[4].z;
                SkyIrradiance[1, 7] = nativeArray[4].w;
                SkyIrradiance[1, 8] = nativeArray[5].x;
                
                // B:
                SkyIrradiance[2, 0] = nativeArray[6].x;
                SkyIrradiance[2, 1] = nativeArray[6].y;
                SkyIrradiance[2, 2] = nativeArray[6].z;
                SkyIrradiance[2, 3] = nativeArray[6].w;
                SkyIrradiance[2, 4] = nativeArray[7].x;
                SkyIrradiance[2, 5] = nativeArray[7].y;
                SkyIrradiance[2, 6] = nativeArray[7].z;
                SkyIrradiance[2, 7] = nativeArray[7].w;
                SkyIrradiance[2, 8] = nativeArray[8].x;

                // Directional light
                SunDiscIrradiance = new Vector3(nativeArray[9].x, nativeArray[9].y, nativeArray[9].z);
                
                nativeArray.Dispose();
            }
        }

        private void ClearReadbackBuffer()
        {
            if (ReadbackBuffer == null)
            {
                ReadbackBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 10, 16);
            }
            SceneLightingCS.SetBuffer(1, "OutReadbackBuffer", ReadbackBuffer);
            SceneLightingCS.Dispatch(1, new Vector3Int(1, 1, 1));
        }


        private void RunSceneLightingUpdatePass()
        {
            SceneLightingCS.SetTexture(0, "SRGBSkyViewTexture", _srgbSkyViewLut);
            SceneLightingCS.SetTexture(0, "SRGBTransmittanceTexture", _srgbtransmittanceRT);
            SceneLightingCS.SetBuffer(0, "OutReadbackBuffer", ReadbackBuffer);
            SceneLightingCS.Dispatch(0, new Vector3Int(1, 1, 1));
        }

        [Flags]
        public enum EDirtyFlags
        {
            Atmosphere = 1 << 0,
            Wavelength = 1 << 1,
            All = Atmosphere | Wavelength
        }

        public EDirtyFlags DirtyFlags { get; private set; }

        public void MarkSettingsDirty(EDirtyFlags flags)
        {
            DirtyFlags |= flags;
        }

        private bool PrepareRenderingLuts()
        {
            if (core == null)
            {
                core = GetComponent<AbusCore>();
            }
            if (core == null)
                return false;

            if (TransmittanceCS == null || MultipleScatteringCS == null || SkyViewCS == null)
                return false;

            // Verify settings.
            if (wavelengthCachedValuesList == null || wavelengthCachedValuesList.Length != NumWavelengths)
            {
                DirtyFlags |= EDirtyFlags.Wavelength;
            }
            
            // Update settings based on dirty flags.
            if (DirtyFlags.HasFlag(EDirtyFlags.Atmosphere))
            {
                MiePropertiesLut = null;
                MieWavelengthLut = null;
            }

            if (DirtyFlags.HasFlag(EDirtyFlags.Atmosphere) || DirtyFlags.HasFlag(EDirtyFlags.Wavelength))
            {
                ApplyWavelengthSettings();
                MieWavelengthLut = null;
                ApplyCloudSettings();
            }
            
            if (!MieWavelengthLut) MieWavelengthLut = CreateMieWavelengthLut();
            if (!MiePropertiesLut) MiePropertiesLut = CreateMiePropertiesLut();
            
            DirtyFlags = 0;

            // After testing, half/full-precision produces nearly no difference.  
            CommonUtils.CreateLUT(ref _transmittanceRT, "Transmittance", transmittanceTextureSize.x, transmittanceTextureSize.y, 1, RenderTextureFormat.ARGBHalf);
            CommonUtils.CreateLUT(ref _srgbtransmittanceRT, "SRGB Transmittance", transmittanceTextureSize.x, transmittanceTextureSize.y, 1, RenderTextureFormat.ARGBHalf);
            CommonUtils.CreateLUT(ref _multipleScatteringLut, "Multiple Scattering", multipleScatteringTextureSize.x, multipleScatteringTextureSize.y, 1, RenderTextureFormat.ARGBHalf);
            CommonUtils.CreateLUT(ref _srgbMultipleScatteringLut, "SRGB Multiple Scattering", multipleScatteringTextureSize.x, multipleScatteringTextureSize.y, 1, RenderTextureFormat.ARGBHalf);
            CommonUtils.CreateLUT(ref _srgbSkyViewLut, "SRGB Sky LUT", skyViewLutSize.x, skyViewLutSize.y, 1, RenderTextureFormat.ARGBHalf, TextureWrapMode.Mirror, TextureWrapMode.Clamp);
            CommonUtils.CreateLUT(ref _aerialPerspectiveScattering, "Aerial Perspective Scattering", aerialPerspectiveTextureSize.x, aerialPerspectiveTextureSize.y, aerialPerspectiveTextureSize.z, RenderTextureFormat.ARGBHalf, TextureWrapMode.Repeat, TextureWrapMode.Clamp);
            CommonUtils.CreateLUT(ref _aerialPerspectiveExtinction, "Aerial Perspective Extinction", aerialPerspectiveTextureSize.x, aerialPerspectiveTextureSize.y, aerialPerspectiveTextureSize.z, RenderTextureFormat.ARGBHalf, TextureWrapMode.Repeat, TextureWrapMode.Clamp);
            CommonUtils.CreateLUT(ref _srgbAerialPerspectiveLut, "SRGB Aerial Perspective LUT", aerialPerspectiveTextureSize.x, aerialPerspectiveTextureSize.y, aerialPerspectiveTextureSize.z, RenderTextureFormat.ARGBHalf, TextureWrapMode.Repeat, TextureWrapMode.Clamp);

            return true;
        }

        private float CloudExtinctionCoefficient;
        private Vector4 CloudPhaseParams;
        private void ApplyCloudSettings()
        {
            CloudExtinctionCoefficient = core.CloudDensity; // Assume all wavelength has same extinction coefficient for cloud, just use density as coefficient.
            CloudPhaseParams = MieUtils.JEPhaseParams(5.0f);    // Assume top layer cloud particles to be this size. 
        }

        public Texture2D MieWavelengthLut { get; private set; }
        public Texture2D MiePropertiesLut { get; private set; }

        Texture2D CreateMieWavelengthLut()
        {
            var lut = new Texture2D(core.AerosolComponents.Count, numWavelengths, TextureFormat.RGBAFloat, false);
            lut.name = "Mie Wavelength";
            lut.filterMode = FilterMode.Bilinear;
            lut.wrapMode = TextureWrapMode.Clamp;

            for (int iMieType = 0; iMieType < core.AerosolComponents.Count; iMieType++)
            {
                var radiusUM = core.AerosolComponents[iMieType].radiusUm;
                for (int iWavelength = 0; iWavelength < numWavelengths; iWavelength++)
                {
                    var wavelength = GetIterateWavelengthNM(iWavelength);
                    var refractiveIndex = core.AerosolComponents[iMieType].RefractiveIndex;

                    double SumExtEfficiency = 0.0, SumScatEfficiency = 0.0, Sumg = 0.0;
                    double SumWeight = 0.0f;
                    
                    // With small particles, mie scattering could become wavelength-dependent.
                    // If we just use all-same-radius particle distribution, result will be strongly biased towards a single color.
                    // So we would calculate mie scattering with multiple sizes and average them, to get a more "smooth" result over wavelength.
                    const int MieCalculationAverageSample = 3;
                    
                    for (int iSmooth = 0; iSmooth < MieCalculationAverageSample; iSmooth++)
                    {
                        // Cover relative size from 0.1 to 10.0.
                        var relativeSize = Mathf.Exp((iSmooth + 0.5f) / MieCalculationAverageSample - 0.5f);
                        var currentSize = radiusUM * relativeSize;
                        var sizeParameter = 2.0f * Mathf.PI * currentSize / (1e-3f * wavelength);
                        
                        MieUtils.CalculateMie(sizeParameter, refractiveIndex, null, out var ExtEfficiency, out var ScatEfficiency, out var BackScatEfficiency, out var g, null, null);
                        var weight = CommonUtils.LogNormalPDFFromGeometric(core.AerosolComponents[iMieType].radiusUm,
                            core.AerosolComponents[iMieType].radiusGeometricDeviation, currentSize);
                        SumWeight += weight;
                        SumScatEfficiency += ScatEfficiency * weight;
                        SumExtEfficiency += ExtEfficiency * weight;
                        Sumg += g * weight;
                    }

                    SumScatEfficiency /= SumWeight;
                    SumExtEfficiency /= SumWeight;
                    Sumg /= SumWeight;
                    
                    lut.SetPixel(iMieType, iWavelength, new Color((float)SumExtEfficiency, (float)SumScatEfficiency, (float)Sumg, 0.0f));
                }
            }
            lut.Apply();
            return lut;
        }
        
        Texture2D CreateMiePropertiesLut()
        {
            var lut = new Texture2D(core.AerosolComponents.Count, 2, TextureFormat.RGBAFloat, false);
            lut.name = "Mie Properties";
            for (int iMieType = 0; iMieType < core.AerosolComponents.Count; iMieType++)
            {
                var radius = core.AerosolComponents[iMieType].radiusUm;
                var geometryCrossSectionKM = core.AerosolComponents[iMieType].geometryCrossSection * core.GetAerosolScale();

                float HeightProfileInfo = (int)core.AerosolComponents[iMieType].heightType;
                lut.SetPixel(iMieType, 0, new Vector4(geometryCrossSectionKM, HeightProfileInfo, 0.0f, core.AerosolComponents[iMieType].scaleHeightKM));
                lut.SetPixel(iMieType, 1, radius > 1.5f ? MieUtils.JEPhaseParams(core.AerosolComponents[iMieType].radiusUm) : Color.black);
            }

            lut.Apply();
            return lut;
        }

        private void RenderMultipleScatteringLut()
        {
            MultipleScatteringCS.SetTexture(0, "OutMultipleScattering", _multipleScatteringLut);
            MultipleScatteringCS.SetTexture(0, "TransmittanceTexture", _transmittanceRT);
            MultipleScatteringCS.SetTexture(0, "OutSRGBMultipleScattering", _srgbMultipleScatteringLut);
            MultipleScatteringCS.Dispatch(0, CommonUtils.GetDispatchGroup(multipleScatteringTextureSize, new Vector2Int(8, 8)));
        }
        
        private void RenderSkyViewLut()
        {
            SkyViewCS.SetTexture(0, "MultipleScatteringTexture", _multipleScatteringLut);
            SkyViewCS.SetTexture(0, "TransmittanceTexture", _transmittanceRT);
            SkyViewCS.SetTexture(0, "OutSRGBSkyColor", _srgbSkyViewLut);
            
            SkyViewCS.Dispatch(0, CommonUtils.GetDispatchGroup(skyViewLutSize, new Vector2Int(8, 8)));
        }
        
        private void RenderTransmittanceLut()
        {
            TransmittanceCS.SetTexture(0, "OutTransmittance", _transmittanceRT);
            TransmittanceCS.SetTexture(0, "OutSRGBTransmittance", _srgbtransmittanceRT);
            TransmittanceCS.Dispatch(0, CommonUtils.GetDispatchGroup(transmittanceTextureSize, new Vector2Int(8, 8)));
        }

        private void RenderAerialPerspectiveLut()
        {
            // Ensure the compute shader and render texture are initialized
            if (AerialPerspectiveCS == null || _aerialPerspectiveScattering == null)
            {
                Debug.LogError("Aerial Perspective Compute Shader or Render Texture is not assigned.");
                return;
            }

            // Set the necessary parameters for the compute shader
            AerialPerspectiveCS.SetTexture(0, "MultipleScatteringTexture", _multipleScatteringLut);
            AerialPerspectiveCS.SetTexture(0, "TransmittanceTexture", _transmittanceRT);
            AerialPerspectiveCS.SetTexture(0, "OutAerialPerspectiveScattering", _aerialPerspectiveScattering);
            AerialPerspectiveCS.SetTexture(0, "OutAerialPerspectiveExtinction", _aerialPerspectiveExtinction);
            AerialPerspectiveCS.Dispatch(0, CommonUtils.GetDispatchGroup(aerialPerspectiveTextureSize, new Vector3Int(4, 4, 4)));

            // Integration pass.
            AerialPerspectiveCS.SetTexture(1, "MultipleScatteringTexture", _multipleScatteringLut);
            AerialPerspectiveCS.SetTexture(1, "TransmittanceTexture", _transmittanceRT);
            AerialPerspectiveCS.SetTexture(1, "AerialPerspectiveScattering", _aerialPerspectiveScattering);
            AerialPerspectiveCS.SetTexture(1, "AerialPerspectiveExtinction", _aerialPerspectiveExtinction);
            AerialPerspectiveCS.SetTexture(1, "OutAerialPerspectiveSRGBLut", _srgbAerialPerspectiveLut);
            AerialPerspectiveCS.Dispatch(1, CommonUtils.GetDispatchGroup(new Vector3Int(aerialPerspectiveTextureSize.x, aerialPerspectiveTextureSize.y, 1), new Vector3Int(8, 8, 1)));
        }

        struct IteratingWavelengthCachedValues
        {
            public float wavelength;
            public float solarIrradiance;
            public float groundAlbedo;
            public float rayleighPhaseGamma;
            public float rayleighSeaLevelScatteringCoefficient;
            public float ozoneAbsorptionCrossSection;
            public Vector3 NormalizedRGBWeight;
            public Vector3 RGBWeight;
        }

        private IteratingWavelengthCachedValues[] wavelengthCachedValuesList;

        /// <summary>
        /// Apply wave length settings to updating.
        /// </summary>
        private void ApplyWavelengthSettings()
        {
            if (wavelengthCachedValuesList == null || wavelengthCachedValuesList.Length != numWavelengths)
                wavelengthCachedValuesList = new IteratingWavelengthCachedValues[numWavelengths];

            Vector3 TotalRGBWeight = Vector3.zero;
            for (int i = 0; i < NumWavelengths; i++)
            {
                IteratingWavelengthCachedValues value;
                var wavelength = GetIterateWavelengthNM(i);
                wavelengthCachedValuesList[i].wavelength = wavelength;
                var dw = GetWavelengthDW();
                var depolarizationFactor = core.CalculateRayleighDepolarizationFactor(wavelength);
                var airRefractiveIndex = RayleighUtils.GetStandardAirRefractiveIndex(wavelength);
                var rayleighSeaLevelScatteringCoefficient = RayleighUtils.CalculateRayleighScatteringCoefficientM(wavelength, depolarizationFactor, airRefractiveIndex) * core.RayleighDensityScale;
                wavelengthCachedValuesList[i].groundAlbedo = core.CalculateGroundAlbedoOfWavelength(wavelength, dw); 
                wavelengthCachedValuesList[i].rayleighSeaLevelScatteringCoefficient = (float)(rayleighSeaLevelScatteringCoefficient * 1e3);//Convert to /KM
                wavelengthCachedValuesList[i].rayleighPhaseGamma = depolarizationFactor / (2.0f - depolarizationFactor);
                wavelengthCachedValuesList[i].solarIrradiance = core.CalculateAverageIrradianceOfWavelength(wavelength, dw);
                wavelengthCachedValuesList[i].ozoneAbsorptionCrossSection = core.CalculateOZoneAbsorptionCrossSectionM2(wavelength, dw);
                wavelengthCachedValuesList[i].RGBWeight = CommonUtils.ConvertXyzToSRGB(CommonUtils.MapWaveLengthToXYZ(GetIterateWavelengthNM(i)));
            }
            
            // Normalize rgb weight. 
            for (int i = 0; i < NumWavelengths; i++)
            {
                TotalRGBWeight += wavelengthCachedValuesList[i].RGBWeight;
            }
            
            for (int i = 0; i < NumWavelengths; i++)
            {
                wavelengthCachedValuesList[i].NormalizedRGBWeight.x = wavelengthCachedValuesList[i].RGBWeight.x / TotalRGBWeight.x;
                wavelengthCachedValuesList[i].NormalizedRGBWeight.y = wavelengthCachedValuesList[i].RGBWeight.y / TotalRGBWeight.y;
                wavelengthCachedValuesList[i].NormalizedRGBWeight.z = wavelengthCachedValuesList[i].RGBWeight.z / TotalRGBWeight.z;
            }
        }

        private void SetupWavelengthIteratingShaderParameters(int FirstSpectrumIndex)
        {
            Vector4 CurrentIteratingWavelengthUM = Vector4.zero;
            Vector4 CurrentIteratingWavelengthUMInv = Vector4.zero;
            Vector4 CurrentIteratingSunIrradiance = Vector4.zero;
            Vector4 GroundSpectrumAlbedo = Vector4.zero;
            Vector4 RayleighSeaLevelScatteringCoefficients = Vector4.zero;
            Vector4 RayleighPhaseFunctionGamma = Vector4.zero;
            Vector4 OZoneAbsorptionCrossSection = Vector4.zero;

            {
                Matrix4x4 NormalizedWavelengthRGBWeight = Matrix4x4.zero;
                Matrix4x4 WavelengthRGBWeight = Matrix4x4.zero;
                for (int i = FirstSpectrumIndex; i < FirstSpectrumIndex + 4 && i < NumWavelengths; i++)
                {
                    var values = wavelengthCachedValuesList[i];
                    CurrentIteratingWavelengthUM[i - FirstSpectrumIndex] = values.wavelength * 1e-3f;
                    CurrentIteratingWavelengthUMInv[i - FirstSpectrumIndex] =
                        1.0f / CurrentIteratingWavelengthUM[i - FirstSpectrumIndex];
                    CurrentIteratingSunIrradiance[i - FirstSpectrumIndex] = values.solarIrradiance;
                    RayleighSeaLevelScatteringCoefficients[i - FirstSpectrumIndex] =
                        values.rayleighSeaLevelScatteringCoefficient;
                    RayleighPhaseFunctionGamma[i - FirstSpectrumIndex] = values.rayleighPhaseGamma;
                    GroundSpectrumAlbedo[i - FirstSpectrumIndex] = values.groundAlbedo;
                    OZoneAbsorptionCrossSection[i - FirstSpectrumIndex] = values.ozoneAbsorptionCrossSection;

                    NormalizedWavelengthRGBWeight.SetColumn(i - FirstSpectrumIndex, values.NormalizedRGBWeight);
                    WavelengthRGBWeight.SetColumn(i - FirstSpectrumIndex, values.RGBWeight);
                }
                Shader.SetGlobalMatrix("NormalizedWavelengthRGBWeight", NormalizedWavelengthRGBWeight);
                Shader.SetGlobalMatrix("WavelengthToSRGB", WavelengthRGBWeight);
            }

            Shader.SetGlobalInteger("CurrentIteratingFirstWavelengthIndex", FirstSpectrumIndex);
            Shader.SetGlobalVector("CurrentIteratingWavelengthUM", CurrentIteratingWavelengthUM);
            Shader.SetGlobalVector("CurrentIteratingWavelengthUMInv", CurrentIteratingWavelengthUMInv);
            Shader.SetGlobalVector("CurrentIteratingSunIrradiance", CurrentIteratingSunIrradiance);
            Shader.SetGlobalVector("CurrentIteratingSunRadiance", CurrentIteratingSunIrradiance / (Mathf.PI * core.HalfSunAngularDiameterRad * core.HalfSunAngularDiameterRad));

            Shader.SetGlobalVector("GroundSpectrumAlbedo", GroundSpectrumAlbedo);
            
            Shader.SetGlobalVector("RayleighSeaLevelSpectrumScatteringCoefficient", RayleighSeaLevelScatteringCoefficients);
            Shader.SetGlobalVector("RayleighSpectrumPhaseFunctionGamma", RayleighPhaseFunctionGamma);
            
            Shader.SetGlobalVector("OZoneAbsorptionCrossSection", OZoneAbsorptionCrossSection);          
        }

        protected virtual Vector3 GetLightDirection()
        {
            if (core && core.boundLight)
            {
                return -core.boundLight.transform.forward;
            }

            return Vector3.up;
        }
        
        private void UpdateGlobalAtmosphereShaderParameters()
        {
            Vector3 lightDirection = GetLightDirection();
            Shader.SetGlobalVector("AtmosphereLightDirection", lightDirection);
            
            Shader.SetGlobalVector("AtmosphereThicknessAndInv", new Vector4(TraceTopAltitude, 1.0f / TraceTopAltitude, 0.0f, 0.0f));
            Shader.SetGlobalFloat("GroundHeight", core.PlanetRadius);
            Shader.SetGlobalFloat("AtmosphereHeight", core.PlanetRadius + TraceTopAltitude);
            Shader.SetGlobalVector("TransmittanceTextureSizeInvSize", new Vector4(transmittanceTextureSize.x, transmittanceTextureSize.y, 1.0f / transmittanceTextureSize.x, 1.0f / transmittanceTextureSize.y));
            Shader.SetGlobalVector("MultipleScatteringTextureSizeInvSize", new Vector4(multipleScatteringTextureSize.x, multipleScatteringTextureSize.y, 1.0f / multipleScatteringTextureSize.x, 1.0f / multipleScatteringTextureSize.y));

            // Skyview LUT.
            {   
                Shader.SetGlobalVector("SkyViewTextureSizeAndInvSize", new Vector4(skyViewLutSize.x, skyViewLutSize.y, 1.0f / skyViewLutSize.x, 1.0f / skyViewLutSize.y));
                var zenithAngle = Mathf.Acos(Vector3.Dot(Vector3.up, lightDirection));
                var rotatedLightDirection = lightDirection;
                if (zenithAngle > Mathf.Deg2Rad * 88.0f)
                {
                    // Rotate the light direction to avoid singularity.
                    rotatedLightDirection = Quaternion.AngleAxis(zenithAngle * Mathf.Rad2Deg - 88.0f, Vector3.Cross(Vector3.up, -lightDirection)) * lightDirection;
                }
                Shader.SetGlobalVector("SkyViewLutUpDir", rotatedLightDirection);

                Matrix4x4 WorldToSkyViewLut = Matrix4x4.identity;
                
                // Z points to zenith
                Vector3 Z = Vector3.up;
                // Y points to right arm side if you look at sun.
                Vector3 Y = rotatedLightDirection == Z ? Vector3.right : Vector3.Cross(rotatedLightDirection, Vector3.up).normalized;
                // X points to light direction projected on horizon
                Vector3 X = Vector3.Cross(Z, Y);
                WorldToSkyViewLut.SetRow(0, X);
                WorldToSkyViewLut.SetRow(1, Y);
                WorldToSkyViewLut.SetRow(2, Z);
                Shader.SetGlobalMatrix("WorldToSkyViewLut", WorldToSkyViewLut);
            }
            
            // Aerial Perspective LUT
            {
                Shader.SetGlobalVector("AerialPerspectiveLutResolution", new Vector4(aerialPerspectiveTextureSize.x, aerialPerspectiveTextureSize.y, aerialPerspectiveTextureSize.z, 0.0f));
                Shader.SetGlobalVector("AerialPerspectiveLutResolutionInv", new Vector4(1.0f / aerialPerspectiveTextureSize.x, 1.0f / aerialPerspectiveTextureSize.y, 1.0f / aerialPerspectiveTextureSize.z, 0.0f));
                Shader.SetGlobalVector("AerialPerspectiveDepthKMAndInv", new Vector4(aerialPerspectiveLutDistance, 1.0f / aerialPerspectiveLutDistance, 0.0f, 0.0f));
                
                Matrix4x4 worldToAerialPerspectiveLut = Matrix4x4.identity;
                // X points to light direction projected on horizon
                Vector3 X = lightDirection;
                // Y points to right arm side if you look at sun.
                Vector3 Y = lightDirection == Vector3.up ? Vector3.right : Vector3.Cross(lightDirection, Vector3.up).normalized;
                // Z points to head-up when looking at sun.
                Vector3 Z = Vector3.Cross(Y, X);
                worldToAerialPerspectiveLut.SetRow(0, X);
                worldToAerialPerspectiveLut.SetRow(1, Y);
                worldToAerialPerspectiveLut.SetRow(2, Z);
                Shader.SetGlobalMatrix("WorldToAerialPerspectiveLut", worldToAerialPerspectiveLut);
            }
            

            Shader.SetGlobalFloat("RayleighScaleHeight", core.RayleighScaleHeight);

            Shader.SetGlobalTexture("MieProperties", MiePropertiesLut);
            Shader.SetGlobalTexture("MieWavelengthLut", MieWavelengthLut);
            Shader.SetGlobalInteger("NumMieTypes", core.AerosolComponents.Count);
            Shader.SetGlobalFloat("PlanetBoundaryLayerAltitude", core.PlanetBoundaryLayerAltitude);
            Shader.SetGlobalFloat("TopCloudAltitude", core.TopCloudAltitude);
            
            Shader.SetGlobalFloat("OZoneLowerDensity", core.OZoneLowerDensity);
            Shader.SetGlobalFloat("OZoneStratosphereMidDensity", core.OZoneStratosphereMidDensity);
            Shader.SetGlobalFloat("OZoneStratosphereTopDensity", core.OZoneStratosphereTopDensity);
            Shader.SetGlobalFloat("OZoneUpperHeightScale", core.OZoneUpperHeightScale);

            Shader.SetGlobalFloat("CosSunDiscHalfAngle", Mathf.Cos(core.HalfSunAngularDiameterRad));
            Shader.SetGlobalFloat("SunDiscHalfAngle", core.HalfSunAngularDiameterRad);
            Shader.SetGlobalVector("SunCenterSRGBRadiance", core.SRGBSolarIrradiance / (Mathf.PI * core.HalfSunAngularDiameterRad * core.HalfSunAngularDiameterRad));
            Shader.SetGlobalVector("SunSRGBIrradiance", core.SRGBSolarIrradiance);
            
            Shader.SetGlobalFloat("dWaveLength", GetWavelengthDW());
            Shader.SetGlobalFloat("NumWavelengths", numWavelengths);
            Shader.SetGlobalFloat("CaptureHeight", captureAltitude + core.PlanetRadius);
            
            Shader.SetGlobalFloat("CloudExtinctionCoefficient", CloudExtinctionCoefficient);
            Shader.SetGlobalFloat("CloudAltitude", core.TopCloudAltitude);
            Shader.SetGlobalVector("CloudPhaseParams", CloudPhaseParams);
            
        }
    }
}