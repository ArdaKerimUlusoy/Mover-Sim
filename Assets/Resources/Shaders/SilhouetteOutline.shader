// Siluet çizgisi (SilhouetteOutline.cs): ters kabuk alt mesh'i için düz renk, ışıksız. Stencil'de aynı karakterin numarası
// (SilhouetteMask) olan piksellere çizilmez → yalnızca siluetin dışındaki halka kalır (burun / kulak / siperlik içi çizgisi yok).
// Kuyruk: Transparent-498 (maskeden hemen sonra). DepthOnly / ShadowCaster geçişi bilerek YOK:
// derinlik öncelemede kabuk yüzün önünü kapatmasın, kabuk gölge düşürmesin.
Shader "Soygun/SilhouetteOutline"
{
    Properties
    {
        _Color ("Renk", Color) = (0.02, 0.015, 0.015, 1)
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        [IntRange] _StencilRef ("Stencil Ref (karakter numarası)", Range(1, 15)) = 8
        // Uzakta (ve aynada) çizgi incelip kaybolmasın: kabuk, ekranda en az _MinPixels piksel kalacak kadar dışa itilir.
        // Mesh'in kabuk köşelerinin normali "dışa doğru" yönle değiştirilmiş olmalı (SilhouetteOutline.cs) → _Widen = 1.
        _MinPixels ("En ince çizgi (piksel)", Float) = 2.2
        _BaseHull ("Kabuğun kendi kalınlığı (m)", Float) = 0.014
        _MaxExtra ("En çok ek kalınlık (m)", Float) = 0.06
        _Widen ("Kalınlaştır (0/1)", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-498" "IgnoreProjector" = "True" }

        Pass
        {
            Name "SilhouetteOutline"
            Tags { "LightMode" = "UniversalForwardOnly" }

            Cull [_Cull]
            ZWrite On
            ZTest LEqual

            Stencil
            {
                Ref [_StencilRef]
                ReadMask 15
                WriteMask 15
                Comp NotEqual
                Pass Keep
            }

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _MinPixels;
                float _BaseHull;
                float _MaxExtra;
                float _Widen;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half fogFactor : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float3 posWS = TransformObjectToWorld(input.positionOS.xyz);
                if (_Widen > 0.5)
                {
                    // Bu derinlikte bir pikselin dünya boyu: 2 * z / (P11 * hedef yüksekliği). Kabuk zaten _BaseHull kalın;
                    // eksik kalan kadar dışa (normal = dışa yön) itilir. Yakında ek yok (görünüm eskisi gibi).
                    float zView = max(0.01, -TransformWorldToView(posWS).z);
                    float pixWorld = 2.0 * zView / (abs(UNITY_MATRIX_P[1][1]) * max(1.0, _ScreenParams.y));
                    float extra = clamp(_MinPixels * pixWorld - _BaseHull, 0.0, _MaxExtra);
                    float3 nWS = TransformObjectToWorldNormal(input.normalOS);
                    posWS += nWS * extra;
                }
                output.positionCS = TransformWorldToHClip(posWS);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half3 color = MixFog(_Color.rgb, input.fogFactor); // eski URP Lit kabuk gibi sise karışsın
                return half4(color, 1);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
