#pragma once

#include "AtmosphereCommon.cginc"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

#ifndef TRANSMITTANCE_READABLE
#define TRANSMITTANCE_READABLE 0
#endif

#ifndef MULTIPLE_SCATTERING_READABLE
#define MULTIPLE_SCATTERING_READABLE 0
#endif

#ifndef SKYVIEW_SRGB_READABLE
#define SKYVIEW_SRGB_READABLE 0
#endif


#define POW2(x) ((x) * (x))

/*****************************
 * Transmittance Texture
 *****************************/
float4 TransmittanceTextureSizeInvSize;

float2 GetTransmittanceTextureUvFromRMu(float r, float cosViewZenith)
{
	// Distance to top atmosphere boundary for a horizontal ray at ground level.
	float H = sqrt(AtmosphereHeight * AtmosphereHeight -
		GroundHeight * GroundHeight);
	// Distance to the horizon.
	float rho =
		SafeSqrt(r * r - GroundHeight * GroundHeight);
	// Distance to the top atmosphere boundary for the ray (r,mu), and its minimum
	// and maximum values over all mu - obtained for (r,1) and (r,mu_horizon).
	float d = DistanceToTopAtmosphereBoundary(r, cosViewZenith);
	float d_min = AtmosphereHeight - r;
	float d_max = rho + H;
	float x_mu = (d - d_min) / (d_max - d_min);
	float x_r = rho / H;
	return float2(GetTextureCoordFromUnitRange(x_mu, TransmittanceTextureSizeInvSize.z),
		GetTextureCoordFromUnitRange(x_r, TransmittanceTextureSizeInvSize.w));
}

void GetRMuFromTransmittanceTextureUv(float2 uv, out float r, out float cosViewZenith)
{
	float x_mu = GetUnitRangeFromTextureCoord(uv.x, TransmittanceTextureSizeInvSize.z);
	float x_r = GetUnitRangeFromTextureCoord(uv.y, TransmittanceTextureSizeInvSize.w);
	// Distance to top atmosphere boundary for a horizontal ray at ground level.
	float H = sqrt(AtmosphereHeight * AtmosphereHeight -
		GroundHeight * GroundHeight);
	// Distance to the horizon, from which we can compute r:
	float rho = H * x_r;
	r = sqrt(rho * rho + GroundHeight * GroundHeight);
	// Distance to the top atmosphere boundary for the ray (r,mu), and its minimum
	// and maximum values over all mu - obtained for (r,1) and (r,mu_horizon) -
	// from which we can recover mu:
	float d_min = AtmosphereHeight - r;
	float d_max = rho + H;
	float d = d_min + x_mu * (d_max - d_min);
	cosViewZenith = d == 0.0 ? float(1.0) : (H * H - rho * rho - d * d) / (2.0 * r * d);
	cosViewZenith = ClampCosine(cosViewZenith);
}

#if TRANSMITTANCE_READABLE
Texture2D<float4> TransmittanceTexture;
SamplerState sampler_TransmittanceTexture;
float4 GetTransmittance(float r, float cosViewZenith)
{
	return TransmittanceTexture.SampleLevel(sampler_TransmittanceTexture, GetTransmittanceTextureUvFromRMu(r, cosViewZenith), 0.0f);
}

float4 GetTransmittance(float r, float mu, float d, bool ray_r_mu_intersects_ground)
{
	float r_d = ClampRadius(sqrt(d * d + 2.0 * r * mu * d + r * r));
	float mu_d = ClampCosine((r * mu + d) / r_d);

	if (ray_r_mu_intersects_ground)
	{
		return min(GetTransmittance(r_d, -mu_d) / GetTransmittance(r, -mu), 1.0);
	}
	else
	{
		return min(GetTransmittance(r, mu) / GetTransmittance(r_d, mu_d), 1.0f);
	}
}

#endif

#if TRANSMITTANCE_SRGB_READABLE
Texture2D<float4> SRGBTransmittanceTexture;
SamplerState sampler_SRGBTransmittanceTexture;
float3 GetSRGBTransmittance(float r, float cosViewZenith)
{
	return SRGBTransmittanceTexture.SampleLevel(sampler_SRGBTransmittanceTexture, GetTransmittanceTextureUvFromRMu(r, cosViewZenith), 0.0f);
}

#endif

/*****************************
 * Multiple Scattering Texture
 *****************************/
float4 MultipleScatteringTextureSizeInvSize;
float2 GetMultipleScatteringTextureUvFromRMu(float r, float cosSunZenith)
{
	float x_cosSunZenith = GetTextureCoordFromUnitRange(cosSunZenith * 0.5f + 0.5f, MultipleScatteringTextureSizeInvSize.z);
	float x_r = GetTextureCoordFromUnitRange((r - GroundHeight) * AtmosphereThicknessAndInv.y, MultipleScatteringTextureSizeInvSize.w);
	return float2(x_cosSunZenith, x_r);
}

void GetRMuFromMultipleScatteringTextureUv(float2 uv, out float r, out float cosSunZenith)
{
	cosSunZenith = (GetUnitRangeFromTextureCoord(uv.x, MultipleScatteringTextureSizeInvSize.z) - 0.5f) * 2.0f;
	r = GetUnitRangeFromTextureCoord(uv.y, MultipleScatteringTextureSizeInvSize.w) * AtmosphereThicknessAndInv.x + GroundHeight;
}

#if MULTIPLE_SCATTERING_READABLE
Texture2D<float4> MultipleScatteringTexture;
SamplerState sampler_MultipleScatteringTexture;
float4 GetMultipleScattering(float r, float cosSunZenith)
{
	return MultipleScatteringTexture.SampleLevel(sampler_MultipleScatteringTexture, GetMultipleScatteringTextureUvFromRMu(r, cosSunZenith), 0.0f);
}
#endif


/*****************************
 * Sky View Texture
 * 
 * A good mapping should be: 
 * 1. Need to be focused around the sun, mie scattering could be super high frequency.
 * 2. Pixels corresponding to horizon, must be aligned in texture space as well.
 * 3. We only consider 1 light source, make full use of this.
*****************************/

float4 SkyViewTextureSizeAndInvSize;
float3 SkyViewLutUpDir;

#define UPPER_PART_FRACTION 0.75	// How many pixels are for above-horizon

float3 GetSkyViewTextureUvToViewDir(float R, float2 uv)
{
	float3 Z = float3(0.0f, 1.0f, 0.0f);
	float3 X = normalize(cross(SkyViewLutUpDir, Z));
	float3 Y = cross(X, Z);

	float theta = POW2(uv.x) * PI;

	// Focus pixels around this height.
	const float sunPhi = acos(dot(float3(0.0f, 1.0f, 0.0f), SkyViewLutUpDir));

	const float horizonPhi = PI - acos(sqrt(R * R - GroundHeight * GroundHeight) / R);

	float sintheta, costheta;
	sincos(theta, sintheta, costheta);

	float phi;
	if (uv.y < UPPER_PART_FRACTION)
	{
		if (uv.y < UPPER_PART_FRACTION * 0.5f)
		{
			const float range = sunPhi;
			const float norm = uv.y / (UPPER_PART_FRACTION * 0.5f);
			phi = (1.0f - POW2(1.0f - norm)) * range;
		}
		else
		{
			const float range = horizonPhi - sunPhi;
			float norm = (uv.y - UPPER_PART_FRACTION * 0.5f) / (UPPER_PART_FRACTION * 0.5f);
			phi = sunPhi + POW2(norm) * range;
		}
	}
	else
	{
		const float norm = (uv.y - UPPER_PART_FRACTION) / (1.0f - UPPER_PART_FRACTION);
		phi = horizonPhi + norm * (PI - horizonPhi);
	}

	float sinphi, cosphi;
	sincos(phi, sinphi, cosphi);

	return Z * cosphi + sinphi * -costheta * Y + sinphi * sintheta * X;
}

float2 GetSkyViewTextureViewDirToUV(float R, float3 ViewDir)
{
	// Imagine T-pose looking at the sun,
	// Z is sun, Y is your head-up, X is your right arm
	float3 Z = float3(0.0f, 1.0f, 0.0f);
	float3 X = normalize(cross(SkyViewLutUpDir, Z));
	float3 Y = cross(X, Z);

	// Focus pixels around this height.
	const float sunPhi = FastACos(dot(float3(0.0f, 1.0f, 0.0f), SkyViewLutUpDir));
	
	const float horizonPhi = PI - acos(sqrt(R * R - GroundHeight * GroundHeight) / R);
	
	float phi = FastACos(dot(ViewDir, Z));

	float x = dot(ViewDir, X);
	float y = dot(ViewDir, -Y);
	float theta = FastAtan2(x, y);

	float U = sqrt(abs(theta / PI));
	float V;
	if (phi < horizonPhi)
	{
		// Above horizon.
		
		if (phi < sunPhi)
		{
			// Above sun
			float norm = (phi / sunPhi);
			norm = 1.0f - sqrt(1.0f - norm);
			V = norm * UPPER_PART_FRACTION * 0.5f;
		}
		else
		{
			// Below sun.
			const float range = horizonPhi - sunPhi;
			const float norm = sqrt((phi - sunPhi) / range); // 0 At sun, 1.0 at horizon.
			V = norm * UPPER_PART_FRACTION * 0.5f + UPPER_PART_FRACTION * 0.5f;
			
			// Avoid bilinear sampling with below part.
			V -= norm * SkyViewTextureSizeAndInvSize.w * 0.5f; 
		}
	}
	else
	{
		const float range = PI - horizonPhi;
		const float norm = (phi - horizonPhi) / range;
		V = norm * (1.0f - UPPER_PART_FRACTION) + UPPER_PART_FRACTION;

		// Avoid bilinear sampling with upper part.
		V += (SkyViewTextureSizeAndInvSize.w * 0.5f) * (1.0f - norm);
	}
	
	return float2(U, V);
}

#if SKYVIEW_SRGB_READABLE
Texture2D<float4> SrgbSkyViewTexture;
SamplerState sampler_SrgbSkyViewTexture;
float4 GetSrgbSkyView(float R, float3 viewDir)
{
	return SrgbSkyViewTexture.SampleLevel(sampler_SrgbSkyViewTexture, GetSkyViewTextureViewDirToUV(R, viewDir), 0.0f);
}
#endif


#if SKYVIEW_READABLE
Texture2D<float4> SkyViewTexture;
SamplerState sampler_SkyViewTexture;
float4 GetSkyView(float R, float3 viewDir)
{
	return SkyViewTexture.SampleLevel(sampler_SkyViewTexture, GetSkyViewTextureViewDirToUV(R, viewDir), 0.0f);
}
#endif

