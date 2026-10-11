// Tezgahtaki yeşil rehber, kili kapatmayacak şekilde iki geçişte çizilir:
//  1) Hedef kilin DIŞINDA kalıyorsa (kil eksik): hafif yeşil dolgu + kenar -> "buraya kil ekle"
//  2) Hedef kilin İÇİNDE kalıyorsa (kil fazla): sadece ince silüet çizgisi -> kil net görünür
Shader "SahteSaheser/ForgeryGhost"
{
    Properties
    {
        _Color ("Renk", Color) = (0.2, 1, 0.35, 1)
        _VisibleFill ("Görünen Dolgu", Range(0, 1)) = 0.16
        _VisibleRim ("Görünen Kenar", Range(0, 2)) = 0.6
        _HiddenRim ("Kil Arkası Kenar", Range(0, 2)) = 0.85
        _RimPower ("Kenar Keskinliği", Range(0.5, 12)) = 4
    }
    SubShader
    {
        Tags { "Queue"="Transparent+50" "RenderType"="Transparent" "IgnoreProjector"="True" }

        CGINCLUDE
        #include "UnityCG.cginc"
        fixed4 _Color;
        float _VisibleFill, _VisibleRim, _HiddenRim, _RimPower;
        struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
        struct v2f { float4 pos : SV_POSITION; float3 n : TEXCOORD0; float3 v : TEXCOORD1; };
        v2f vert (appdata i)
        {
            v2f o;
            o.pos = UnityObjectToClipPos(i.vertex);
            o.n = UnityObjectToWorldNormal(i.normal);
            o.v = normalize(WorldSpaceViewDir(i.vertex));
            return o;
        }
        float rimOf(v2f i) { return pow(1.0 - saturate(dot(normalize(i.n), normalize(i.v))), _RimPower); }
        ENDCG

        // Kilin önünde görünen kısım (kil eksik)
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            ZWrite Off ZTest LEqual Cull Back
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            fixed4 frag (v2f i) : SV_Target
            {
                float r = rimOf(i);
                return fixed4(_Color.rgb + r * 0.25, saturate(_VisibleFill + r * _VisibleRim));
            }
            ENDCG
        }

        // Kilin arkasında kalan kısım (kil fazla): sadece silüet çizgisi
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            ZWrite Off ZTest Greater Cull Back
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            fixed4 frag (v2f i) : SV_Target
            {
                float r = rimOf(i);
                float a = saturate(r * _HiddenRim);
                a = a > 0.12 ? a : 0.0; // iç kısım tamamen şeffaf
                return fixed4(_Color.rgb * 1.2 + 0.1, a);
            }
            ENDCG
        }
    }
}
