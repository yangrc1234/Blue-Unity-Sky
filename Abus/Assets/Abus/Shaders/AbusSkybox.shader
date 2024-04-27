Shader "Unlit/AbusSampleSkybox"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "QUEUE"="Background" "RenderType"="Background" "RenderPipeline" = "UniversalRenderPipeline"}
        LOD 100

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            
            #define SKYVIEW_SRGB_READABLE 1
            #define SUNFOCUS_READABLE 1
            #define TRANSMITTANCE_SRGB_READABLE 1
            
            #include "LutCommon.cginc"
            #include "AtmosphereCommon.cginc"
            
            struct appdata
            {
                float4 positionOS   : POSITION;      
            };

            struct v2f
            {
                float4 positionHCS  : SV_POSITION;
				float3 worldPos : TEXCOORD1;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.positionHCS = TransformObjectToHClip(v.positionOS.xyz);
				o.worldPos = TransformObjectToWorld(v.positionOS.xyz);
                return o;
            }
            
            float4 frag(v2f i) : SV_Target
            {
                float4 Color = 0.0f;
                // sample the texture
				float3 ViewDir = normalize(i.worldPos.xyz - _WorldSpaceCameraPos);

                {
                    // Sun disc.
				    float3 LightDir = normalize(AtmosphereLightDirection.xyz);
                    float3 PlanetPos = _WorldSpaceCameraPos * 1e-3f + float3(0.0f, GroundHeight, 0.0f);
                    float R = length(PlanetPos);
                    
                    float viewMu = dot(PlanetPos / R, ViewDir);

                    const float mu = dot(ViewDir, LightDir);

                    if (mu > CosSunDiscHalfAngle && !RayIntersectsGround(R, viewMu))
                    {
                        const float3 Transmittance = GetSRGBTransmittance(R, viewMu);
                        Color.rgb += Transmittance * SunCenterSrgbRadiance;

                        const float LimbDarkening = (1.0f - 0.6f * (1.0f - sqrt(1.0 - POW2(FastACos(mu) / SunDiscHalfAngle))));
                        Color.rgb *= LimbDarkening;
                    }
                }

                Color.rgb += GetSrgbSkyView(ViewDir).rgb;
                
            	return float4(Color.rgb, 1.0f);
            }
            ENDHLSL
        }
    }
}
