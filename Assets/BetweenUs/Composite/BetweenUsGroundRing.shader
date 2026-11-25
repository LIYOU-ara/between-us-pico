Shader "Between Us/Composite/Ground Ring"
{
    Properties
    {
        _Color ("Silver ring / optional emotion tint", Color) = (0.86, 0.91, 1, 1)
        _RimColor ("Hot core tint", Color) = (1, 1, 1, 1)
        _Opacity ("Ring opacity", Range(0,1)) = 0.94
        _Radius ("Ring radius in UV disc", Range(0.1,0.92)) = 0.74
        _Width ("Sharp core width", Range(0.003,0.12)) = 0.018
        _GlowWidth ("Soft red glow width", Range(0.01,0.25)) = 0.085
        _Softness ("Diffuse core softness", Range(0,1)) = 0
        _CoreStrength ("Core contribution", Range(0,1)) = 0.77
        _GlowStrength ("Broad glow contribution", Range(0,1)) = 0.17
        _DiscStrength ("Very faint floor disc", Range(0,0.15)) = 0.018
        _PulseSpeed ("Pulse radians per second", Range(0,3)) = 0.7
        _PulseStrength ("Subtle breathing", Range(0,0.3)) = 0.07
        _PulseTime ("Explicit pulse time / -1 for live", Float) = -1
        _ShowcaseTime ("Shared export time / -1 for live", Float) = -1
    }
    SubShader
    {
        Tags { "Queue"="Transparent-10" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend One OneMinusSrcAlpha
        Cull Off
        ZWrite Off
        ZTest LEqual
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 _Color, _RimColor;
            float _Opacity, _Radius, _Width, _GlowWidth, _DiscStrength;
            float _Softness, _CoreStrength, _GlowStrength;
            float _PulseSpeed, _PulseStrength, _PulseTime, _ShowcaseTime;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(appdata v)
            {
                v2f o; o.position = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                float time = _PulseTime >= 0 ? _PulseTime : (_ShowcaseTime >= 0 ? _ShowcaseTime : _Time.y);
                float r = length((i.uv - 0.5) * 2.0);
                float aa = max(fwidth(r), 0.0004);
                float edgeDistance = abs(r - _Radius);
                float sharpCore = 1.0 - smoothstep(_Width, _Width + aa, edgeDistance);
                // Softness zero preserves the original core. At one, a broad Gaussian
                // removes the hard line without raising the peak opacity.
                float diffuseWidth = max(max(_Width, _GlowWidth * 0.58), aa);
                float diffuseCore = exp2(-pow(edgeDistance / diffuseWidth, 2.0) * 3.2);
                float core = lerp(sharpCore, diffuseCore, saturate(_Softness));
                float soft = exp2(-pow(edgeDistance / max(_GlowWidth, 0.01), 2.0) * 3.2);
                float floorDisc = 1.0 - smoothstep(_Radius - 0.10, _Radius + 0.03, r);
                // Every highlight is clipped inside the circular quad silhouette.
                float silhouette = 1.0 - smoothstep(0.94, 0.995, r);
                float pulse = 1.0 + sin(time * _PulseSpeed) * _PulseStrength;
                float alpha = saturate((core * _CoreStrength + soft * _GlowStrength + floorDisc * _DiscStrength) * pulse * _Opacity * _Color.a) * silhouette;
                clip(alpha - 0.00015);
                float3 colour = lerp(_Color.rgb, _RimColor.rgb, core * 0.27);
                return float4(colour * alpha, alpha);
            }
            ENDCG
        }
    }
    Fallback Off
}
