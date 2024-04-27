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

float4 RayleighPhase(float mu)
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

/***************************************
 * Atmosphere Properties.
 ***************************************/
float2 AtmosphereThicknessAndInv;
float GroundHeight;
float AtmosphereHeight; // AtmosphereThicknessAndInv.x + GroundHeight
float3 AtmosphereLightDirection;
float CosSunDiscHalfAngle;
float SunDiscHalfAngle;
float3 SunCenterSrgbRadiance;

float ClampRadius(float r) {
    return clamp(r, GroundHeight, AtmosphereHeight);
}

bool RayIntersectsGround(float r, float mu, out float d_1, out float d_2)
{
    d_1 = 0.0;
    d_2 = 0.0;
    float discriminant = 4 * r * r * (mu * mu - 1.0) +
        4 * GroundHeight * GroundHeight;
    if (discriminant >= 0.0f) {
        float sqDis = sqrt(discriminant);
        d_1 = (-2.0f * r * mu - sqDis) / 2.0f;
        d_2 = (-2.0f * r * mu + sqDis) / 2.0f;
    }
    return mu < 0.0 && discriminant >= 0.0;
}

bool RayIntersectsGround(float r, float mu)
{
    return mu < 0.0 && r * r * (mu * mu - 1.0) + GroundHeight * GroundHeight >= 0.0;
}

float DistanceToTopAtmosphereBoundary(float r, float mu) {
    float discriminant = r * r * (mu * mu - 1.0) +
        AtmosphereHeight * AtmosphereHeight;
    return ClampDistance(-r * mu + SafeSqrt(discriminant));
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
int CurrentIteratingFirstWavelengthIndex;
int NumWavelengths;
float4 CurrentIteratingWavelengthUM;
float4 CurrentIteratingWavelengthUMInv;
float4 CurrentIteratingSunIrradiance;
float4 CurrentIteratingSunRadiance;
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
        Result += SpectrumResult[i] * max(0.0f, XYZ);
    }

    return Result;
}

float3 ColorConvertToSRGB(float4 SpectrumResult)
{
    return max(0.0f, mul(XyzToSRGB, WavelengthToXYZ(SpectrumResult)));
}

/***************************************
 * Ground Properties.
 ***************************************/
float4 GroundSpectrumAlbedo;

/***************************************
 * Rayleigh Properties.
 ***************************************/

// Ext of current iterating 4 spectrum.
float4 RayleighSeaLevelSpectrumScatteringCoefficient;
float4 RayleighSpectrumPhaseFunctionGamma;
float RayleighScaleHeight;

float4 RayleighPhaseAlt(float mu)
{
    return 3.0 / (16.0 * PI * (1 + 2 * RayleighSpectrumPhaseFunctionGamma)) * ((1 + 3.0 * RayleighSpectrumPhaseFunctionGamma) + (1 - RayleighSpectrumPhaseFunctionGamma) * mu * mu);
}

float4 SampleRayleigh(float Height)
{
    return RayleighSeaLevelSpectrumScatteringCoefficient * GetScaleHeight(Height - GroundHeight, RayleighScaleHeight);
}

/***************************************
 * Mie Properties.
 ***************************************/
Texture2D<float4> MieProperties;
Texture2D<float4> MieWavelengthLut;
int NumMieTypes;

#include "JEPhaseFunction.cginc"

void LoadMieProperty(int TypeIndex, out float GeometryCrossSection, out float PBLDensity, out float Thickness, out float ScaleHeight, out float4 JEPhaseParams)
{
    float4 Result = MieProperties.Load(uint3(TypeIndex, 0, 0));
    GeometryCrossSection = Result.x;
    PBLDensity = Result.y;
    Thickness = Result.z;
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
float GetAerosolHeightProfile(float SampleHeight, float PBLDensity, float Thickness, float ScaleHeight)
{
    float Altitude = SampleHeight - GroundHeight;

    // The height distribution model from BAMS98 has a step at PBL boundary.
    // We need to do a smooth transition near boundary to avoid artifacts.

    const float FadeMask = saturate((Altitude - Thickness) / 2.0f);
    
    return PBLDensity * GetScaleHeight(Altitude - Thickness, ScaleHeight) * (1.0f - FadeMask);
}

#define JE_PHASE_FUNCTION 1

void IntegrateMieParticles(float SampleHeight, inout float4 TotalScattering, inout float4 TotalMieExtinction, inout float4 MieLuminance, float VoL)
{
    for (int iMie = 0; iMie < NumMieTypes; iMie++)
    {
        float GeometryCrossSection;
        float PBLDensity, Thickness, ScaleHeight;
        float4 JEPhaseParams;
        LoadMieProperty(iMie, GeometryCrossSection, PBLDensity, Thickness, ScaleHeight, JEPhaseParams);
        const float ConcentrationScale = GetAerosolHeightProfile(SampleHeight, PBLDensity, Thickness, ScaleHeight);
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
float4 OZoneAbsorptionCrossSection;

float4 SampleOZoneAbsorptionCoefficients(float SampleHeight)
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