Shader "Custom/TriplanarAlbedo"
{
   Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        _NormalMap ("Normal Map", 2D) = "bump" {}
        _Tiling ("Tiling", Float) = 1
        _Sharpness ("Blend Sharpness", Float) = 4
        _Metallic ("Metallic", Range(0,1)) = 0
        _Glossiness ("Smoothness", Range(0,1)) = 0.5
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 300

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0

        sampler2D _MainTex;
        sampler2D _NormalMap;

        float _Tiling;
        float _Sharpness;
        half _Metallic;
        half _Glossiness;

        struct Input
        {
            float3 worldPos;
            float3 worldNormal;
            INTERNAL_DATA
        };

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            float3 n = normalize(IN.worldNormal);

            // Blend weights
            float3 blend = abs(n);
            blend = pow(blend, _Sharpness);
            blend /= (blend.x + blend.y + blend.z + 0.0001);

            // UVs
            float2 uvX = IN.worldPos.yz * _Tiling;
            float2 uvY = IN.worldPos.xz * _Tiling;
            float2 uvZ = IN.worldPos.xy * _Tiling;

            // Albedo samples
            float3 colX = tex2D(_MainTex, uvX).rgb;
            float3 colY = tex2D(_MainTex, uvY).rgb;
            float3 colZ = tex2D(_MainTex, uvZ).rgb;

            o.Albedo = colX * blend.x +
                       colY * blend.y +
                       colZ * blend.z;

            // NORMALS (simple & safe blend)
            float3 nX = UnpackNormal(tex2D(_NormalMap, uvX));
            float3 nY = UnpackNormal(tex2D(_NormalMap, uvY));
            float3 nZ = UnpackNormal(tex2D(_NormalMap, uvZ));

            float3 blendedNormal =
                nX * blend.x +
                nY * blend.y +
                nZ * blend.z;

            o.Normal = normalize(blendedNormal);

            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
        }
        ENDCG
    }

    FallBack "Standard"
}
