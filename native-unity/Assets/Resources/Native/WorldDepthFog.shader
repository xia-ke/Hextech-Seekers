Shader "HextechSeekers/WorldDepthFog"
{
    Properties
    {
        _FogMask ("Private Rift Fog Mask", 2D) = "white" {}
        _FogTint ("Fog Color", Color) = (0.025, 0.042, 0.07, 1.0)
        _FogParams ("Explored, Unknown, Outside Alpha", Vector) = (0.23, 0.49, 0.08, 0)
        _MapMin ("Rift Origin XZ", Vector) = (0,0,0,0)
        _MapSpan ("Rift Size XZ", Vector) = (1,1,0,0)
    }
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Overlay"
        }
        Pass
        {
            Name "PrivateFogOverlay"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Off
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex FogVertex
            #pragma fragment FogFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_FogMask);
            SAMPLER(sampler_FogMask);

            CBUFFER_START(UnityPerMaterial)
                float4 _FogTint;
                float4 _FogParams;
                float4 _MapMin;
                float4 _MapSpan;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings FogVertex(Attributes input)
            {
                Varyings o;
                // A deliberately full-screen mesh. The vertex shader writes
                // clip coordinates directly, independent of camera roll/pitch.
                o.positionCS = float4(input.positionOS.xy, UNITY_NEAR_CLIP_VALUE, 1);
                return o;
            }

            half4 FogFragment(Varyings input) : SV_Target
            {
                float2 screenUV = input.positionCS.xy / _ScaledScreenParams.xy;
                real rawDepth = SampleSceneDepth(screenUV);

                // Unrendered background is not visible ground.
                #if UNITY_REVERSED_Z
                    if (rawDepth <= 0.00001)
                        return half4(_FogTint.rgb, 0);
                #else
                    if (rawDepth >= 0.99999)
                        return half4(_FogTint.rgb, 0);
                #endif

                float3 world = ComputeWorldSpacePosition(
                    screenUV, rawDepth, UNITY_MATRIX_I_VP);
                float2 uv = (world.xz - _MapMin.xy) / _MapSpan.xy;
                if (any(uv < 0) || any(uv >= 1))
                    return half4(_FogTint.rgb, _FogParams.z);

                // R8-equivalent encoded in RGBA32 to support the widest
                // range of Windows D3D11 hardware and Unity GPU formats.
                half fog = SAMPLE_TEXTURE2D(_FogMask, sampler_FogMask, uv).r;
                // Visible=0, explored=170/255, unknown=238/255.
                // The previous fog*.96 yielded 64%/90% dark overlays and
                // forced the background to 94%, obscuring the whole view.
                // Use three distinct visual strengths, with smooth edges.
                // Tactical visibility and private masks stay unchanged.
                half exploredBlend = smoothstep(.02, .65, fog);
                half unknownBlend = smoothstep(.68, .90, fog);
                half opacity = lerp(0, _FogParams.x, exploredBlend);
                opacity = lerp(opacity, _FogParams.y, unknownBlend);
                return half4(_FogTint.rgb, saturate(opacity));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
