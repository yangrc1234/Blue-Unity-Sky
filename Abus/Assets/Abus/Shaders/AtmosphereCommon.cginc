#pragma once

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

/***************************************
 * Common Functions.
 ***************************************/
float ClampCosine(float mu) {
    return clamp(mu, float(-1.0), float(1.0));
}

float ClampDistance(float d) {
    return max(d, 0.0);
}

float IsotropicPhase()
{
    return 1.0f / (4.0f * PI);
}

// Map 0~1 value to a (0.5f * TexelSize, 1.0f - 0.5f * TexelSize)
float GetTextureCoordFromUnitRange(float x, float TexelSize) {
    return x + (0.5 - x) * TexelSize;
}

// Inverse mapping of above.
float GetUnitRangeFromTextureCoord(float u, float TexelSize) {
    return saturate((u - 0.5 * TexelSize) / (1.0 - TexelSize));
}

float GetScaleHeight(float altitude, float scale_height) {
    return exp(-altitude / scale_height);
}

// See [Hillaire15] Physically Based and Unified Volumetric Rendering in Frostbite
float4 IntegrateWithTransmittance(float4 SampleExtinction, float4 SampleTransmittance, float4 ToIntegrate)
{
    return (ToIntegrate - ToIntegrate * SampleTransmittance) / SampleExtinction;
}

float RayleighPhase(float mu)
{
    return 3.0 / (16.0 * PI) * (1.0 + mu * mu);
}

static const float3x3 XyzToSRGB = float3x3( 
    3.2406, -1.5372, -0.4986,
    -0.9689, 1.8758, 0.0415,
    0.0557, -0.2040, 1.0570
);

float xFit_1931( float wave )
{
    float t1 = (wave-442.0f)*((wave<442.0f)?0.0624f:0.0374f);
    float t2 = (wave-599.8f)*((wave<599.8f)?0.0264f:0.0323f);
    float t3 = (wave-501.1f)*((wave<501.1f)?0.0490f:0.0382f);
    return 0.362f*exp(-0.5f*t1*t1) + 1.056f*exp(-0.5f*t2*t2)
    - 0.065f*exp(-0.5f*t3*t3);
}

float yFit_1931( float wave )
{
    float t1 = (wave-568.8f)*((wave<568.8f)?0.0213f:0.0247f);
    float t2 = (wave-530.9f)*((wave<530.9f)?0.0613f:0.0322f);
    return 0.821f*exp(-0.5f*t1*t1) + 0.286f*exp(-0.5f*t2*t2);
}

float zFit_1931( float wave )
{
    float t1 = (wave-437.0f)*((wave<437.0f)?0.0845f:0.0278f);
    float t2 = (wave-459.0f)*((wave<459.0f)?0.0385f:0.0725f);
    return 1.217f*exp(-0.5f*t1*t1) + 0.681f*exp(-0.5f*t2*t2);
}

// Convert a unit intensity of specific wavelength to intensity in standard RGB.
float3 WavelengthConvertToSRGB(float WavelengthNM)
{
    float3 XYZ;
    XYZ.x = xFit_1931(WavelengthNM);
    XYZ.y = yFit_1931(WavelengthNM);
    XYZ.z = zFit_1931(WavelengthNM);
    return mul(XyzToSRGB, XYZ);
}

void GetThetaPhiFromDirection(float3 direction, out float theta, out float phi)
{
    theta = FastAtan2(direction.y, direction.x);
    phi = FastACos(direction.z);
}

void GetThetaPhiOfDirectionInSystem(float3 WorldDir, float3x3 System, out float theta, out float phi)
{
    float x = dot(WorldDir, System[0]);
    float y = dot(WorldDir, System[1]);
    float z = dot(WorldDir, System[2]);

    GetThetaPhiFromDirection(float3(x, y, z), theta, phi);
}

float3 GetDirectionFromThetaPhi(float theta, float phi)
{
    float sintheta, costheta;
    sincos(theta, sintheta, costheta);
    float sinphi, cosphi;
    sincos(phi, sinphi, cosphi);
    return float3(sinphi * costheta, sinphi * sintheta, cosphi);
}

float3 GetWorldDirectionFromThetaPhi(float3x3 System, float theta, float phi)
{
    float3 Dir = GetDirectionFromThetaPhi(theta, phi);
    return System[0] * Dir.x + System[1] * Dir.y + System[2] * Dir.z;
}

/***************************************
 * Atmosphere Properties.
 ***************************************/
float2 AtmosphereThicknessAndInv;
float GroundHeight;
float AtmosphereHeight; // AtmosphereThicknessAndInv.x + GroundHeight
float3 AtmosphereLightDirection;
float CosSunDiscHalfAngle;
float SunDiscHalfAngle;
float3 SunCenterSRGBRadiance;
float3 SunSRGBIrradiance;
float CaptureHeight;
float PlanetBoundaryLayerAltitude;
float TopCloudAltitude;

float ClampRadius(float r) {
    return clamp(r, GroundHeight, AtmosphereHeight);
}

bool RayIntersectsLayerAtHeight(float r, float mu, float LayerHeight, out float d_1, out float d_2)
{
    d_1 = 0.0;
    d_2 = 0.0;
    float discriminant = 4 * r * r * (mu * mu - 1.0) +
        4 * LayerHeight * LayerHeight;
    if (discriminant >= 0.0f) {
        float sqDis = sqrt(discriminant);
        d_1 = (-2.0f * r * mu - sqDis) / 2.0f;
        d_2 = (-2.0f * r * mu + sqDis) / 2.0f;
    }
    return discriminant >= 0.0;
}

bool RayIntersectsGround(float r, float mu, out float d_1, out float d_2)
{
    return mu < 0.0 && RayIntersectsLayerAtHeight(r, mu, GroundHeight, d_1, d_2);
}

bool RayIntersectsGround(float r, float mu)
{
    return mu < 0.0 && r * r * (mu * mu - 1.0) + GroundHeight * GroundHeight >= 0.0;
}

float DistanceToLayerAtHeight(float r, float mu, float LayerHeight) {
    float discriminant = r * r * (mu * mu - 1.0) +
        LayerHeight * LayerHeight;
    return ClampDistance(-r * mu + SafeSqrt(discriminant));
}

float DistanceToTopAtmosphereBoundary(float r, float mu) {
    return DistanceToLayerAtHeight(r, mu, AtmosphereHeight);
}

float DistanceToBottomAtmosphereBoundary(float r, float mu)
{
    float discriminant = r * r * (mu * mu - 1.0) +
        GroundHeight * GroundHeight;
    return ClampDistance(-r * mu - SafeSqrt(discriminant));
}

float ZenithHorizonAngle(float r)
{
    // Return at altitude r, the angle between zenith and horizon.
    return acos(GroundHeight / r) + PI / 2.0f;
}

float3 WorldPositionToAtmospherePosition(float3 UnityWorldPosition)
{
    return UnityWorldPosition * 1e-3 + float3(0.0, GroundHeight, 0.0);
}

bool ResolveRaymarchPath(float OriginHeight, float viewMu, out float IntegrateDistance)
{
    float2 GroundIntersection;
    const bool bGroundIntersected = RayIntersectsGround(OriginHeight, viewMu, GroundIntersection.x, GroundIntersection.y); 
    if (bGroundIntersected)
    {
        IntegrateDistance = GroundIntersection.x;
    }
    else
    {
        IntegrateDistance = DistanceToTopAtmosphereBoundary(OriginHeight, viewMu);
    }

    return bGroundIntersected;
}

/***************************************
 * Iterating Wavelength Parameters.
 ***************************************/

#ifndef SRGB_SAMPLE_MDOE
#define SRGB_SAMPLE_MDOE 0
#endif

#if SRGB_SAMPLE_MDOE
    // still use float4, float3 will be aligned to 16bytes so really not meaningful..
    #define SPECTRUM_UNIFORM_PARAMETER(MemberName) uniform float4 SRGB_##MemberName
    #define GET_SPECTRUM_UNIFORM_VALUE(MemberName) SRGB_##MemberName 
#else
    #define SPECTRUM_UNIFORM_PARAMETER(MemberName) uniform float4 MemberName
    #define GET_SPECTRUM_UNIFORM_VALUE(MemberName) MemberName
#endif

#if SRGB_SAMPLE_MDOE
    #define SPECTRUM_SAMPLE float3 
#else
    #define SPECTRUM_SAMPLE float4 
#endif

#if !SRGB_SAMPLE_MDOE
    int CurrentIteratingFirstWavelengthIndex;
    int NumWavelengths;
    float4 CurrentIteratingWavelengthUM;
    float4 CurrentIteratingWavelengthUMInv;
    float4 CurrentIteratingSunIrradiance;
    float4 CurrentIteratingSunRadiance;
    float4 GroundSpectrumAlbedo;
    float4x4 NormalizedWavelengthRGBWeight;  // Store RGB weight for each wavelength.
    float4x4 WavelengthToSRGB;  // Translate result of current 4 wavelenght to SRGB.
    float dWaveLength;

    float3 WavelengthToXYZ(float4 SpectrumResult)
    {
        float3 Result = 0.0f;
        
        UNITY_UNROLL
        for (int i = 0; i < 4; i++)
        {
            if (CurrentIteratingWavelengthUM[i] == 0.0f)
                continue;
            float3 XYZ;
            XYZ.x = xFit_1931(1e3 * CurrentIteratingWavelengthUM[i]);
            XYZ.y = yFit_1931(1e3 * CurrentIteratingWavelengthUM[i]);
            XYZ.z = zFit_1931(1e3 * CurrentIteratingWavelengthUM[i]);
            Result += SpectrumResult[i] * XYZ;
        }

        return Result;
    }

    float3 ColorConvertToSRGB(float4 SpectrumResult)
    {
        return mul(XyzToSRGB, WavelengthToXYZ(SpectrumResult));
    }
#endif

/***************************************
 * Rayleigh Properties.
 ***************************************/

// Ext of current iterating 4 spectrum.
SPECTRUM_UNIFORM_PARAMETER(RayleighSeaLevelSpectrumScatteringCoefficient);
SPECTRUM_UNIFORM_PARAMETER(RayleighSpectrumPhaseFunctionGamma);
float RayleighScaleHeight;

SPECTRUM_SAMPLE RayleighPhaseAlt(float mu)
{
    return 3.0 / (16.0 * PI * (1 + 2 * GET_SPECTRUM_UNIFORM_VALUE(RayleighSpectrumPhaseFunctionGamma))) * ((1 + 3.0 * GET_SPECTRUM_UNIFORM_VALUE(RayleighSpectrumPhaseFunctionGamma)) + (1 - GET_SPECTRUM_UNIFORM_VALUE(RayleighSpectrumPhaseFunctionGamma)) * mu * mu);
}

SPECTRUM_SAMPLE SampleRayleigh(float Height)
{
    return GET_SPECTRUM_UNIFORM_VALUE(RayleighSeaLevelSpectrumScatteringCoefficient) * GetScaleHeight(Height - GroundHeight, RayleighScaleHeight);
}

/***************************************
 * Mie Properties.
 ***************************************/
Texture2D<float4> MieProperties;
Texture2D<float4> MieWavelengthLut;
int NumMieTypes;
#define PLANET_BOUNDARY_LAYER_FADE_HEIGHT 1.5

#include "JEPhaseFunction.cginc"

void LoadMieProperty(int TypeIndex, out float GeometryCrossSection, out float HeightProfileInfo, out float ScaleHeight, out float4 JEPhaseParams)
{
    float4 Result = MieProperties.Load(uint3(TypeIndex, 0, 0));
    GeometryCrossSection = Result.x;
    HeightProfileInfo = Result.y;
    ScaleHeight = Result.w;

    JEPhaseParams = MieProperties.Load(uint3(TypeIndex, 1, 0));
}

void LoadMieEfficiencies(int wavelengthIndex, int mieTypeIndex, out float scatEfficiency, out float extEfficiency, out float g)
{
    const int WavelengthIndex = min(0x7FFFFFFF, wavelengthIndex);   // rcying: Seems to be a driver/compiler bug, causing indexing into garbage data if no clamping here.
    float4 Sample = MieWavelengthLut.Load(uint3(mieTypeIndex, WavelengthIndex, 0));
    extEfficiency = Sample.x;
    scatEfficiency = Sample.y;
    g = Sample.z;
}

// Get phase function for Mie scattering. (batch as 4).
float4 SimpleMiePhaseFunction(float4 g, float VoL)
{
    // Common HG function. 
    float4 k = 3.0 / (8.0 * PI) * (1.0 - g * g) / (2.0 + g * g);
    return k * (1.0 + VoL * VoL) / pow(abs(1.0 + g * g - 2.0 * g * VoL), 1.5);
}

// Get phase function for 
float GetAerosolHeightProfile(float SampleHeight, float HeightProfileInfo, float ScaleHeight)
{
    float Altitude = SampleHeight - GroundHeight;

    if (HeightProfileInfo == 0.0f)
    {
        // The height distribution model from BAMS98 has a step at PBL boundary.
        // We need to do a smooth transition near boundary to avoid artifacts.
        const float FadeMask = saturate(((Altitude - PlanetBoundaryLayerAltitude) / PLANET_BOUNDARY_LAYER_FADE_HEIGHT + 1.0f) * 0.5f);
        
        return GetScaleHeight(Altitude - PlanetBoundaryLayerAltitude, ScaleHeight) * (1.0f - FadeMask);
    }
    else
    {
        return 0.0f;
    }
}

#define JE_PHASE_FUNCTION 1

void IntegrateMieParticles(float SampleHeight, inout float4 TotalScattering, inout float4 TotalMieExtinction, inout float4 MieLuminance, float VoL)
{
    for (int iMie = 0; iMie < NumMieTypes; iMie++)
    {
        float GeometryCrossSection;
        float HeightProfileInfo, ScaleHeight;
        float4 JEPhaseParams;
        LoadMieProperty(iMie, GeometryCrossSection, HeightProfileInfo, ScaleHeight, JEPhaseParams);
        const float ConcentrationScale = GetAerosolHeightProfile(SampleHeight, HeightProfileInfo, ScaleHeight);
        if (GeometryCrossSection <= 0.0f || ConcentrationScale <= 0.0f)
            continue;
        GeometryCrossSection *= ConcentrationScale;

        float4 CurrentTypeScattering;
        float4 CurrentTypeExtinction;
        float4 gs;
        UNITY_UNROLL
        for (int i = 0; i < 4; i++)
        {
            float scatEfficiency, extEfficiency, g;
            LoadMieEfficiencies(CurrentIteratingFirstWavelengthIndex + i, iMie, scatEfficiency, extEfficiency, g);
            CurrentTypeScattering[i] = scatEfficiency * GeometryCrossSection;
            CurrentTypeExtinction[i] = extEfficiency * GeometryCrossSection;
            gs[i] = g;
        }
        TotalScattering += CurrentTypeScattering;
        TotalMieExtinction += CurrentTypeExtinction;

        // Apply phase for optional output.
        float4 MiePhase;
        if (JE_PHASE_FUNCTION && any(JEPhaseParams) != 0.0f)
        {
            MiePhase = JEPhaseMie(JEPhaseParams, VoL).xxxx;
        }
        else
        {
            MiePhase = SimpleMiePhaseFunction(gs, VoL);
        }
        MieLuminance += CurrentTypeScattering * MiePhase;
    }
}

void IntegrateMieParticles(float SampleHeight, inout float4 TotalScattering, inout float4 TotalMieExtinction)
{
    float4 MieLuminance;
    IntegrateMieParticles(SampleHeight, TotalScattering, TotalMieExtinction, MieLuminance, 0.0f);
}


/***************************************
 * OZone Properties.
 ***************************************/
float OZoneLowerDensity;
float OZoneStratosphereMidDensity;
float OZoneStratosphereTopDensity;
float OZoneUpperHeightScale;
SPECTRUM_UNIFORM_PARAMETER(OZoneAbsorptionCrossSection);

SPECTRUM_SAMPLE SampleOZoneAbsorptionCoefficients(float SampleHeight)
{
    const float Altitude = SampleHeight - GroundHeight;
    float Density = 0.0f;
    if (Altitude < 10.0)
    {
        Density = OZoneLowerDensity;
    }
    else if (Altitude < 20.0)
    {
        Density = lerp(OZoneLowerDensity, OZoneStratosphereMidDensity, (Altitude - 10.0) / 10.0);
    }
    else if (Altitude < 35.0)
    {
         Density = lerp(OZoneStratosphereMidDensity, OZoneStratosphereTopDensity, (Altitude - 20.0) / 15.0);
    }
    else
    {
        Density = OZoneStratosphereTopDensity * GetScaleHeight(Altitude - 35.0f, OZoneUpperHeightScale);
    }

    return Density * OZoneAbsorptionCrossSection * 1e3f; // Convert to km^-1
}


/***************************************
 * Cloud Properties.
 ***************************************/
float CloudExtinctionCoefficient;   // Assume cloud albedo is purely white, only extinction required.
float4 CloudPhaseParams;
float CloudAltitude;
