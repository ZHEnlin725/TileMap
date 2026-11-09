Shader "Custom/GameTile/River-URP-Starfield"
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
//        [NoScaleOffset] _ItemLayerTexture ("Reflection RT (Item Layer)", 2D) = "black" {}
        _ReflectionStrength ("Reflection Strength", Range(0, 1)) = 0.25
        _ReflectionDistortion ("Reflection Distortion", Float) = 0.05
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

        [Header(Day Night)]
        _DayNight ("Day Night (0 = Day, 1 = Night)", Range(0, 1)) = 1
        _NightSkyColor ("Night Sky Color", Color) = (0.03, 0.07, 0.18, 1)
        _NightSkyMix ("Night Sky Water Mix", Range(0, 1)) = 0.35
        
        [Header(Starfield)]
        _StarCellSize ("Star Cell Size", Range(0.1, 16)) = 1
        _StarDensity ("Star Density", Range(0, 1)) = 0.25
        _StarScale ("Star Scale", Range(0, 0.25)) = 0.08
        _StarColor ("Star Color", Color) = (1, 1, 1, 1)
        _StarMoveDirection ("Star Move Direction XY", Vector) = (0, 1, 0, 0)
        _StarMoveSpeed ("Star Move Speed", Float) = 0.25
        _StarTwinkleAmount ("Star Twinkle Amount", Range(0, 1)) = 0.5
        _StarTwinkleSpeed ("Star Twinkle Speed", Range(0, 10)) = 2

        [Header(Aurora)]
        [Toggle] _UseAurora ("Use Aurora", Float) = 0
        _AuroraNoiseTex ("Aurora Noise (R)", 2D) = "gray" {}
        [Toggle] _AuroraRayDown ("Ray March Down", Float) = 0
        _AuroraRaySteps ("Ray March Steps", Range(1, 12)) = 6
        _AuroraStepSize ("Ray Step Size", Range(0.01, 1)) = 0.04
        _AuroraNoiseScale ("Noise Scale", Range(0.01, 8)) = 1
        _AuroraNoiseOffset ("Noise Offset XY", Vector) = (0, 0, 0, 0)
        _AuroraThreshold ("Noise Threshold", Range(0, 1)) = 0.45
        _AuroraAlpha ("Layer Alpha", Range(0, 1)) = 0.35
        _AuroraIntensity ("Aurora Intensity", Range(0, 4)) = 1
        _AuroraColorTop ("Top Color", Color) = (0.25, 1.00, 0.75, 1.00)
        _AuroraColorBottom ("Bottom Color", Color) = (0.10, 0.45, 1.00, 1.00)
        _AuroraFlowStrength ("Aurora Flow Strength", Range(0, 2)) = 0.35
        _AuroraFlowSpeed ("Aurora Flow Speed", Range(0, 4)) = 0.5
        _AuroraFlowScale ("Aurora Flow Scale", Range(0.1, 8)) = 1
        _AuroraSoftness ("Aurora Layer Softness", Range(0.001, 1)) = 0.30

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
        TEXTURE2D(_ItemLayerTexture);
        SAMPLER(sampler_ItemLayerTexture);
        TEXTURE2D(_AuroraNoiseTex);
        SAMPLER(sampler_AuroraNoiseTex);

        CBUFFER_START(UnityPerMaterial)
            half4 _WaterColor;
            half4 _NightSkyColor;
            half4 _CausticColor;
            half4 _FoamColor;
            float _ReflectionStrength;
            float _ReflectionDistortion;
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
            float _DayNight;
            float _NightSkyMix;
            half4 _StarColor;
            float4 _StarMoveDirection;
            float4 _AuroraNoiseOffset;
            half4 _AuroraColorTop;
            half4 _AuroraColorBottom;
            float _StarCellSize;
            float _StarDensity;
            float _StarScale;
            float _StarMoveSpeed;
            float _StarTwinkleAmount;
            float _StarTwinkleSpeed;
            float _UseAurora;
            float _AuroraRayDown;
            float _AuroraRaySteps;
            float _AuroraStepSize;
            float _AuroraNoiseScale;
            float _AuroraThreshold;
            float _AuroraAlpha;
            float _AuroraIntensity;
            float _AuroraFlowStrength;
            float _AuroraFlowSpeed;
            float _AuroraFlowScale;
            float _AuroraSoftness;
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

        uint StarHashU32(uint value)
        {
            value ^= value >> 16;
            value *= 0x7feb352d;
            value ^= value >> 15;
            value *= 0x846ca68b;
            value ^= value >> 16;
            return value;
        }

        float4 StarHashCell(int2 cell)
        {
            uint hash = StarHashU32((uint)cell.x * 0x9e3779b1u + (uint)cell.y * 0x85ebca77u);
            return float4(
                (hash & 0xffffu) / 65535.0,
                ((hash >> 16) & 0xffffu) / 65535.0,
                (StarHashU32(hash ^ 0x9e3779b9u) & 0xffffu) / 65535.0,
                (StarHashU32(hash ^ 0x85ebca6bu) & 0xffffu) / 65535.0
            );
        }

        float2 SampleAuroraFlow(float2 uv)
        {
            float eps = 0.035;
            float noiseL = SAMPLE_TEXTURE2D(_AuroraNoiseTex, sampler_AuroraNoiseTex, uv - float2(eps, 0.0)).r;
            float noiseR = SAMPLE_TEXTURE2D(_AuroraNoiseTex, sampler_AuroraNoiseTex, uv + float2(eps, 0.0)).r;
            float noiseD = SAMPLE_TEXTURE2D(_AuroraNoiseTex, sampler_AuroraNoiseTex, uv - float2(0.0, eps)).r;
            float noiseU = SAMPLE_TEXTURE2D(_AuroraNoiseTex, sampler_AuroraNoiseTex, uv + float2(0.0, eps)).r;
            return float2(noiseU - noiseD, noiseL - noiseR) * 0.5;
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

            float2 reflectionUV = GetNormalizedScreenSpaceUV(input.positionCS);
            reflectionUV += causticDistort * _ReflectionDistortion * visible * stepSample.a;
            reflectionUV = clamp(reflectionUV, 0.0, 1.0);
            half4 reflection = SAMPLE_TEXTURE2D(_ItemLayerTexture, sampler_ItemLayerTexture, reflectionUV);
            float reflectionFade = smoothstep(0.0, 1.0, _WaterLevel);
            waterRGB = lerp(waterRGB, reflection.rgb * input.color.rgb,
                saturate(_ReflectionStrength * reflectionFade * visible * stepSample.a * reflection.a));

            waterRGB = lerp(waterRGB, _FoamColor.rgb * input.color.rgb, shoreMask * _FoamColor.a);
            color.rgb = lerp(color.rgb, waterRGB, visible);
            // color.a = lerp(color.a, _WaterColor.a * input.color.a, visible);

            float nightMask = smoothstep(0.0, 1.0, _DayNight);
            float waterMask = visible * stepSample.a;
            float waterLevelFade = smoothstep(0.0, 1.0, _WaterLevel);
            float nightWaterMask = nightMask * waterLevelFade;
            half3 nightSkyColor = _NightSkyColor.rgb * input.color.rgb;
            color.rgb = lerp(color.rgb, nightSkyColor, nightWaterMask * waterMask * _NightSkyMix);

            if (nightWaterMask > 0.001)
            {
                float2 starMoveDirection = _StarMoveDirection.xy;
                float starMoveLength = length(starMoveDirection);
                starMoveDirection = starMoveLength > 0.0001 ? starMoveDirection / starMoveLength : float2(0.0, 0.0);

                float2 starSamplePosition = input.worldXY - _Time.y * _StarMoveSpeed * starMoveDirection;
                float starCellSize = max(_StarCellSize, 0.0001);
                float2 starCellId = floor(starSamplePosition / starCellSize);
                int2 starCell = (int2)starCellId;
                float4 starRandom = StarHashCell(starCell);

                float2 starCellOrigin = starCellId * starCellSize;
                float2 starLocalPosition = starSamplePosition - starCellOrigin;
                float2 starCenter = (0.25 + starRandom.xy * 0.5) * starCellSize;
                float starDistance = length(starLocalPosition - starCenter);

                float starExists = step(starRandom.w, _StarDensity);
                float star = step(starDistance, _StarScale) * starExists;
                float twinkle = lerp(
                    1.0,
                    saturate(0.5 + 0.5 * sin(6.2831853 * (_Time.y * _StarTwinkleSpeed + starRandom.z))),
                    _StarTwinkleAmount
                );

                float starAlpha = star * _StarColor.a * twinkle * nightWaterMask * waterMask;
                color.rgb = lerp(color.rgb, _StarColor.rgb * input.color.rgb, saturate(starAlpha));

                if (_UseAurora > 0.5)
                {
                    int auroraStepCount = clamp((int)_AuroraRaySteps, 1, 12);
                    float2 auroraRayDirection = _AuroraRayDown > 0.5 ? float2(0.0, -1.0) : float2(0.0, 1.0);
                    float3 auroraColor = 0;
                    float auroraAlpha = 0;
                    float auroraWeight = 0;

                    [loop]
                    for (int layer = 0; layer < auroraStepCount; ++layer)
                    {
                        float layerT = smoothstep(0.0, 1.0, saturate((float)layer / max((float)auroraStepCount - 1.0, 1.0)));
                        float2 layerPosition = starSamplePosition + auroraRayDirection * _AuroraStepSize * (float)layer;
                        float2 noiseUV = layerPosition * _AuroraNoiseScale + _AuroraNoiseOffset.xy;
                        float2 flowUV = noiseUV * _AuroraFlowScale + _Time.y * _AuroraFlowSpeed * float2(1.0, 0.73);
                        float2 flow = SampleAuroraFlow(flowUV);
                        noiseUV += flow * _AuroraFlowStrength;
                        float noise = SAMPLE_TEXTURE2D(_AuroraNoiseTex, sampler_AuroraNoiseTex, noiseUV).r;
                        float3 layerColor = lerp(_AuroraColorBottom.rgb, _AuroraColorTop.rgb, layerT);
                        float layerWeight = smoothstep(_AuroraThreshold, _AuroraThreshold + max(_AuroraSoftness, 0.001), noise);

                        auroraColor += layerColor * layerWeight;
                        auroraAlpha += layerWeight * _AuroraAlpha;
                        auroraWeight += layerWeight;
                    }

                    auroraColor = saturate(auroraColor / max(auroraWeight, 0.001));
                    auroraAlpha = saturate(auroraAlpha * _AuroraIntensity * nightWaterMask * waterMask);
                    color.rgb = lerp(color.rgb, auroraColor, auroraAlpha);
                }
            }

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
    Fallback "Sprites/Default"
}
