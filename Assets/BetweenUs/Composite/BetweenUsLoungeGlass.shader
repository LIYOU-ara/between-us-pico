Shader "Between Us/Composite/Lounge Translucent"
{
    Properties
    {
        _Color ("Soft neutral body tint", Color) = (0.67, 0.70, 0.75, 1)
        _Opacity ("Surface opacity", Range(0,1)) = 0.28
        _RimColor ("Soft contour tint", Color) = (0.92, 0.95, 1, 1)
        _RimStrength ("Contour contribution", Range(0,2)) = 0.30
        _RimPower ("Contour falloff", Range(0.5,6)) = 3.0
        _AccentColor ("Subtle reflected accent", Color) = (0.80, 0.20, 0.24, 1)
        _AccentStrength ("Reflected accent strength", Range(0,1)) = 0.015
        _Roughness ("Broad highlight softness", Range(0,1)) = 0.78
        _FrostGrain ("Stable fine surface grain", Range(0,0.025)) = 0.001
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Face culling", Float) = 2
    }
    SubShader
    {
        // Architecture/furniture stays behind the existing UI glass (3000),
        // ghost participants (3010) and text (3020). No scene lighting is needed.
        Tags { "Queue"="Transparent-20" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend One OneMinusSrcAlpha
        ZWrite Off
        ZTest LEqual
        Cull [_Cull]

        Pass
        {
            Name "SoftLoungeSurface"
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _Color, _RimColor, _AccentColor;
            float _Opacity, _RimStrength, _RimPower, _AccentStrength;
            float _Roughness, _FrostGrain;
            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };
            struct v2f
            {
                float4 position : SV_POSITION;
                float3 worldPosition : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
            };
            v2f vert(appdata v)
            {
                v2f o;
                o.position = UnityObjectToClipPos(v.vertex);
                o.worldPosition = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                return o;
            }
            float4 frag(v2f i, float face : VFACE) : SV_Target
            {
                // Two-sided room planes can use Cull Off without making their
                // reverse surfaces light in the wrong direction.
                float3 normal = normalize(i.worldNormal) * (face >= 0 ? 1.0 : -1.0);
                float3 view = normalize(_WorldSpaceCameraPos - i.worldPosition);
                float facing = saturate(dot(normal, view));
                float rim = pow(1.0 - facing, max(_RimPower, 0.5));
                float roughness = saturate(_Roughness);

                // A fixed, gently wrapped studio key and a weaker fill expose
                // furniture volume while leaving the dialogue panels dominant.
                float3 keyDirection = normalize(float3(-0.42, 0.79, -0.45));
                float3 fillDirection = normalize(float3(0.75, 0.44, 0.49));
                float key = saturate((dot(normal, keyDirection) + 0.32) / 1.32);
                float fill = saturate((dot(normal, fillDirection) + 0.20) / 1.20);
                float3 colour = _Color.rgb * (0.44 + key * 0.39 + fill * 0.13);

                float3 halfDirection = normalize(keyDirection + view);
                float exponent = lerp(72.0, 7.0, roughness);
                float broadHighlight = pow(saturate(dot(normal, halfDirection)), exponent);
                float highlightStrength = lerp(0.15, 0.035, roughness);
                colour += _RimColor.rgb * broadHighlight * highlightStrength;
                colour += _RimColor.rgb * rim * _RimStrength * 0.37;

                float accent = pow(saturate(dot(normal, normalize(float3(0.80, 0.22, -0.55)))), 2.0);
                colour += _AccentColor.rgb * accent * _AccentStrength;
                float grain = frac(sin(dot(i.worldPosition * 53.0,
                    float3(12.9898, 78.233, 37.719))) * 43758.5453) - 0.5;
                colour += grain * _FrostGrain;

                // All light is premultiplied; zero opacity really disappears.
                // Even an accidental opacity of one retains some transmission.
                float opacity = saturate(_Opacity) * saturate(_Color.a);
                float alpha = min(0.88, opacity * (0.82 + 0.18 * facing
                    + rim * _RimStrength * 0.16));
                clip(alpha - 0.0001);
                return float4(saturate(colour) * alpha, alpha);
            }
            ENDCG
        }
    }
    Fallback Off
}
