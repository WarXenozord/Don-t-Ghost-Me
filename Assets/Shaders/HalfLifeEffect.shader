Shader "Custom/HalfLifeEffect"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _BlurSize ("Blur Size", Float) = 1.0
        _GrayscaleAmount ("Grayscale Amount", Range(0,1)) = 1
    }

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _BlurSize;
            float _GrayscaleAmount;
            float4 _MainTex_TexelSize;

            fixed4 frag(v2f_img i) : SV_Target
            {
                float2 uv = i.uv;

                // Simple 4-sample blur
                fixed4 col = tex2D(_MainTex, uv) * 0.4;
                col += tex2D(_MainTex, uv + float2(_MainTex_TexelSize.x, 0) * _BlurSize) * 0.15;
                col += tex2D(_MainTex, uv - float2(_MainTex_TexelSize.x, 0) * _BlurSize) * 0.15;
                col += tex2D(_MainTex, uv + float2(0, _MainTex_TexelSize.y) * _BlurSize) * 0.15;
                col += tex2D(_MainTex, uv - float2(0, _MainTex_TexelSize.y) * _BlurSize) * 0.15;

                // Convert to grayscale
                float gray = dot(col.rgb, float3(0.299, 0.587, 0.114));
                col.rgb = lerp(col.rgb, float3(gray, gray, gray), _GrayscaleAmount);

                return col;
            }
            ENDCG
        }
    }
}