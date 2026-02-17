Shader "Custom/Triplanar"
{
   Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Tiling ("Tiling", Float) = 1
        _Sharpness ("Sharpness", Float) = 4
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows

        sampler2D _MainTex;
        float _Tiling;
        float _Sharpness;

        struct Input
        {
            float3 worldPos;
            float3 worldNormal;
        };

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            float3 blend = abs(IN.worldNormal);
            blend = pow(blend, _Sharpness);
            blend /= (blend.x + blend.y + blend.z);

            float2 xUV = IN.worldPos.yz * _Tiling;
            float2 yUV = IN.worldPos.xz * _Tiling;
            float2 zUV = IN.worldPos.xy * _Tiling;

            float4 xTex = tex2D(_MainTex, xUV);
            float4 yTex = tex2D(_MainTex, yUV);
            float4 zTex = tex2D(_MainTex, zUV);

            float4 finalColor =
                xTex * blend.x +
                yTex * blend.y +
                zTex * blend.z;

            o.Albedo = finalColor.rgb;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
