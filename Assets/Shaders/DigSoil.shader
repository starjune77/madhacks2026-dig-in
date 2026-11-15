// The ground of the dig volume: photoscanned soil, clay and rock (Poly Haven, CC0),
// projected triplanar in world space (the mesh has no UVs), fully matte.
//   - Grass on the untouched surface, bare earth in the starting hollow.
//   - Below: strata by depth (dirt, clay, stone) with the same wobble DigVolume uses for
//     hardness, so what looks like stone is stone. Walls show compact earth, floors loose.
//   - Freshly dug earth is darker and a little richer in colour than the dry top.
//   - Warmer near the hidden life path.
// Globals (_Dig...) are set by DigVolume at start.
Shader "DigIn/Soil"
{
    Properties
    {
        [Header(Dirt (Childhood))]
        _DirtWallDiff("Wall colour", 2D) = "grey" {}
        [NoScaleOffset][Normal] _DirtWallNrm("Wall normal", 2D) = "bump" {}
        [NoScaleOffset] _DirtWallAO("Wall AO", 2D) = "white" {}
        [NoScaleOffset] _DirtFloorDiff("Floor colour", 2D) = "grey" {}
        [NoScaleOffset][Normal] _DirtFloorNrm("Floor normal", 2D) = "bump" {}
        [NoScaleOffset] _DirtFloorAO("Floor AO", 2D) = "white" {}
        _DirtTint("Dirt tint", Color) = (1, 1, 1, 1)
        _DirtScale("Dirt tiles per metre", Float) = 0.55

        [Header(Clay (School Days))]
        [NoScaleOffset] _ClayDiff("Colour", 2D) = "grey" {}
        [NoScaleOffset][Normal] _ClayNrm("Normal", 2D) = "bump" {}
        [NoScaleOffset] _ClayAO("AO", 2D) = "white" {}
        _ClayTint("Clay tint", Color) = (1, 1, 1, 1)
        _ClayScale("Clay tiles per metre", Float) = 0.5

        [Header(Stone (Growing Up))]
        [NoScaleOffset] _StoneDiff("Colour", 2D) = "grey" {}
        [NoScaleOffset][Normal] _StoneNrm("Normal", 2D) = "bump" {}
        [NoScaleOffset] _StoneAO("AO", 2D) = "white" {}
        _StoneTint("Stone tint", Color) = (1, 1, 1, 1)
        _StoneScale("Stone tiles per metre", Float) = 0.4

        [Header(Grass)]
        [NoScaleOffset] _GrassDiff("Colour", 2D) = "grey" {}
        [NoScaleOffset][Normal] _GrassNrm("Normal", 2D) = "bump" {}
        [NoScaleOffset] _GrassAO("AO", 2D) = "white" {}
        _GrassTint("Grass tint", Color) = (1, 1, 1, 1)
        _GrassScale("Grass tiles per metre", Float) = 0.5

        [Header(Look)]
        _NormalStrength("Normal strength", Range(0, 2)) = 1
        _AOStrength("AO strength", Range(0, 1)) = 0.8
        _WarmTint("Tint near the life path", Color) = (1.15, 1.04, 0.86, 1)
        _Moist("Darkening of dug earth", Range(0, 1)) = 0.22
        _Macro("Large-scale variation", Range(0, 1)) = 0.25
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _DirtWallDiff_ST;
            half4 _DirtTint, _ClayTint, _StoneTint, _GrassTint, _WarmTint;
            float _DirtScale, _ClayScale, _StoneScale, _GrassScale;
            half _NormalStrength, _AOStrength, _Moist, _Macro;
        CBUFFER_END
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
            #include "DigTriplanar.hlsl"

            TEXTURE2D(_DirtWallDiff);  SAMPLER(sampler_DirtWallDiff);
            TEXTURE2D(_DirtWallNrm);   TEXTURE2D(_DirtWallAO);
            TEXTURE2D(_DirtFloorDiff); TEXTURE2D(_DirtFloorNrm); TEXTURE2D(_DirtFloorAO);
            TEXTURE2D(_ClayDiff);      TEXTURE2D(_ClayNrm);      TEXTURE2D(_ClayAO);
            TEXTURE2D(_StoneDiff);     TEXTURE2D(_StoneNrm);     TEXTURE2D(_StoneAO);
            TEXTURE2D(_GrassDiff);     TEXTURE2D(_GrassNrm);     TEXTURE2D(_GrassAO);
            #define SAMP sampler_DirtWallDiff

            TEXTURE3D(_DigPathTex);     SAMPLER(sampler_DigPathTex);
            TEXTURE2D(_DigWobbleTex);   SAMPLER(sampler_DigWobbleTex);
            TEXTURE2D(_DigDisturbTex);
            float4 _DigVolumeMin;       // xyz = min corner
            float4 _DigVolumeSize;      // xyz = size
            float4 _DigStrata;          // x, y = depth of the two stratum boundaries
            float4 _DigStart;           // xyz = the starting hollow, w = radius of bare earth

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
                float ragged = (DigFbm(p * 1.3) - 0.5) * 0.5;
                half clay = smoothstep(-0.25, 0.25, depth + ragged - _DigStrata.x);
                half stone = smoothstep(-0.25, 0.25, depth + ragged - _DigStrata.y);

                // Grass on the untouched top; bare earth in the starting hollow.
                float bare = 1.0 - smoothstep(_DigStart.w * 0.55, _DigStart.w, distance(p.xz, _DigStart.xz) + (DigFbm(p * 1.7) - 0.5) * 1.2);
                half top = smoothstep(0.55, 0.8, n.y) * smoothstep(-0.30, -0.10, p.y + (DigNoise3(p * 4.0) - 0.5) * 0.1);
                half disturbed = SAMPLE_TEXTURE2D(_DigDisturbTex, sampler_DigWobbleTex, uvw.xz).r;
                disturbed = saturate(disturbed + (DigNoise3(p * 3.0) - 0.5) * 0.6 * disturbed);
                half grass = top * (1.0 - bare) * (1.0 - disturbed);

                // ---- Sample only what is visible here ---------------------------
                TriSample soil;
                if (stone < 0.99)
                {
                    if (clay < 0.99)
                    {
                        TriCoords t = MakeTriCoords(p, n, _DirtScale, 4);
                        soil = SampleTri(TEXTURE2D_ARGS(_DirtWallDiff, SAMP), TEXTURE2D_ARGS(_DirtWallNrm, SAMP), TEXTURE2D_ARGS(_DirtWallAO, SAMP),
                                         TEXTURE2D_ARGS(_DirtFloorDiff, SAMP), TEXTURE2D_ARGS(_DirtFloorNrm, SAMP), TEXTURE2D_ARGS(_DirtFloorAO, SAMP), t, _NormalStrength);
                        soil.albedo *= _DirtTint.rgb;
                        if (clay > 0.01)
                        {
                            TriCoords tc = MakeTriCoords(p, n, _ClayScale, 4);
                            TriSample c = SampleTri(TEXTURE2D_ARGS(_ClayDiff, SAMP), TEXTURE2D_ARGS(_ClayNrm, SAMP), TEXTURE2D_ARGS(_ClayAO, SAMP),
                                                    TEXTURE2D_ARGS(_ClayDiff, SAMP), TEXTURE2D_ARGS(_ClayNrm, SAMP), TEXTURE2D_ARGS(_ClayAO, SAMP), tc, _NormalStrength);
                            c.albedo *= _ClayTint.rgb;
                            soil = LerpSample(soil, c, clay);
                        }
                    }
                    else
                    {
                        TriCoords tc = MakeTriCoords(p, n, _ClayScale, 4);
                        soil = SampleTri(TEXTURE2D_ARGS(_ClayDiff, SAMP), TEXTURE2D_ARGS(_ClayNrm, SAMP), TEXTURE2D_ARGS(_ClayAO, SAMP),
                                         TEXTURE2D_ARGS(_ClayDiff, SAMP), TEXTURE2D_ARGS(_ClayNrm, SAMP), TEXTURE2D_ARGS(_ClayAO, SAMP), tc, _NormalStrength);
                        soil.albedo *= _ClayTint.rgb;
                    }
                    if (stone > 0.01)
                    {
                        TriCoords ts = MakeTriCoords(p, n, _StoneScale, 4);
                        TriSample s = SampleTri(TEXTURE2D_ARGS(_StoneDiff, SAMP), TEXTURE2D_ARGS(_StoneNrm, SAMP), TEXTURE2D_ARGS(_StoneAO, SAMP),
                                                TEXTURE2D_ARGS(_StoneDiff, SAMP), TEXTURE2D_ARGS(_StoneNrm, SAMP), TEXTURE2D_ARGS(_StoneAO, SAMP), ts, _NormalStrength);
                        s.albedo *= _StoneTint.rgb;
                        soil = LerpSample(soil, s, stone);
                    }
                }
                else
                {
                    TriCoords ts = MakeTriCoords(p, n, _StoneScale, 4);
                    soil = SampleTri(TEXTURE2D_ARGS(_StoneDiff, SAMP), TEXTURE2D_ARGS(_StoneNrm, SAMP), TEXTURE2D_ARGS(_StoneAO, SAMP),
                                     TEXTURE2D_ARGS(_StoneDiff, SAMP), TEXTURE2D_ARGS(_StoneNrm, SAMP), TEXTURE2D_ARGS(_StoneAO, SAMP), ts, _NormalStrength);
                    soil.albedo *= _StoneTint.rgb;
                }

                // Large patches of lighter and darker earth, so the tiling never shows.
                soil.albedo *= 1.0 + (DigFbm(p * 0.35 + 5.0) - 0.5) * 2.0 * _Macro;

                // Softer, warmer earth along the life path.
                half warm = SAMPLE_TEXTURE3D(_DigPathTex, sampler_DigPathTex, uvw).r;
                warm = saturate(warm + (DigFbm(p * 0.9 + 11.0) - 0.5) * 0.35) * warm;
                soil.albedo = lerp(soil.albedo, soil.albedo * _WarmTint.rgb, warm);

                // Dug earth is moist: darker, a touch richer. The untouched top stays dry.
                half dug = 1.0 - top * bare * (1.0 - disturbed);
                half3 moist = soil.albedo * (1.0 - _Moist);
                moist = lerp(dot(moist, half3(0.3, 0.59, 0.11)).xxx, moist, 1.12);
                soil.albedo = lerp(soil.albedo, moist, dug);

                TriSample surface = soil;
                if (grass > 0.01)
                {
                    TriCoords tg = MakeTriCoords(p, n, _GrassScale, 8);
                    TriSample g = SampleTri(TEXTURE2D_ARGS(_GrassDiff, SAMP), TEXTURE2D_ARGS(_GrassNrm, SAMP), TEXTURE2D_ARGS(_GrassAO, SAMP),
                                            TEXTURE2D_ARGS(_GrassDiff, SAMP), TEXTURE2D_ARGS(_GrassNrm, SAMP), TEXTURE2D_ARGS(_GrassAO, SAMP), tg, _NormalStrength);
                    g.albedo *= _GrassTint.rgb * (1.0 + (DigFbm(p * 0.25 + 3.0) - 0.5) * 0.5);
                    surface = LerpSample(soil, g, grass);
                }

                // ---- Lighting ---------------------------------------------------
                InputData inputData = (InputData)0;
                inputData.positionWS = p;
                inputData.normalWS = surface.normal;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(p);
            #if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                inputData.shadowCoord = TransformWorldToShadowCoord(p);
            #else
                inputData.shadowCoord = float4(0, 0, 0, 0);
            #endif
                inputData.fogCoord = InitializeInputDataFog(float4(p, 1.0), input.fogFactor);
                inputData.vertexLighting = half3(0, 0, 0);
                inputData.bakedGI = SampleSH(surface.normal);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = surface.albedo;
                surfaceData.metallic = 0;
                surfaceData.specular = half3(0, 0, 0);
                surfaceData.smoothness = 0;
                surfaceData.normalTS = half3(0, 0, 1);
                surfaceData.occlusion = lerp(1.0, surface.ao, _AOStrength);
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
