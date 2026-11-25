Shader "Between Us/Portfolio/Holographic Panel"
{
    Properties
    {
        [HDR] _AccentColor ("Accent / illuminated edge", Color) = (1, 0.15, 0.20, 1)
        _FillColor ("Interior / transparent charcoal", Color) = (0.018, 0.012, 0.020, 0.86)
        _Opacity ("Overall opacity", Range(0, 1)) = 1
        _Aspect ("Panel width / height", Float) = 2.35
        _Radius ("Corner radius / panel height", Range(0.005, 0.25)) = 0.065
        _EdgeWidth ("Illuminated border width", Range(0.0005, 0.015)) = 0.0017
        _GlowWidth ("Outer glow width", Range(0.002, 0.08)) = 0.023
        _GlowStrength ("Outer glow intensity", Range(0, 1)) = 0.40
        _SweepSpeed ("Edge highlight speed", Range(0, 2)) = 0.32
        _ScanStrength ("Interior scanline strength", Range(0, 0.25)) = 0.035
        _PulseStrength ("Gentle edge breathing", Range(0, 0.25)) = 0.055
        _ShowcaseTime ("Preview time / -1 for animation", Float) = -1
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
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

            float4 _AccentColor;
            float4 _FillColor;
            float _Opacity, _Aspect, _Radius, _EdgeWidth;
            float _GlowWidth, _GlowStrength, _SweepSpeed, _ScanStrength;
            float _PulseStrength, _ShowcaseTime;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
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
                return o;
            }

            // Distances are measured in panel-height units, so wide panels retain
            // circular corners and a consistent border thickness.
            float RoundedRectangle(float2 p, float2 halfExtent, float radius)
            {
                float2 q = abs(p) - halfExtent + radius;
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - radius;
            }

            float4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float time = _ShowcaseTime >= 0.0 ? _ShowcaseTime : _Time.y;
                float aspect = max(_Aspect, 0.1);
                float glowWidth = max(_GlowWidth, 0.001);
                // A transparent margin keeps the exterior halo inside the quad.
                float margin = min(0.14, glowWidth * 2.7 + _EdgeWidth * 2.0);
                float2 halfExtent = max(float2(aspect, 1.0) * 0.5 - margin, 0.015);
                float radius = min(_Radius, min(halfExtent.x, halfExtent.y) * 0.95);
                float2 p = (i.uv - 0.5) * float2(aspect, 1.0);
                float distance = RoundedRectangle(p, halfExtent, radius);
                float aa = max(fwidth(distance), 0.00035);
                float interior = 1.0 - smoothstep(-aa, aa, distance);
                float border = 1.0 - smoothstep(_EdgeWidth, _EdgeWidth + aa, abs(distance));

                // The highlight follows the perimeter rather than crossing text.
                float angle = atan2(p.y / halfExtent.y, p.x / halfExtent.x) * 0.159154943 + 0.5;
                float cycle = frac(angle - time * _SweepSpeed * 0.16);
                float trailDistance = min(cycle, 1.0 - cycle);
                float highlight = exp2(-trailDistance * trailDistance * 900.0);
                float breath = 1.0 - _PulseStrength * (0.5 + 0.5 * sin(time * 1.35));

                float outerGlow = exp2(-max(distance, 0.0) / glowWidth * 2.1);
                outerGlow *= smoothstep(-aa, aa, distance);
                // Suppress the barely visible tail at the mesh boundary.
                float2 quadEdge = min(i.uv, 1.0 - i.uv);
                outerGlow *= smoothstep(0.0, 0.025, min(quadEdge.x, quadEdge.y));
                float glowAlpha = saturate(outerGlow * _GlowStrength * breath * (0.65 + highlight * 0.35));
                float fillAlpha = interior * _FillColor.a;
                float edgeAlpha = saturate(border * (0.72 + highlight * 0.28) * _AccentColor.a * breath);

                // A quiet, narrow interior sweep and fine scan texture require
                // neither a screen grab nor post-processing bloom.
                float scanWave = 0.5 + 0.5 * sin(i.uv.y * 950.0 + time * 0.7);
                float scan = pow(scanWave, 14.0) * _ScanStrength;
                float sweepPosition = frac(time * _SweepSpeed * 0.085);
                float sweepDistance = abs(i.uv.x + i.uv.y * 0.11 - sweepPosition * 1.25);
                float softSweep = exp2(-sweepDistance * sweepDistance * 110.0) * 0.018;
                float innerRim = exp2(-max(-distance, 0.0) / 0.085 * 2.0) * 0.022;
                float3 fillColor = _FillColor.rgb + _AccentColor.rgb * (scan + softSweep + innerRim);
                float3 edgeColor = lerp(_AccentColor.rgb, float3(1.0, 0.88, 0.80), highlight * 0.72);

                // Explicit premultiplied source-over compositing: glow, glass,
                // then luminous border. This also preserves correct fades.
                float3 color = _AccentColor.rgb * glowAlpha * (1.0 - fillAlpha) * (1.0 - edgeAlpha);
                color += fillColor * fillAlpha * (1.0 - edgeAlpha);
                color += edgeColor * edgeAlpha;
                float alpha = 1.0 - (1.0 - glowAlpha) * (1.0 - fillAlpha) * (1.0 - edgeAlpha);
                return float4(color * _Opacity, alpha * _Opacity);
            }
            ENDCG
        }
    }
    Fallback Off
}
