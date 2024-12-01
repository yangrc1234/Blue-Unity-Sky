Shader "Hidden/AerialPerspectiveOnOpaque"
{
    HLSLINCLUDE
    
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        // The Blit.hlsl file provides the vertex shader (Vert),
        // the input structure (Attributes), and the output structure (Varyings)
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    
    ENDHLSL
    
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline"}
        LOD 100
        ZTest Always ZWrite Off Cull Off Blend One SrcAlpha,Zero One
        Pass
        {
            Name "ApplyAerialPerspective"

            HLSLPROGRAM
            // This line defines the name of the vertex shader.
            #pragma vertex Vert
            // This line defines the name of the fragment shader.
            #pragma fragment frag

            // The Core.hlsl file contains definitions of frequently used HLSL
            // macros and functions, and also contains #include references to other
            // HLSL files (for example, Common.hlsl, SpaceTransforms.hlsl, etc.).
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            // The DeclareDepthTexture.hlsl file contains utilities for sampling the
            // Camera depth texture.
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            #define AERIAL_PERSPECTIVE_SRGB_READABLE 1
            #include "LutCommon.cginc"
            #include "AtmosphereCommon.cginc"
        
            // The fragment shader definition.
            // The Varyings input structure contains interpolated values from the
            // vertex shader. The fragment shader uses the `positionHCS` property
            // from the `Varyings` struct to get locations of pixels.
            half4 frag(Varyings IN) : SV_Target
            {
                // To calculate the UV coordinates for sampling the depth buffer,
                // divide the pixel location by the render target resolution
                // _ScaledScreenParams.
                float2 UV = IN.positionCS.xy / _ScaledScreenParams.xy;

                // Sample the depth from the Camera depth texture.
                float depth = SampleSceneDepth(UV);

                // Reconstruct the world space positions.
                float3 worldPos = ComputeWorldSpacePosition(UV, depth, UNITY_MATRIX_I_VP);

                float3 RelativeWorldPositionKM = 1e-3f * (worldPos - _WorldSpaceCameraPos);
                const float3 UVW = GetAerialPerspectiveLutUVW(RelativeWorldPositionKM);
                const float4 Sample = SAMPLE_TEXTURE3D(AerialPerspectiveSRGBLut, sampler_AerialPerspectiveSRGBLut, UVW);
                return Sample;
            }
            ENDHLSL
        }
    }
    FallBack "Diffuse"
}