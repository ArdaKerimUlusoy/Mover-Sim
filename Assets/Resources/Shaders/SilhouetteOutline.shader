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
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
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
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = pos.positionCS;
                output.fogFactor = ComputeFogFactor(pos.positionCS.z);
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
