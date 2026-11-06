// Handwritten translation of Jess Hammer's WaterShader / Caustic / Specular / Foam graphs.
// Source: https://github.com/jess-hammer/2d-pixel-water-shader
// Upstream commit: 0952ffbe7ca4c167b4ee80d4d28989e6782d6d4b (MIT; see LICENSE-JESS.txt).
// Target: Unity 2022.3, URP 14, 2D Renderer. No Shader Graph package dependency.
Shader "Custom/GameTile/Water-URP"
{
    Properties
    {
        _MainTex("Water tile mask (R: outline, A: silhouette)", 2D) = "white" {}
        _MainColour("Main Colour", Color) = (0.24252,0.5171283,0.645,0.5254902)
        [Toggle] _UseHeightMap("Use tutorial height-map gradient", Float) = 0
        [NoScaleOffset] _HeightMap("World height map (ALPHA channel)", 2D) = "white" {}
        _CurrentWorldTexturePos("Height-map world origin XY", Vector) = (0,0,0,0)
        _CurrentWorldTextureScale("Height-map inverse world size", Float) = 0.005681818
        _CurrentWorldTextureScaleXY("Height-map inverse world size XY (0 = legacy scale)", Vector) = (0,0,0,0)
        _Pixelization("World pixels per unit", Float) = 16
        [NoScaleOffset] _CausticTexture("Caustic Texture", 2D) = "white" {}
        [NoScaleOffset] _CausticHighlightTexture("Caustic Highlight Texture", 2D) = "black" {}
        _CausticColour("Caustic Colour", Color) = (0.45490196,0.77254903,0.7647059,0.12156863)
        _CausticHighlightColour("Caustic Highlight Colour", Color) = (1,1,1,0.36862746)
        _CausticScale("Caustic Scale", Float) = 0.08
        _CausticSpeed("Caustic Speed", Float) = 0.8
        _CausticSquashAmount("Caustic Y Squash", Float) = 1.3
        _CausticMovementScale("Caustic Movement Noise Scale", Float) = 1.63
        _CausticMovementAmount("Caustic Movement Amount", Float) = 0.018
        _CausticFaderMultiplier("Caustic Fader Multiplier", Float) = 0.05
        _CausticFaderScale("Caustic Fader Noise Scale", Float) = 0.17
        _SpecularColour("Specular Colour", Color) = (1,1,1,0.87058824)
        _SpecularSpeed("Specular Speed", Float) = 0.3
        _SpecularScale("Specular Moving Noise Scale", Float) = 0.83
        _SpecularScale2("Specular Static Noise Scale", Float) = 3.68
        _SpecularThreshold("Specular Threshold", Float) = -0.84
        [NoScaleOffset] _FoamTexture("Foam Texture", 2D) = "white" {}
        _FoamColour("Foam / Outline Colour", Color) = (1,1,1,1)
        _FoamScale("Foam World Scale", Float) = 0.16
        _FoamTexelSize("Foam Blur UV Step XY", Vector) = (0.005,0.005,0,0)
        _FoamBlurAmount("Foam Blur Kernel Size (odd, 1..31)", Range(1,31)) = 5
        [HideInInspector] _Color("Sprite tint", Color) = (1,1,1,1)
        [HideInInspector] _RendererColor("Renderer tint", Color) = (1,1,1,1)
        [HideInInspector] _Flip("Flip", Vector) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "CanUseSpriteAtlas"="False" }
        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Cull Off
        ZWrite Off
        ZTest LEqual

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        // Unconnected Custom Function sampler in the source graph defaults to Linear/Repeat.
        SAMPLER(sampler_PixelWater_linear_repeat);
        TEXTURE2D(_HeightMap); SAMPLER(sampler_HeightMap);
        TEXTURE2D(_CausticTexture); SAMPLER(sampler_CausticTexture);
        TEXTURE2D(_CausticHighlightTexture); SAMPLER(sampler_CausticHighlightTexture);
        TEXTURE2D(_FoamTexture); SAMPLER(sampler_FoamTexture);

        CBUFFER_START(UnityPerMaterial)
            float4 _MainColour, _CurrentWorldTexturePos, _CurrentWorldTextureScaleXY;
            float4 _CausticColour, _CausticHighlightColour;
            float4 _SpecularColour, _FoamColour, _FoamTexelSize;
            float4 _Color, _RendererColor;
            float _UseHeightMap, _CurrentWorldTextureScale, _Pixelization;
            float _CausticScale, _CausticSpeed, _CausticSquashAmount;
            float _CausticMovementScale, _CausticMovementAmount;
            float _CausticFaderMultiplier, _CausticFaderScale;
            float _SpecularSpeed, _SpecularScale, _SpecularScale2, _SpecularThreshold;
            float _FoamScale, _FoamBlurAmount;
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
            float4 color : COLOR;
            float2 uv : TEXCOORD0;
            float3 positionWS : TEXCOORD1;
            float2 lightingUV : TEXCOORD2;
            UNITY_VERTEX_OUTPUT_STEREO
        };
        Varyings WaterVert(Attributes v)
        {
            Varyings o = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(v);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            #ifdef UNITY_INSTANCING_ENABLED
                v.positionOS = UnityFlipSprite(v.positionOS, unity_SpriteFlip);
            #endif
            o.positionWS = TransformObjectToWorld(v.positionOS);
            o.positionCS = TransformWorldToHClip(o.positionWS);
            o.uv = v.uv; // Original graph samples UV0, without _MainTex_ST.
            o.color = v.color * _Color * _RendererColor;
            #ifdef UNITY_INSTANCING_ENABLED
                o.color *= unity_SpriteColor;
            #endif
            o.lightingUV = ComputeScreenPos(o.positionCS / o.positionCS.w).xy;
            return o;
        }

        // GradientNoiseNode hashType=0: deterministic Tchou hash, not the legacy mod hash.
        // Float precision preserves the world-space pixel grid and stable integer hash input.
        float2 WaterGradientDirection(float2 p)
        {
            uint2 v = (uint2)(int2)round(p);
            v.y ^= 1103515245U;
            v.x += v.y;
            v.x *= v.y;
            v.x ^= v.x >> 5u;
            v.x *= 0x27d4eb2du;
            float x = (v.x >> 8) * (1.0 / 16777215.0);
            return normalize(float2(x - floor(x + 0.5), abs(x) - 0.5));
        }
        float WaterGradientNoise(float2 uv, float scale)
        {
            float2 p = uv * scale;
            float2 ip = floor(p), fp = frac(p);
            float d00 = dot(WaterGradientDirection(ip), fp);
            float d01 = dot(WaterGradientDirection(ip + float2(0,1)), fp - float2(0,1));
            float d10 = dot(WaterGradientDirection(ip + float2(1,0)), fp - float2(1,0));
            float d11 = dot(WaterGradientDirection(ip + float2(1,1)), fp - float2(1,1));
            float2 f = fp * fp * fp * (fp * (fp * 6.0 - 15.0) + 10.0);
            return lerp(lerp(d00,d01,f.y), lerp(d10,d11,f.y), f.x) + 0.5;
        }
        float2 WaterPixelPosition(float2 positionWS)
        {
            float pixels = max(_Pixelization, 0.0001);
            return floor(positionWS * pixels) / pixels;
        }
        float WaterOverlay(float baseValue, float blendValue)
        {
            // Unity Blend / Overlay: branch on Base, with no saturation.
            float low = 2.0 * baseValue * blendValue;
            float high = 1.0 - 2.0 * (1.0-baseValue) * (1.0-blendValue);
            return lerp(high, low, step(baseValue, 0.5));
        }

        float4 WaterCaustics(float2 worldXY, float2 pixelXY)
        {
            // CausticSubGraph n16..22: quantize before squashing world Y.
            float2 squashed = pixelXY * float2(1.0, _CausticSquashAmount);
            float movement = WaterGradientNoise(squashed + _Time.y * _CausticSpeed, _CausticMovementScale);
            // Blend/Subtract with Opacity=MovementAmount is Base - Blend * Opacity.
            float2 uv = squashed * _CausticScale - movement * _CausticMovementAmount;
            float4 caustic = SAMPLE_TEXTURE2D(_CausticTexture, sampler_CausticTexture, uv) * _CausticColour;
            float4 highlight = SAMPLE_TEXTURE2D(_CausticHighlightTexture, sampler_CausticHighlightTexture, uv) * _CausticHighlightColour;
            float4 mixed = lerp(caustic, highlight, highlight.a);
            // Fader intentionally uses NON-pixelized, NON-squashed world XY, without Time.
            mixed.a = saturate(mixed.a - WaterGradientNoise(worldXY, _CausticFaderScale) * _CausticFaderMultiplier);
            return mixed;
        }
        float4 WaterSpecular(float2 pixelXY)
        {
            float t = _Time.y * _SpecularSpeed;
            float left = WaterGradientNoise(pixelXY + float2(t,0), _SpecularScale);
            float right = WaterGradientNoise(pixelXY - float2(t,0), _SpecularScale);
            float staticNoise = WaterGradientNoise(pixelXY, _SpecularScale2);
            // n19 is Overlay; n26 is Subtract. Preserve this order.
            float pattern = WaterOverlay(left, right) - staticNoise;
            // Important: Step's EDGE is pattern, and its IN is the threshold in the graph.
            return _SpecularColour * step(pattern, _SpecularThreshold);
        }
        float WaterBlurredShoreRed(float2 uv)
        {
            // Upstream calls this GaussianBlur, but all weights are equal: a BOX blur.
            // Preserve float->int truncation: even kernel settings round down to odd widths.
            int radius = (int)((clamp(_FoamBlurAmount, 1.0, 31.0) - 1.0) * 0.5);
            float sum = 0.0;
            [loop] for (int x = -radius; x <= radius; ++x)
            {
                [loop] for (int y = -radius; y <= radius; ++y)
                {
                    // Explicit LOD 0 avoids undefined derivatives inside a dynamic loop.
                    sum += SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_PixelWater_linear_repeat,
                        uv + float2(x,y) * _FoamTexelSize.xy, 0).r;
                }
            }
            float width = radius * 2 + 1;
            return sum / (width * width);
        }
        float WaterFoamAlpha(float2 uv, float2 pixelXY)
        {
            float foamAlpha = SAMPLE_TEXTURE2D(_FoamTexture, sampler_FoamTexture, pixelXY * _FoamScale).a;
            return foamAlpha * _FoamColour.a * WaterBlurredShoreRed(uv);
        }
        float3 WaterGradientRGB(float3 gammaRGB)
        {
            #if defined(UNITY_COLORSPACE_GAMMA)
                return gammaRGB;
            #else
                return SRGBToLinear(gammaRGB);
            #endif
        }
        float4 WaterDepthGradient(float depth)
        {
            // Original serialized Gradient keys. Alpha keys have their OWN timestamps.
            float3 c0 = float3(0.0427059904,0.1242964640,0.1629999876);
            float3 c1 = float3(0.1463699937,0.2441772074,0.2549999952);
            float3 c2 = float3(0.2102616578,0.4245283008,0.3462385535);
            float t1 = 16144.0 / 65535.0, t2 = 25250.0 / 65535.0;
            float3 rgb = lerp(c0,c1,saturate(depth/t1));
            rgb = lerp(rgb,c2,saturate((depth-t1)/(t2-t1)));
            float alpha = 1.0 - saturate((depth - 15410.0/65535.0) / ((25187.0-15410.0)/65535.0));
            return float4(WaterGradientRGB(rgb),alpha);
        }
        float4 EvaluatePixelWater(float2 uv, float2 worldXY)
        {
            float4 tile = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
            float4 baseColour = _MainColour;
            // The released graph disconnects this branch. OFF matches that graph;
            // ON restores the tutorial's depth-gradient path, including its alpha.
            if (_UseHeightMap > 0.5)
            {
                // Keep old square-map materials working; generated rectangular maps use XY.
                float2 heightScale = _CurrentWorldTextureScaleXY.xy;
                if (heightScale.x <= 0.0 || heightScale.y <= 0.0)
                    heightScale = float2(_CurrentWorldTextureScale, _CurrentWorldTextureScale);
                float2 heightUV = (worldXY - _CurrentWorldTexturePos.xy) * heightScale;
                float depth = SAMPLE_TEXTURE2D(_HeightMap, sampler_HeightMap, heightUV).a;
                baseColour = WaterDepthGradient(depth);
            }
            float2 pixelXY = WaterPixelPosition(worldXY);
            float4 caustics = WaterCaustics(worldXY,pixelXY);
            float4 specular = WaterSpecular(pixelXY);
            float foam = WaterFoamAlpha(uv,pixelXY);
            // Main graph order: base -> caustic -> specular -> white foam -> outline.
            float4 colour = lerp(baseColour,caustics,caustics.a);
            colour = lerp(colour,specular,specular.a * ceil(caustics.a));
            // n47 B is literal (1,1,1,1), NOT _FoamColour.rgb.
            colour = lerp(colour,float4(1,1,1,1),foam);
            float outline = tile.r * tile.a;
            colour = lerp(colour,_FoamColour * outline,outline);
            // Match released graph: features do not replace the output alpha.
            // return float4(colour.rgb,tile.a * baseColour.a);
            return float4(colour.rgb,tile.a * baseColour.a);
        }
        ENDHLSL

        Pass
        {
            Name "Water2DLit"
            Tags { "LightMode"="Universal2D" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex WaterVert
            #pragma fragment WaterLitFrag
            #pragma multi_compile_instancing
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_0 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_1 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_2 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_3 __
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/LightingUtility.hlsl"
            #if USE_SHAPE_LIGHT_TYPE_0
                SHAPE_LIGHT(0)
            #endif
            #if USE_SHAPE_LIGHT_TYPE_1
                SHAPE_LIGHT(1)
            #endif
            #if USE_SHAPE_LIGHT_TYPE_2
                SHAPE_LIGHT(2)
            #endif
            #if USE_SHAPE_LIGHT_TYPE_3
                SHAPE_LIGHT(3)
            #endif
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/CombinedShapeLightShared.hlsl"
            half4 WaterLitFrag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float4 water = EvaluatePixelWater(i.uv,i.positionWS.xy) * i.color;
                SurfaceData2D surface;
                InputData2D inputData;
                InitializeSurfaceData(water.rgb,water.a,half4(1,1,1,1),surface);
                InitializeInputData(i.uv,i.lightingUV,inputData);
                return CombinedShapeLightShared(surface,inputData);
            }
            ENDHLSL
        }
        Pass
        {
            Name "WaterNormals"
            Tags { "LightMode"="NormalsRendering" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex WaterVert
            #pragma fragment WaterNormalFrag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/NormalsRenderingShared.hlsl"
            half4 WaterNormalFrag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                // Flat normal, as in the original Sprite Lit graph. Alpha matches main pass.
                half4 surface = EvaluatePixelWater(i.uv,i.positionWS.xy) * i.color;
                return NormalsRenderingShared(surface,half3(0,0,1),
                    half3(1,0,0),half3(0,1,0),-GetViewForwardDir());
            }
            ENDHLSL
        }
        Pass
        {
            Name "WaterForwardUnlit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex WaterVert
            #pragma fragment WaterForwardFrag
            #pragma multi_compile_instancing
            half4 WaterForwardFrag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                return EvaluatePixelWater(i.uv,i.positionWS.xy) * i.color;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
