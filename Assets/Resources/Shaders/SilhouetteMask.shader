// Siluet maskesi (SilhouetteOutline.cs): karakterin kabuk OLMAYAN üçgenleri renk / derinlik yazmadan stencil'in alt 4 bitine
// karakterin numarasını yazar. Kabuk (SilhouetteOutline.shader) bu numaranın olduğu yere çizilmez → iç çizgiler kalkar.
// Kuyruk: Transparent-499 (opaklardan sonra; derinlik önceleme açıkken URP opak geçişi ZTest Equal'a zorlar, burada zorlamaz).
// Gölge geçişi YOK: "yalnızca gölge" modundaki renderer'da (sahibinin kafası) maske hiç çizilmez.
Shader "Soygun/SilhouetteMask"
{
    Properties
    {
        [IntRange] _StencilRef ("Stencil Ref (karakter numarası)", Range(1, 15)) = 8
        [HideInInspector] _MaskPad ("", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-499" "IgnoreProjector" = "True" }

        Pass
        {
            Name "SilhouetteMask"
            Tags { "LightMode" = "UniversalForwardOnly" }

            ColorMask 0
            ZWrite Off
            ZTest LEqual
            Cull Off          // çift yüzlü parçalar (siperlik, askı) arkadan görünse de maskelensin; gizli yüzler derinlikten geçemez
            Offset -1, -1     // kendi gövdesinin derinliğiyle birebir aynı: z-çekişmesi olmasın

            Stencil
            {
                Ref [_StencilRef]
                ReadMask 15
                WriteMask 15
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _MaskPad;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz); // deri (skinning) Unity'de zaten uygulanmış
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return half4(0, 0, 0, _MaskPad); // ColorMask 0: hiçbir şey yazılmaz
            }
            ENDHLSL
        }
    }

    FallBack Off
}
