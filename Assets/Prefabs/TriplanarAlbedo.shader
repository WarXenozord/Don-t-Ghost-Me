Shader "Custom/Triplanar"
{
   Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        _NormalMap ("Normal Map", 2D) = "bump" {}
        _Tiling ("Tiling", Float) = 1
        _Sharpness ("Blend Sharpness", Float) = 4
        _NormalStrength ("Normal Strength", Range(0,2)) = 1
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
        float _NormalStrength;

        struct Input
        {
            float3 worldPos;
            float3 worldNormal;
            float3 viewDir;
        };

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            float3 worldNormal = normalize(IN.worldNormal);

            // Blend weights
            float3 blend = abs(worldNormal);
            blend = pow(blend, _Sharpness);
            blend /= (blend.x + blend.y + blend.z + 0.0001);

            // UV projections
            float2 xUV = IN.worldPos.yz * _Tiling;
            float2 yUV = IN.worldPos.xz * _Tiling;
            float2 zUV = IN.worldPos.xy * _Tiling;

            // Albedo
            float3 xCol = tex2D(_MainTex, xUV).rgb;
            float3 yCol = tex2D(_MainTex, yUV).rgb;
            float3 zCol = tex2D(_MainTex, zUV).rgb;

            o.Albedo = xCol * blend.x +
                       yCol * blend.y +
                       zCol * blend.z;

            // ---- NORMALS ----
            // Sample tangent normals
            float3 xN = UnpackNormal(tex2D(_NormalMap, xUV));
            float3 yN = UnpackNormal(tex2D(_NormalMap, yUV));
            float3 zN = UnpackNormal(tex2D(_NormalMap, zUV));

            // Re-orient them into world space manually
            float3 worldXN = float3( xN.z, xN.y, xN.x );
            float3 worldYN = float3( yN.x, yN.z, yN.y );
            float3 worldZN = float3( zN.x, zN.y, zN.z );

            float3 blendedNormal =
                worldXN * blend.x +
                worldYN * blend.y +
                worldZN * blend.z;

            blendedNormal = normalize(blendedNormal);

            // Convert back to tangent space for surface shader
            o.Normal = normalize(blendedNormal) * _NormalStrength;
        }
        ENDCG
    }
    FallBack "Standard"
}
