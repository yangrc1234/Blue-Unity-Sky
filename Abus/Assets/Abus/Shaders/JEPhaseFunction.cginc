/*
 * Phase function for approximate Mie phase function.
 * From Johannes Jendersie and Eugene d'Eon. 2023. An Approximate Mie Scattering Function for Fog and Cloud Rendering. In ACM SIGGRAPH 2023 Talks (SIGGRAPH '23). Association for Computing Machinery, New York, NY, USA, Article 47, 1–2. https://doi.org/10.1145/3587421.3595409
 *     https://research.nvidia.com/labs/rtr/approximate-mie/publications/approximate-mie.pdf
 *
 * The code is from https://www.shadertoy.com/view/4XsXRn. Thanks to FordPerfect!
 */

#pragma once

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

float phase_draine(float a, float g, float x)
{
    float d = 1.0 + g * g - 2.0 * g * x;
    return 1.0 / (4.0 * PI) * (1.0 - g * g) / (1.0 + a * (1.0 + 2.0 * g * g) / 3.0) * // <-- constant factor.
        (1.0 + a * x * x) / (d * sqrt(d));
}

// Parametrization by droplet size.
// Input: droplet size, in micrometres.
// Valid range: 5<d<50.
float4 phase_params_mie(float d)
{
    return float4(
        exp(-0.0990567 / (d - 1.67154)), // gHG
        exp(-2.20679 / (d + 3.91029) - 0.428934), // gD
        exp(3.62489 - 8.29288 / (d + 5.52825)), // alpha
        exp(-0.599085 / (d - 0.641583) - 0.665888) // wD
    );
}

// Proposed approximation ("Jendersie-d'Eon phase function"?).
// NOTE: this reduces to pure Henyey-Greenstein for phase_mie(float4(g,0,0,0),c).
float phase_mie(float4 M, float x)
{
    return lerp(phase_draine(0.0, M.x, x), phase_draine(M.z, M.y, x), M.w);
}

// NOTE: unlike Henyey-Greenstein, the asymmetry parameter (defined
// as <cos(θ)> = 2*π * ∫ p(θ)*cos(θ)*sin(θ) dθ on [0;π]) for Draine
// phase function is not simply g, but g*(1+a*(3+2*g^2)/5)/(1+a*(1+2*g^2)/3).
float asymmetry_draine(float a, float g)
{
    return g * (1.0 + a * (3.0 + 2.0 * g * g) / 5.0) / (1.0 + a * (1.0 + 2.0 * g * g) / 3.0);
}

float asymmetry_mie(float4 M)
{
    return lerp(M.x, asymmetry_draine(M.z, M.y), M.w);
}
