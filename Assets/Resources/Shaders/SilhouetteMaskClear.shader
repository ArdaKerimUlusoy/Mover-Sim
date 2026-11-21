// Siluet maskesini temizleme (SilhouetteOutline.cs): çizgiden SONRA maskenin yazdığı stencil bitlerini sıfırlar.
// Aynanın kalıcı render dokusunda stencil kareler arası temizlenmeyebiliyor; birikince çizginin halkası da "kendi
// karakterim" sayılıp çizilmiyordu (aynada hiç dış çizgi yok). Maske alt mesh'i bu malzemeyle ikinci kez çizilir.
Shader "Soygun/SilhouetteMaskClear"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-497" "IgnoreProjector" = "True" }

        Pass
        {
            Name "SilhouetteMaskClear"
            Tags { "LightMode" = "UniversalForwardOnly" }

            ColorMask 0
            ZWrite Off
            ZTest Always      // maskenin (ZTest LEqual) yazmış olabileceği her yeri kapsar
            Cull Off

            Stencil
            {
                Ref 0
                WriteMask 15
                Comp Always
                Pass Zero
            }

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
