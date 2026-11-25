Shader "Between Us/Composite/Stage Beam"
{
    Properties
    {
        _Color ("Warm white scattering", Color) = (1, 0.91, 0.80, 1)
        _Opacity ("Scattering opacity", Range(0, 0.5)) = 0.055
        _Intensity ("Scattering colour strength", Range(0, 3)) = 1
        _EdgeSoftness ("Silhouette softness", Range(0.05, 1)) = 0.65
        _TipFade ("Soft start / cone length", Range(0.005, 0.3)) = 0.065
        _EndFade ("Soft end / cone length", Range(0.05, 0.7)) = 0.30
        _LengthFalloff ("Scattering loss toward floor", Range(0, 1)) = 0.72
        _BackfaceContribution ("Far shell contribution", Range(0, 0.5)) = 0.08
        [Toggle] _GroundMode ("Ground pool instead of cone shell", Float) = 0
        _PoolRadius ("Pool radius / quad half-width", Range(0.1, 1)) = 0.88
        _PoolFalloff ("Pool Gaussian falloff", Range(1, 8)) = 3.4
    }
    SubShader
    {
        Tags
        {
            "Queue"="Transparent-20"
            "RenderType"="Transparent"
            "IgnoreProjector"="True"
            "DisableBatching"="False"
        }
        Blend One OneMinusSrcAlpha
        ZWrite Off
        ZTest LEqual
        Cull Off
        Lighting Off
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _Color;
            float _Opacity, _Intensity, _EdgeSoftness;
            float _TipFade, _EndFade, _LengthFalloff, _BackfaceContribution;
            float _GroundMode, _PoolRadius, _PoolFalloff;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };
            struct v2f
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
            };
            v2f vert(appdata v)
            {
                v2f o;
                o.position = UnityObjectToClipPos(v.vertex);
                o.positionWS = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.normalWS = UnityObjectToWorldNormal(v.normal);
                o.uv = v.uv;
                return o;
            }
            float4 frag(v2f i, float face : VFACE) : SV_Target
            {
                float density;
                UNITY_BRANCH
                if (_GroundMode > 0.5)
                {
                    // A single horizontal quad with ordinary 0..1 UVs. A smooth
                    // outer cutoff prevents a rectangular footprint or hard disc.
                    float2 p = (i.uv - 0.5) * 2.0;
                    float radius = length(p);
                    float normalizedRadius = radius / max(_PoolRadius, 0.05);
                    float gaussian = exp2(-normalizedRadius * normalizedRadius
                        * max(_PoolFalloff, 0.1));
                    float outerStart = lerp(0.94, 0.70, saturate(_EdgeSoftness));
                    float outerFade = 1.0 - smoothstep(outerStart, 1.0, radius);
                    density = gaussian * outerFade;
                }
                else
                {
                    // Cone UV.y is zero at the source and one at the floor.
                    // UV.x wraps around its circumference and is deliberately
                    // unused: the angular seam cannot produce a visible streak.
                    float t = saturate(i.uv.y);
                    float startFade = smoothstep(0.0, max(_TipFade, 0.001), t);
                    float endFade = 1.0 - smoothstep(1.0 - max(_EndFade, 0.001), 1.0, t);
                    float3 normalWS = normalize(i.normalWS);
                    float3 perspectiveView = _WorldSpaceCameraPos.xyz - i.positionWS;
                    float3 orthographicView = UNITY_MATRIX_V[2].xyz;
                    float3 viewWS = normalize(lerp(perspectiveView, orthographicView, unity_OrthoParams.w));
                    float facing = saturate(abs(dot(normalWS, viewWS)));
                    // Grazing shell surfaces vanish smoothly. The broad facing
                    // region reads as a filled beam rather than a wire cone.
                    float crossSection = pow(facing, lerp(0.8, 3.2, saturate(_EdgeSoftness)));
                    float attenuation = lerp(1.0, 1.0 - saturate(_LengthFalloff), t * t);
                    // Suppress the far surface so a closed shell does not become
                    // twice as bright. Keep Cull Off for flexible mesh winding.
                    float shellWeight = face >= 0 ? 1.0 : saturate(_BackfaceContribution);
                    density = crossSection * startFade * endFade * attenuation * shellWeight;
                }
                float alpha = saturate(density * _Opacity * _Color.a);
                // Fixed deterministic material: no noise animation, time input,
                // GrabPass, scene depth sampling or full-screen post-processing.
                return float4(max(_Color.rgb, 0.0) * max(_Intensity, 0.0) * alpha, alpha);
            }
            ENDCG
        }
    }
    Fallback Off
}
