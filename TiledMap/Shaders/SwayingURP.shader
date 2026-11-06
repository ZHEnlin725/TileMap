Shader "Custom/Swaying-URP"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _SwayingMask ("SwayingMask", 2D) = "white" {}
        _Pixels ("Pixels", Float) = 256
        _Density ("Density", Float) = 1
        _Speed ("Speed", Float) = 1
        _Blend ("Blend", Range(0, 1)) = 0
        _WindDirection ("WindDirection", Vector) = (0, 0, 0, 0)
        _WindStrength ("WindStrength", Float) = 1
        _WindFrequency ("WindFrequency", Float) = 1
        _SwayAmount ("SwayAmount", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        HLSLINCLUDE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float2 uv         : TEXCOORD0;
                float  fogFactor  : TEXCOORD1;
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_SwayingMask);
            SAMPLER(sampler_SwayingMask);

            // NOTE: Do not ifdef the properties here as SRP batcher can not handle different layouts.
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _SwayingMask_ST;
                float  _WindFrequency;
                float  _WindStrength;
                float  _SwayAmount;
                float4 _WindDirection;
                float  _Pixels;
                float  _Density;
                float  _Speed;
                float  _Blend;
            CBUFFER_END

            void Hash_Tchou_2_2_uint(uint2 v, out uint2 o)
            {
                v.y ^= 1103515245U;
                v.x += v.y;
                v.x *= v.y;
                v.x ^= v.x >> 5u;
                v.x *= 0x27d4eb2du;
                v.y ^= (v.x << 3u);
                o = v;
            }

            void Hash_Tchou_2_2_float(float2 i, out float2 o)
            {
                uint2 r;
                uint2 v = (uint2)(int2)round(i);
                Hash_Tchou_2_2_uint(v, r);
                o = (r >> 8) * (1.0 / float(0x00ffffff));
            }

            float2 Unity_Voronoi_RandomVector_Deterministic_float(float2 UV, float offset)
            {
                Hash_Tchou_2_2_float(UV, UV);
                return float2(sin(UV.y * offset), cos(UV.x * offset)) * 0.5 + 0.5;
            }

            void Unity_Voronoi_Deterministic_float(float2 UV, float AngleOffset, float CellDensity, out float Out,
                                                   out float Cells)
            {
                float2 g = floor(UV * CellDensity);
                float2 f = frac(UV * CellDensity);
                float2 res = float2(8.0, 0.0);
                for (int y = -1; y <= 1; y++)
                {
                    for (int x = -1; x <= 1; x++)
                    {
                        float2 lattice = float2(x, y);
                        float2 offset = Unity_Voronoi_RandomVector_Deterministic_float(lattice + g, AngleOffset);
                        float d = distance(lattice + offset, f);
                        if (d < res.x)
                        {
                            res = float2(d, offset.x);
                            Out = res.x;
                            Cells = res.y;
                        }
                    }
                }
            }

            void Unity_Blend_Overlay_float4(float4 Base, float4 BlendValue, out float4 Out, float Opacity)
            {
                float4 result1 = 1.0 - 2.0 * (1.0 - Base) * (1.0 - BlendValue);
                float4 result2 = 2.0 * Base * BlendValue;
                float4 zeroOrOne = step(Base, 0.5);
                Out = result2 * zeroOrOne + (1 - zeroOrOne) * result1;
                Out = lerp(Base, Out, Opacity);
            }

            Varyings vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                float wind = _WindFrequency * _Time.x;
                float4 offset = (sin(input.positionOS.x * .1 + wind) * .5 + .5) * _WindDirection * _WindStrength *
                    _SwayAmount * input.uv.y;
                float mask = SAMPLE_TEXTURE2D_LOD(_SwayingMask, sampler_SwayingMask, input.uv, 0);
                float4 position = lerp(input.positionOS, input.positionOS + offset, mask);
                o.positionCS = TransformObjectToHClip(position.xyz);
                o.uv = input.uv * _MainTex_ST.xy + _MainTex_ST.zw;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float wind = _WindFrequency * _Time.y * _Speed;
                float2 uv = floor(input.uv * _Pixels) / _Pixels;
                float voronoi, cells;
                Unity_Voronoi_Deterministic_float(uv, wind, _Density, voronoi, cells);
                float4 mask = SAMPLE_TEXTURE2D(_SwayingMask, sampler_SwayingMask, input.uv) * _Blend;
                float4 result;
                Unity_Blend_Overlay_float4(float4(uv.x, uv.y, 0, 0), voronoi, result, mask.r);
                half4 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, result.xy);
                col.rgb = MixFog(col.rgb, input.fogFactor);
                return col;
            }
        ENDHLSL

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
                #pragma vertex vert
                #pragma fragment frag
                #pragma multi_compile_fog
                #pragma multi_compile_instancing
            ENDHLSL
        }

        Pass
        {
            Name "Universal2D"
            Tags { "LightMode" = "Universal2D" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
                #pragma vertex vert
                #pragma fragment frag
                #pragma multi_compile_fog
                #pragma multi_compile_instancing
            ENDHLSL
        }
    }
}
