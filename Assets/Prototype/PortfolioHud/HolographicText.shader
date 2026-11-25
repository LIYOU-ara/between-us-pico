Shader "Between Us/Portfolio/Holographic Text"
{
    Properties
    {
        [PerRendererData] _MainTex ("Font atlas / TextMesh texture", 2D) = "white" {}
        _TextColor ("Readable text tint", Color) = (0.98, 0.97, 0.96, 1)
        [HDR] _AccentColor ("Halo / highlight accent", Color) = (1, 0.16, 0.22, 1)
        _OutlineColor ("Contrast outline", Color) = (0.004, 0.005, 0.010, 0.95)
        _OutlinePixels ("Outline / atlas pixels", Range(0, 2)) = 0.7
        _GlowPixels ("Soft halo / atlas pixels", Range(0.5, 4)) = 1.6
        _GlowStrength ("Soft halo strength", Range(0, 1)) = 0.32
        _Opacity ("Overall opacity", Range(0, 1)) = 1
        _SweepSpeed ("Highlight speed", Range(0, 2)) = 0.32
        _SweepStrength ("Text highlight amount", Range(0, 0.5)) = 0.12
        _PulseStrength ("Gentle halo breathing", Range(0, 0.25)) = 0.04
        _ShowcaseTime ("Preview time / -1 for animation", Float) = -1
    }

    SubShader
    {
        Tags { "Queue"="Transparent+10" "RenderType"="Transparent" "IgnoreProjector"="True" }
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
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float4 _TextColor, _AccentColor, _OutlineColor;
            float _OutlinePixels, _GlowPixels, _GlowStrength, _Opacity;
            float _SweepSpeed, _SweepStrength, _PulseStrength, _ShowcaseTime;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                float localX : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                o.localX = v.vertex.x;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float time = _ShowcaseTime >= 0.0 ? _ShowcaseTime : _Time.y;
                // Nine atlas samples retain the original sharp glyph coverage.
                // This shader does not require a distance-field font asset.
                float core = tex2D(_MainTex, i.uv).a;
                float2 outlineStep = _MainTex_TexelSize.xy * _OutlinePixels;
                float outline = max(
                    max(tex2D(_MainTex, i.uv + float2(outlineStep.x, 0)).a,
                        tex2D(_MainTex, i.uv - float2(outlineStep.x, 0)).a),
                    max(tex2D(_MainTex, i.uv + float2(0, outlineStep.y)).a,
                        tex2D(_MainTex, i.uv - float2(0, outlineStep.y)).a));
                float2 glowStep = _MainTex_TexelSize.xy * _GlowPixels;
                float halo = (
                    tex2D(_MainTex, i.uv + float2(glowStep.x, glowStep.y)).a +
                    tex2D(_MainTex, i.uv + float2(-glowStep.x, glowStep.y)).a +
                    tex2D(_MainTex, i.uv + float2(glowStep.x, -glowStep.y)).a +
                    tex2D(_MainTex, i.uv - float2(glowStep.x, glowStep.y)).a) * 0.25;

                // Local position keeps the sweep continuous across glyphs even
                // when their individual UVs occupy unrelated atlas rectangles.
                float phase = frac(i.localX * 0.45 - time * _SweepSpeed * 0.24);
                float highlight = pow(saturate(1.0 - abs(phase * 2.0 - 1.0)), 12.0);
                float breath = 1.0 - _PulseStrength * (0.5 + 0.5 * sin(time * 1.35));
                float coreAlpha = saturate(core * _TextColor.a);
                float outlineAlpha = saturate(outline * _OutlineColor.a);
                float haloAlpha = saturate(halo * _GlowStrength * breath * (1.0 + highlight * 0.25));
                float3 coreColor = i.color.rgb * _TextColor.rgb;
                coreColor = lerp(coreColor, float3(1.0, 0.91, 0.87), highlight * _SweepStrength);

                float3 color = _AccentColor.rgb * haloAlpha * (1.0 - outlineAlpha) * (1.0 - coreAlpha);
                color += _OutlineColor.rgb * outlineAlpha * (1.0 - coreAlpha);
                color += coreColor * coreAlpha;
                float alpha = 1.0 - (1.0 - haloAlpha) * (1.0 - outlineAlpha) * (1.0 - coreAlpha);
                float fade = saturate(i.color.a * _Opacity);
                return float4(color * fade, alpha * fade);
            }
            ENDCG
        }
    }
    Fallback Off
}
