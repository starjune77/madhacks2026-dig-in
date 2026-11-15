// The ground of the dig volume. World-space (the mesh has no UVs), fully matte.
//   - Grass on the untouched surface, bare earth in the starting hollow.
//   - Below: strata by depth (dirt, clay, stone), with the same wobble DigVolume uses
//     for hardness, so what looks like stone is stone.
//   - Warmer and a little lighter near the hidden life path.
// Globals (_Dig...) are set by DigVolume at start.
Shader "DigIn/Soil"
{
    Properties
    {
        _GrassColor("Grass", Color) = (0.30, 0.36, 0.16, 1)
        _GrassDark("Grass (dark patches)", Color) = (0.20, 0.27, 0.11, 1)
        _DirtColor("Dirt (Childhood)", Color) = (0.36, 0.25, 0.16, 1)
        _ClayColor("Clay (School Days)", Color) = (0.50, 0.29, 0.19, 1)
        _StoneColor("Stone (Growing Up)", Color) = (0.34, 0.33, 0.34, 1)
        _WarmTint("Tint near the life path", Color) = (1.22, 1.06, 0.82, 1)
        _Moist("Darkening of dug surfaces", Range(0, 1)) = 0.28
        _Mottle("Mottling strength", Range(0, 1)) = 0.35
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _GrassColor;
            half4 _GrassDark;
            half4 _DirtColor;
            half4 _ClayColor;
            half4 _StoneColor;
            half4 _WarmTint;
            half _Moist;
            half _Mottle;
        CBUFFER_END

        TEXTURE3D(_DigPathTex);     SAMPLER(sampler_DigPathTex);
        TEXTURE2D(_DigWobbleTex);   SAMPLER(sampler_DigWobbleTex);
        float4 _DigVolumeMin;       // xyz = min corner
        float4 _DigVolumeSize;      // xyz = size
        float4 _DigStrata;          // x, y = depth of the two stratum boundaries
        float4 _DigStart;           // xyz = the starting hollow, w = radius of bare earth

        float Hash3(float3 p)
        {
            p = frac(p * 0.3183099 + 0.1);
            p *= 17.0;
            return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
        }

        float Noise3(float3 x)
        {
            float3 i = floor(x);
            float3 f = frac(x);
            f = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(lerp(Hash3(i + float3(0, 0, 0)), Hash3(i + float3(1, 0, 0)), f.x),
                             lerp(Hash3(i + float3(0, 1, 0)), Hash3(i + float3(1, 1, 0)), f.x), f.y),
                        lerp(lerp(Hash3(i + float3(0, 0, 1)), Hash3(i + float3(1, 0, 1)), f.x),
                             lerp(Hash3(i + float3(0, 1, 1)), Hash3(i + float3(1, 1, 1)), f.x), f.y), f.z);
        }

        float Fbm(float3 p)
        {
            return Noise3(p) * 0.5 + Noise3(p * 2.3 + 7.1) * 0.3 + Noise3(p * 5.9 + 3.3) * 0.2;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            // Soil does not shine.
            #define _SPECULARHIGHLIGHTS_OFF 1
            #define _ENVIRONMENTREFLECTIONS_OFF 1

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half fogFactor : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vertexInput.positionCS;
                output.positionWS = vertexInput.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
            #if defined(_FOG_FRAGMENT)
                output.fogFactor = 0;
            #else
                output.fogFactor = ComputeFogFactor(vertexInput.positionCS.z);
            #endif
                return output;
            }

            void Frag(Varyings input, out half4 outColor : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                float3 p = input.positionWS;
                half3 n = normalize(input.normalWS);

                // ---- What is it made of -----------------------------------------
                float3 uvw = (p - _DigVolumeMin.xyz) / _DigVolumeSize.xyz;
                float wobble = SAMPLE_TEXTURE2D(_DigWobbleTex, sampler_DigWobbleTex, uvw.xz).r;
                float depth = -p.y + wobble;
                float ragged = (Fbm(p * 1.3) - 0.5) * 0.5;

                half3 soil = _DirtColor.rgb;
                soil = lerp(soil, _ClayColor.rgb, smoothstep(-0.25, 0.25, depth + ragged - _DigStrata.x));
                soil = lerp(soil, _StoneColor.rgb, smoothstep(-0.25, 0.25, depth + ragged - _DigStrata.y));

                // Layered earth: faint horizontal bands, plus clumpy mottling.
                float band = Noise3(float3(p.x * 0.15, p.y * 3.1, p.z * 0.15)) - 0.5;
                float mottle = Fbm(p * 3.7) - 0.5;
                float grit = Noise3(p * 23.0) - 0.5;
                soil *= 1.0 + band * 0.22 + mottle * _Mottle + grit * 0.10;

                // Softer, warmer earth along the life path.
                half warm = SAMPLE_TEXTURE3D(_DigPathTex, sampler_DigPathTex, uvw).r;
                warm = saturate(warm + (Fbm(p * 0.9 + 11.0) - 0.5) * 0.35) * warm;
                soil = lerp(soil, soil * _WarmTint.rgb, warm);

                // Grass on the untouched top; bare earth in the starting hollow.
                float bare = 1.0 - smoothstep(_DigStart.w * 0.55, _DigStart.w, distance(p.xz, _DigStart.xz) + (Fbm(p * 1.7) - 0.5) * 1.2);
                float top = smoothstep(0.55, 0.8, n.y) * smoothstep(-0.30, -0.10, p.y + (Noise3(p * 4.0) - 0.5) * 0.1);
                float grass = top * (1.0 - bare);
                half3 grassColor = lerp(_GrassDark.rgb, _GrassColor.rgb, Fbm(p * 0.6 + 3.0));
                grassColor *= 0.9 + 0.2 * Noise3(p * 19.0);

                // Dug surfaces are darker and moister than the dry top.
                half3 albedo = lerp(soil * (1.0 - _Moist * (1.0 - top * bare)), grassColor, grass);

                // ---- Lighting ---------------------------------------------------
                InputData inputData = (InputData)0;
                inputData.positionWS = p;
                inputData.normalWS = n;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(p);
            #if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                inputData.shadowCoord = TransformWorldToShadowCoord(p);
            #else
                inputData.shadowCoord = float4(0, 0, 0, 0);
            #endif
                inputData.fogCoord = InitializeInputDataFog(float4(p, 1.0), input.fogFactor);
                inputData.vertexLighting = half3(0, 0, 0);
                inputData.bakedGI = SampleSH(n);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = albedo;
                surfaceData.metallic = 0;
                surfaceData.specular = half3(0, 0, 0);
                surfaceData.smoothness = 0;
                surfaceData.normalTS = half3(0, 0, 1);
                surfaceData.occlusion = 1;
                surfaceData.alpha = 1;

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                outColor = half4(color.rgb, 1);

            #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
            #endif
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            float4 ShadowVert(Attributes input) : SV_POSITION
            {
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                return ApplyShadowClamping(positionCS);
            }

            half4 ShadowFrag() : SV_TARGET { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            float4 DepthVert(float4 positionOS : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(positionOS.xyz);
            }

            half4 DepthFrag(float4 positionCS : SV_POSITION) : SV_TARGET { return positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3 normalWS : TEXCOORD0;
            };

            Varyings DepthNormalsVert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            void DepthNormalsFrag(Varyings input, out half4 outNormalWS : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                outNormalWS = half4(NormalizeNormalPerPixel(input.normalWS), 0.0);
            #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
            #endif
            }
            ENDHLSL
        }
    }
}
