Shader "Custom/GameTile/River-URP"
{
    Properties
    {
        _MainTex ("Bottom Texture", 2D) = "white" {}
        _StepTex ("Depth Step Mask", 2D) = "black" {}
        _CausticTex ("Caustic Texture", 2D) = "black" {}
        _DistortTex ("Distortion Texture (RG = UV Offset)", 2D) = "black" {}
        [Toggle(_USE_DISTORT_TEX)] _UseDistortTex ("Use Distortion Texture", Float) = 0
        _DistortStrength ("Distortion Strength", Range(0, 0.25)) = 0.04
        _DistortScale ("Distortion Noise Scale", Range(0.05, 20)) = 3.00
        _DistortSpeed ("Distortion Speed", Float) = 0.35
        _DistortShoreWidth ("Distortion Shore Fade", Range(0.01, 1)) = 0.30
        _WaterLevel ("Water Level", Range(0, 1)) = 0.55
        _WaterColor ("Water Color", Color) = (0.20, 0.50, 0.80, 0.85)
        _CausticColor ("Caustic Color", Color) = (0.70, 0.95, 1.00, 1.00)
        _CausticStrength ("Caustic Strength", Range(0, 1)) = 0.45
        _CausticScale ("Caustic Scale", Float) = 0.18
        _CausticSpeed ("Caustic Speed", Float) = 0.45
        _CausticDistortScale ("Caustic Distortion Influence", Range(0, 4)) = 1.50
        _FoamColor ("Shore Foam Color", Color) = (0.92, 0.98, 1.00, 0.90)
        _FoamWidth ("Foam Width", Range(0, 0.5)) = 0.14
        _FoamWaveAmplitude ("Foam Wave Amplitude", Range(0, 0.25)) = 0.05
        _FoamWaveScale ("Foam Wave Scale", Range(0.01, 1)) = 0.15
        _FoamWaveSpeed ("Foam Wave Speed", Float) = 0.80
        _FoamRandomness ("Foam Randomness", Range(0, 1)) = 0.45

        [HideInInspector] _Color ("Tint", Color) = (1, 1, 1, 1)
        [HideInInspector] PixelSnap ("Pixel snap", Float) = 0
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "CanUseSpriteAtlas" = "True"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);
        TEXTURE2D(_StepTex);
        SAMPLER(sampler_StepTex);
        TEXTURE2D(_CausticTex);
        SAMPLER(sampler_CausticTex);
        TEXTURE2D(_DistortTex);
        SAMPLER(sampler_DistortTex);

        CBUFFER_START(UnityPerMaterial)
            half4 _WaterColor;
            half4 _CausticColor;
            half4 _FoamColor;
            float _CausticStrength;
            float _CausticScale;
            float _CausticSpeed;
            float _CausticDistortScale;
            float _DistortStrength;
            float _DistortScale;
            float _DistortSpeed;
            float _DistortShoreWidth;
            float _FoamWidth;
            float _FoamWaveAmplitude;
            float _FoamWaveScale;
            float _FoamWaveSpeed;
            float _FoamRandomness;
            half4 _Color;
            half4 _RendererColor;
            float _WaterLevel;
            float PixelSnap;
        CBUFFER_END

        struct Attributes
        {
            float3 positionOS : POSITION;
            float4 color : COLOR;
            float2 uv : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            half4 color : COLOR;
            float2 uv : TEXCOORD0;
            float2 worldXY : TEXCOORD1;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        Varyings RiverVertex(Attributes input)
        {
            Varyings output = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

            input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);
            output.positionCS = TransformObjectToHClip(input.positionOS);
            output.uv = input.uv;
            output.worldXY = TransformObjectToWorld(input.positionOS).xy;
            output.color = input.color * _Color * _RendererColor * unity_SpriteColor;
            return output;
        }

        float RiverHash(float2 p)
        {
            return frac(sin(dot(p, float2(127.10, 311.70))) * 43758.5453123);
        }

        float RiverNoise(float2 p)
        {
            float2 i = floor(p);
            float2 f = frac(p);
            float2 u = f * f * (3.0 - 2.0 * f);

            float a = RiverHash(i);
            float b = RiverHash(i + float2(1.0, 0.0));
            float c = RiverHash(i + float2(0.0, 1.0));
            float d = RiverHash(i + float2(1.0, 1.0));

            return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
        }

        half4 RiverFragment(Varyings input) : SV_Target
        {
            half4 stepSample = SAMPLE_TEXTURE2D(_StepTex, sampler_StepTex, input.uv);

            //严禁修改
            half visible = step(stepSample.r, _WaterLevel - .001);

            // 扰动与焦散共享同一流向，保证两者运动协调
            float2 flowDir = normalize(float2(0.65h, -1.00h));

            float2 distortUV = input.worldXY * _DistortScale;
            float2 distortOffset;
            #ifdef _USE_DISTORT_TEX
            half4 distortSample = SAMPLE_TEXTURE2D(_DistortTex, sampler_DistortTex,
                                                   distortUV + flowDir * _Time.y * _DistortSpeed);
            distortOffset = (distortSample.rg - 0.5h) * 2.0h;
            #else
            float distortN1 = RiverNoise(distortUV + flowDir * _Time.y * _DistortSpeed);
            float distortN2 = RiverNoise(distortUV * 1.30h + 17.30h
                + flowDir * _Time.y * _DistortSpeed * 0.60h);
            distortOffset = (float2(distortN1, distortN2) - 0.5h) * 2.0h;
            #endif

            float distortShoreFade = smoothstep(0.0h, _DistortShoreWidth,
                                                       (_WaterLevel - stepSample.r) / max(_WaterLevel, 1e-4h));
            float distortEnv = _DistortStrength * visible * stepSample.a
                * distortShoreFade * pow(_WaterLevel, 3);
            distortOffset *= distortEnv;

            // 扭曲后的采样点必须仍落在水体范围内，避免把岸边像素拉进水里
            float2 bottomUV = input.uv + distortOffset;
            half4 bottomStep = SAMPLE_TEXTURE2D(_StepTex, sampler_StepTex, bottomUV);
            half bottomInWater = step(bottomStep.r, _WaterLevel - .001) * bottomStep.a;
            bottomUV = lerp(input.uv, bottomUV, bottomInWater);

            half4 bottom = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, bottomUV);

            // 焦散在跟随水底偏移之外，再叠加一层同流向的高频局部弯折
            float2 causticWarpUV = input.worldXY * _DistortScale * 2.20h
                + flowDir * _Time.y * _DistortSpeed * 1.35h;
            float2 causticWarp = float2(
                RiverNoise(causticWarpUV),
                RiverNoise(causticWarpUV + 37.70h)) - 0.5h;

            float2 causticDistort = distortOffset * _CausticDistortScale
                + causticWarp * 0.5h * distortEnv * _CausticDistortScale;

            float2 causticUV = input.worldXY * _CausticScale
                + _Time.y * _CausticSpeed * float2(0.65h, -1.00h)
                + causticDistort;
            half3 causticRGB = SAMPLE_TEXTURE2D(_CausticTex, sampler_CausticTex, causticUV).rgb;
            half causticLum = dot(causticRGB, half3(0.299h, 0.587h, 0.114h));
            half causticMask = saturate(causticLum * _CausticStrength * pow(_WaterLevel, 3));

            float2 foamNoiseUV = input.worldXY * _FoamWaveScale;
            float foamWave = RiverNoise(foamNoiseUV
                + float2(_Time.y * _FoamWaveSpeed, -_Time.y * _FoamWaveSpeed * 0.70h));
            float foamRandom = RiverHash(floor(foamNoiseUV * 4.0h)
                + floor(_Time.y * _FoamWaveSpeed * 2.0h));
            float foamMix = saturate(lerp(foamWave, foamRandom, _FoamRandomness));

            half foamRange = min(_FoamWaveAmplitude, _FoamWidth * 0.5h);
            half foamOffset = (foamMix - 0.5h) * 2.0h * foamRange;
            half foamCenter = _WaterLevel - _FoamWidth * 0.5h + foamOffset;
            half foamUpper = min(_WaterLevel - 0.001h, foamCenter + _FoamWidth * 0.5h);
            half foamLower = max(foamCenter - _FoamWidth * 0.5h, 0.001h);
            half foamMask = step(foamLower, stepSample.r) * step(stepSample.r, foamUpper) * stepSample.a;
            half shoreMask = foamMask * visible;

            half4 color = bottom * input.color;

            //严禁修改
            half3 waterRGB = lerp(color.rgb, _WaterColor * input.color, _WaterColor.a * _WaterLevel);
            //严禁修改
            waterRGB = lerp(color.rgb, waterRGB.rgb, stepSample.a);

            // 保留焦散贴图自带的彩虹光晕，按亮度遮罩以加色方式叠进水体
            half3 causticGlow = causticRGB * _CausticColor.rgb * input.color.rgb;
            waterRGB += causticGlow * causticMask * visible * stepSample.a;
            waterRGB = lerp(waterRGB, _FoamColor.rgb * input.color.rgb, shoreMask * _FoamColor.a);
            color.rgb = lerp(color.rgb, waterRGB, visible);
            // color.a = lerp(color.a, _WaterColor.a * input.color.a, visible);
            return color;
        }
        ENDHLSL

        Pass
        {
            Tags
            {
                "LightMode" = "Universal2D"
            }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex RiverVertex
            #pragma fragment RiverFragment
            #pragma multi_compile_instancing
            #pragma shader_feature_local _USE_DISTORT_TEX
            ENDHLSL
        }

        Pass
        {
            Tags
            {
                "LightMode" = "UniversalForward"
                "Queue" = "Transparent"
                "RenderType" = "Transparent"
            }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex RiverVertex
            #pragma fragment RiverFragment
            #pragma multi_compile_instancing
            #pragma shader_feature_local _USE_DISTORT_TEX
            ENDHLSL
        }
    }
    CustomEditor "RiverWaterCausticsShaderGUI"
    Fallback "Sprites/Default"
}
