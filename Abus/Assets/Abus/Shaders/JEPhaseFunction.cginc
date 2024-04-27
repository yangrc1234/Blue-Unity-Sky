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

// Proposed approximation ("Jendersie-d'Eon phase function"?).
// NOTE: this reduces to pure Henyey-Greenstein for phase_mie(float4(g,0,0,0),c).
float JEPhaseMie(float4 M, float x)
{
    return lerp(phase_draine(0.0, M.x, x), phase_draine(M.z, M.y, x), M.w);
}