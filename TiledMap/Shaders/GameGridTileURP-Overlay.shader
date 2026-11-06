    Shader "Custom/GameTile/URP-Overlay"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _OverlayTex ("Overlay Texture", 2D) = "white" {}
        _OverlayMask ("Overlay Mask", 2D) = "black" {}
        _OverlayScale ("Overlay Scale", Float) = .5
        _OverlayMix ("Overlay Mix Amount", Range(0, 1)) = .5
        [HideInInspector] _Color ("Tint", Color) = (1,1,1,1)
        [HideInInspector] PixelSnap ("Pixel snap", Float) = 0
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _AlphaTex ("External Alpha", 2D) = "white" {}
        [HideInInspector] _EnableExternalAlpha ("Enable External Alpha", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        Pass
        {
            Tags
            {
                "LightMode" = "Universal2D"
            }

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/DebugMipmapStreamingMacros.hlsl"
            #if defined(DEBUG_DISPLAY)
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/InputData2D.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/SurfaceData2D.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Debug/Debugging2D.hlsl"
            #endif

            #pragma vertex UnlitVertex
            #pragma fragment UnlitFragment
            #pragma multi_compile _ DEBUG_DISPLAY SKINNED_SPRITE

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_SKINNED_VERTEX_INPUTS
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                float3 worldPosition : TEXCOORD1;
                #if defined(DEBUG_DISPLAY)
                float3 positionWS : TEXCOORD2;
                #endif
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            UNITY_TEXTURE_STREAMING_DEBUG_VARS_FOR_TEX(_MainTex);

            TEXTURE2D(_OverlayTex);
            SAMPLER(sampler_OverlayTex);
            UNITY_TEXTURE_STREAMING_DEBUG_VARS_FOR_TEX(_OverlayTex);
            
            TEXTURE2D(_OverlayMask);
            SAMPLER(sampler_OverlayMask);
            UNITY_TEXTURE_STREAMING_DEBUG_VARS_FOR_TEX(_OverlayMask);

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _OverlayScale;
                float _OverlayMix;
            CBUFFER_END

            Varyings UnlitVertex(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                UNITY_SKINNED_VERTEX_COMPUTE(v);

                v.positionOS = UnityFlipSprite(v.positionOS, unity_SpriteProps.xy);
                o.positionCS = TransformObjectToHClip(v.positionOS);
                o.worldPosition = TransformObjectToWorld(v.positionOS);
                #if defined(DEBUG_DISPLAY)
                o.positionWS = TransformObjectToWorld(v.positionOS);
                #endif
                o.uv = v.uv;
                o.color = v.color * _Color * unity_SpriteColor;
                return o;
            }

            half4 UnlitFragment(Varyings i) : SV_Target
            {
                float4 mainTex = i.color * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                float4 mask = SAMPLE_TEXTURE2D(_OverlayMask, sampler_OverlayMask, i.uv);
                float4 overlayTex = SAMPLE_TEXTURE2D(_OverlayTex, sampler_OverlayTex, i.worldPosition * _OverlayScale);
                mainTex.rgb = lerp(mainTex.rgb, overlayTex.rgb, mask.r * _OverlayMix);
                #if defined(DEBUG_DISPLAY)
                SurfaceData2D surfaceData;
                InputData2D inputData;
                half4 debugColor = 0;

                InitializeSurfaceData(mainTex.rgb, mainTex.a, surfaceData);
                InitializeInputData(i.uv, inputData);
                SETUP_DEBUG_TEXTURE_DATA_2D_NO_TS(inputData, i.positionWS, i.positionCS, _MainTex);

                if (CanDebugOverrideOutputColor(surfaceData, inputData, debugColor))
                {
                    return debugColor;
                }
                #endif

                return mainTex;
            }
            ENDHLSL
        }

        Pass
        {
            Tags
            {
                "LightMode" = "UniversalForward" "Queue" = "Transparent" "RenderType" = "Transparent"
            }

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/DebugMipmapStreamingMacros.hlsl"
            #if defined(DEBUG_DISPLAY)
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/InputData2D.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/SurfaceData2D.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Debug/Debugging2D.hlsl"
            #endif

            #pragma vertex UnlitVertex
            #pragma fragment UnlitFragment
            #pragma multi_compile_fragment _ DEBUG_DISPLAY

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_SKINNED_VERTEX_INPUTS
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float3 worldPosition : TEXCOORD1;
                #if defined(DEBUG_DISPLAY)
                float3 positionWS : TEXCOORD2;
                #endif
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            UNITY_TEXTURE_STREAMING_DEBUG_VARS_FOR_TEX(_MainTex);

            TEXTURE2D(_OverlayTex);
            SAMPLER(sampler_OverlayTex);
            UNITY_TEXTURE_STREAMING_DEBUG_VARS_FOR_TEX(_OverlayTex);

            TEXTURE2D(_OverlayMask);
            SAMPLER(sampler_OverlayMask);
            UNITY_TEXTURE_STREAMING_DEBUG_VARS_FOR_TEX(_OverlayMask);
            
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _OverlayScale;
                float _OverlayMix;
            CBUFFER_END
            
            Varyings UnlitVertex(Attributes attributes)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(attributes);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                UNITY_SKINNED_VERTEX_COMPUTE(attributes);

                attributes.positionOS = UnityFlipSprite(attributes.positionOS, unity_SpriteProps.xy);
                o.positionCS = TransformObjectToHClip(attributes.positionOS);
                o.worldPosition = TransformObjectToWorld(attributes.positionOS);
                #if defined(DEBUG_DISPLAY)
                o.positionWS = TransformObjectToWorld(attributes.positionOS);
                #endif
                o.uv = attributes.uv;
                o.color = attributes.color * _Color * unity_SpriteColor;
                return o;
            }

            float4 UnlitFragment(Varyings i) : SV_Target
            {
                float4 mainTex = i.color * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                float4 mask = SAMPLE_TEXTURE2D(_OverlayMask, sampler_OverlayMask, i.uv);
                float4 overlayTex = SAMPLE_TEXTURE2D(_OverlayTex, sampler_OverlayTex, i.worldPosition * _OverlayScale);
                mainTex.rgb = lerp(mainTex.rgb, overlayTex.rgb, mask.r * _OverlayMix);
                #if defined(DEBUG_DISPLAY)
                SurfaceData2D surfaceData;
                InputData2D inputData;
                half4 debugColor = 0;

                InitializeSurfaceData(mainTex.rgb, mainTex.a, surfaceData);
                InitializeInputData(i.uv, inputData);
                SETUP_DEBUG_TEXTURE_DATA_2D_NO_TS(inputData, i.positionWS, i.positionCS, _MainTex);

                if (CanDebugOverrideOutputColor(surfaceData, inputData, debugColor))
                {
                    return debugColor;
                }
                #endif

                return mainTex;
            }
            ENDHLSL
        }
    }

    Fallback "Sprites/Default"
}
