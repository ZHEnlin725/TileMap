Shader "Custom/GroundWaterPuddleURP"
{
    Properties
    {
        _MainTex("Quad Mask (Alpha)", 2D) = "white" {}
        _NoiseTex("Puddle Noise (R)", 2D) = "gray" {}
        _SurfaceMask("Allowed Area Mask (R)", 2D) = "white" {}
        [NoScaleOffset] _EdgeNoiseTex("Edge Motion Noise (R)", 2D) = "gray" {}

        [Header(Puddle Shape)]
        _Coverage("Coverage", Range(0, 1)) = 0.5
        _EdgeSoftness("Edge Softness", Range(0, 0.25)) = 0.0
        _EdgeWidth("Edge Width", Range(0, 0.5)) = 0.05
        _EdgeFade("Edge Transparency Fade", Range(0, 1)) = 0.25
        _NoiseScale("Noise Scale", Float) = 0.25
        _NoiseOffset("Noise Offset", Vector) = (0, 0, 0, 0)
        _UseSurfaceMask("Use Allowed Area Mask", Float) = 0

        [Header(Edge Motion)]
        _EdgeNoiseScale("Edge Noise Scale", Float) = 0.4
        _EdgeNoiseSpeed("Edge Noise Speed", Vector) = (0.03, 0.03, 0, 0)
        _EdgeNoiseStrength("Edge Noise Strength", Range(0, 0.5)) = 0.08

        [Header(Appearance)]
        _WaterColor("Water Color", Color) = (0.42, 0.62, 0.78, 1)
        _WaterOpacity("Water Opacity", Range(0, 1)) = 0.8
        _EdgeColor("Edge Color", Color) = (0.85, 0.95, 1, 1)

        [Header(Rain Ripples)]
        _RippleDensity("Ripple Density", Float) = 6.0
        _RippleSize("Ripple Size", Float) = 0.15
        _RippleSpeed("Ripple Speed", Float) = 1.0
        _RippleStrength("Ripple Strength", Range(0, 1)) = 0.25
        _RippleColor("Ripple Color", Color) = (0.85, 0.95, 1, 1)

        [Header(Sky Reflection)]
        [NoScaleOffset] _SkyTex("Sky Texture", 2D) = "black" {}
        _SkyScale("Sky Scale", Float) = 0.25
        _SkyStrength("Sky Strength", Range(0, 1)) = 0.0
        _SkyRippleStrength("Sky Ripple Strength", Float) = 0.08
        _SkyMoveDirection("Sky Move Direction", Vector) = (1, 0, 0, 0)
        _SkyMoveSpeed("Sky Move Speed", Float) = 0.02
        [NoScaleOffset] _WaterWaveNoiseTex("Water Wave Noise (RG)", 2D) = "gray" {}
        _WaterWaveScale("Water Wave Scale", Float) = 0.6
        _WaterWaveSpeed("Water Wave Speed", Float) = 0.6
        _WaterWaveStrength("Water Wave Strength", Float) = 0.03

        [Header(Item Reflection)]
//        [NoScaleOffset] _ItemLayerTexture("Reflection RT (Item Layer)", 2D) = "black" {}
        _ReflectionStrength("Reflection Strength", Range(0, 1)) = 0.25
        _ReflectionDistortion("Reflection Distortion", Float) = 0.05
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "CanUseSpriteAtlas" = "False"
        }

        HLSLINCLUDE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_NoiseTex);
            SAMPLER(sampler_NoiseTex);
            TEXTURE2D(_SurfaceMask);
            SAMPLER(sampler_SurfaceMask);
            TEXTURE2D(_EdgeNoiseTex);
            SAMPLER(sampler_EdgeNoiseTex);
            TEXTURE2D(_SkyTex);
            SAMPLER(sampler_SkyTex);
            TEXTURE2D(_WaterWaveNoiseTex);
            SAMPLER(sampler_WaterWaveNoiseTex);
            TEXTURE2D(_ItemLayerTexture);
            SAMPLER(sampler_ItemLayerTexture);

            CBUFFER_START(UnityPerMaterial)
                half4 _WaterColor;
                half4 _EdgeColor;
                float4 _NoiseOffset;
                float _Coverage;
                float _EdgeSoftness;
                float _EdgeWidth;
                float _EdgeFade;
                float _NoiseScale;
                float _UseSurfaceMask;
                float _WaterOpacity;
                float _EdgeNoiseScale;
                float _EdgeNoiseStrength;
                float4 _EdgeNoiseSpeed;
                float _RippleDensity;
                float _RippleSize;
                float _RippleSpeed;
                float _RippleStrength;
                half4 _RippleColor;
                float _SkyScale;
                float _SkyStrength;
                float _SkyRippleStrength;
                float4 _SkyMoveDirection;
                float _SkyMoveSpeed;
                float _WaterWaveScale;
                float _WaterWaveSpeed;
                float _WaterWaveStrength;
                float _ReflectionStrength;
                float _ReflectionDistortion;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half4 color : COLOR;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            float PuddleMask(float noise, float coverage, float softness)
            {
                float threshold = 1.0 - coverage;
                if (softness <= 0.0001)
                {
                    return step(threshold, noise);
                }
                return smoothstep(threshold - softness, threshold + softness, noise);
            }

            float EdgeMask(float noise, float coverage, float edgeWidth)
            {
                if (edgeWidth <= 0.0001)
                {
                    return 0.0;
                }

                float threshold = 1.0 - coverage;
                return 1.0 - smoothstep(0.0, edgeWidth, max(noise - threshold, 0.0));
            }

            float2 RippleHash22(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.xx + p3.yz) * p3.zy);
            }

            float RippleHash1(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float3 RainRippleField(float2 worldPos, float time, float density, float size, float speed)
            {
                float2 uv = worldPos * max(density, 0.0001);
                float cellSize = 1.0 / max(density, 0.0001);
                float2 cell = floor(uv);
                float ripple = 0.0;
                float2 distortion = 0.0;

                [unroll]
                for (int y = -1; y <= 1; ++y)
                {
                    [unroll]
                    for (int x = -1; x <= 1; ++x)
                    {
                        float2 offset = float2(x, y);
                        float2 c = cell + offset;
                        float active = step(0.35, RippleHash1(c + 0.71));
                        float phase = RippleHash1(c + 0.37);
                        float sizeScale = lerp(0.25, 1.5, RippleHash1(c + 0.93));
                        float lifeScale = lerp(0.7, 1.5, RippleHash1(c + 0.57));
                        float cycleTime = time * speed * lifeScale + phase;
                        float cycleIndex = floor(cycleTime);
                        float t = frac(cycleTime);
                        float2 rnd = RippleHash22(c + cycleIndex * 0.617 + 0.13);
                        float2 center = c + rnd * 0.8 + 0.1;
                        float dist = distance(uv, center) * cellSize;
                        float radius = t * size * sizeScale;
                        float thickness = (0.06 + 0.06 * t) * lerp(0.6, 1.4, sizeScale) * size;
                        float ring = smoothstep(radius - thickness, radius, dist)
                            * (1.0 - smoothstep(radius, radius + thickness, dist));
                        float fade = 1.0 - t;
                        ripple += ring * fade * fade * active;
                        float2 dir = normalize(uv - center + float2(0.0001, 0.0001));
                        distortion += dir * ring * fade * active;
                    }
                }

                return float3(saturate(ripple), distortion.x, distortion.y);
            }

            half4 frag(Varyings input) : SV_Target
            {
                float silhouette = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a;

                float2 noiseUV = input.positionWS.xy * max(_NoiseScale, 0.0001) + _NoiseOffset.xy;
                float noise = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, noiseUV).r;
                float mask = PuddleMask(noise, _Coverage, _EdgeSoftness);

                if (_UseSurfaceMask > 0.5)
                {
                    float permission = SAMPLE_TEXTURE2D(_SurfaceMask, sampler_SurfaceMask, input.uv).r;
                    mask *= saturate(permission);
                }

                // Flowing highlight along the water edge: the scrolling edge
                // noise modulates the edge band brightness, and the flowing
                // overlay blends with the same edge color as the static edge.
                float edgeMask = EdgeMask(noise, _Coverage, _EdgeWidth);
                float2 edgeFlowUV = input.positionWS.xy * max(_EdgeNoiseScale, 0.0001) + _Time.y * _EdgeNoiseSpeed.xy;
                float edgeFlow = SAMPLE_TEXTURE2D(_EdgeNoiseTex, sampler_EdgeNoiseTex, edgeFlowUV).r * 2.0;
                edgeMask *= lerp(1.0, edgeFlow, saturate(_EdgeNoiseStrength));

                float puddleThreshold = 1.0 - _Coverage;
                float puddleDepth = saturate((noise - puddleThreshold) / max(_EdgeFade, 0.0001));
                float edgeFade = smoothstep(0.0, 1.0, puddleDepth);
                float3 rain = RainRippleField(input.positionWS.xy, _Time.y, _RippleDensity, _RippleSize, _RippleSpeed);
                float ripple = rain.x * mask;
                float2 rippleDistortion = rain.yz * _SkyRippleStrength * mask;
                float wavePhase = frac(_Time.y * _WaterWaveSpeed);
                float2 waveUV = input.positionWS.xy * max(_WaterWaveScale, 0.0001) + wavePhase;
                float2 waveNoise = SAMPLE_TEXTURE2D(_WaterWaveNoiseTex, sampler_WaterWaveNoiseTex, waveUV).rg * 2.0 - 1.0;
                float2 waveDistortion = waveNoise * _WaterWaveStrength * (1.0 + ripple * 2.0);
                float2 skyMove = _SkyMoveDirection.xy * _SkyMoveSpeed * _Time.y;
                float2 skyUV = input.positionWS.xy * max(_SkyScale, 0.0001) + rippleDistortion + waveDistortion + skyMove;
                float4 sky = SAMPLE_TEXTURE2D(_SkyTex, sampler_SkyTex, skyUV);

                // The capture RT is rendered with per-item mirrored draws inside
                // ItemLayerCaptureFeature, so the reflection is sampled at the
                // fragment's own screen position. It shares the sky texture's
                // rain ripple and wave distortion terms so both react to the
                // water surface together.
                float2 reflectionUV = GetNormalizedScreenSpaceUV(input.positionCS);
                reflectionUV += (rippleDistortion + waveDistortion) * _ReflectionDistortion * mask;
                reflectionUV = clamp(reflectionUV, 0.0, 1.0);
                float4 reflection = SAMPLE_TEXTURE2D(_ItemLayerTexture, sampler_ItemLayerTexture, reflectionUV);

                half3 color = _WaterColor.rgb * input.color.rgb;
                color = lerp(color, sky.rgb * input.color.rgb, sky.a * saturate(_SkyStrength * mask));
                color = lerp(color, reflection.rgb * input.color.rgb, saturate(_ReflectionStrength * mask * reflection.a));
                color = lerp(color, _EdgeColor.rgb * input.color.rgb, saturate(edgeMask));
                color = saturate(color + _RippleColor.rgb * input.color.rgb * ripple * _RippleStrength);
                half alpha = mask * silhouette * input.color.a * _WaterColor.a * _WaterOpacity * edgeFade;
                return half4(color, saturate(alpha));
            }
        ENDHLSL

        Pass
        {
            Name "Unlit"

            Tags
            {
                "LightMode" = "UniversalForward"
            }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
                #pragma vertex vert
                #pragma fragment frag
            ENDHLSL
        }

        Pass
        {
            Name "Universal2D"

            Tags
            {
                "LightMode" = "Universal2D"
            }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
                #pragma vertex vert
                #pragma fragment frag
            ENDHLSL
        }
    }

    Fallback Off
}
