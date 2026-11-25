Shader "Hidden/Between Us/Camera Glass Blur"
{
    Properties
    {
        _MainTex ("Camera frame", 2D) = "black" {}
        _BlurRadius ("Blur radius", Range(.5, 4)) = 2
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex;
        float4 _MainTex_TexelSize;
        float _BlurRadius;

        // Symmetric nine-tap Gaussian. Each sample stays within the source;
        // camera orientation is retained for the panel's calibrated projection.
        half4 Gaussian(float2 uv, float2 axis)
        {
            float2 step = axis * abs(_MainTex_TexelSize.xy) * _BlurRadius;
            half3 color = tex2D(_MainTex, saturate(uv)).rgb * .2270270270h;
            color += (tex2D(_MainTex, saturate(uv + step)).rgb + tex2D(_MainTex, saturate(uv - step)).rgb) * .1945945946h;
            color += (tex2D(_MainTex, saturate(uv + step * 2)).rgb + tex2D(_MainTex, saturate(uv - step * 2)).rgb) * .1216216216h;
            color += (tex2D(_MainTex, saturate(uv + step * 3)).rgb + tex2D(_MainTex, saturate(uv - step * 3)).rgb) * .0540540541h;
            color += (tex2D(_MainTex, saturate(uv + step * 4)).rgb + tex2D(_MainTex, saturate(uv - step * 4)).rgb) * .0162162162h;
            return half4(color, 1);
        }
        half4 Horizontal(v2f_img i) : SV_Target { return Gaussian(i.uv, float2(1, 0)); }
        half4 Vertical(v2f_img i) : SV_Target { return Gaussian(i.uv, float2(0, 1)); }
        ENDCG
        Pass
        {
            Name "Horizontal"
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment Horizontal
            ENDCG
        }
        Pass
        {
            Name "Vertical"
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment Vertical
            ENDCG
        }
    }
    Fallback Off
}
