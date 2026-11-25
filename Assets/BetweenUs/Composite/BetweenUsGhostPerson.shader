Shader "Between Us/Composite/Ghost Person"
{
    Properties
    {
        _Color ("Cool grey body", Color) = (0.70, 0.73, 0.77, 1)
        _Opacity ("Body opacity", Range(0,1)) = 0.28
        _RimColor ("Soft white contour", Color) = (0.91, 0.94, 0.98, 1)
        _RimStrength ("Contour light", Range(0,2)) = 0.68
        _RimPower ("Contour falloff", Range(0.5,6)) = 2.6
        _AccentColor ("Red reflection", Color) = (1, 0.18, 0.23, 1)
        _AccentStrength ("Red reflection strength", Range(0,1)) = 0.035
        // Stage light is opt-in per material. Direction follows Light.forward.
        _StageStrength ("Stage lighting strength (0 = legacy look)", Range(0,4)) = 0
        _StageLightPosition ("Stage light world position", Vector) = (-2.1,7.6,0.1,1)
        _StageLightDirection ("Stage light world forward", Vector) = (0,-1,0,0)
        _StageSpotCos ("Stage spot outer cosine", Range(-1,1)) = 0.945519
        _StageSpotSoftness ("Stage spot feather (cosine width)", Range(0.001,0.5)) = 0.035
        _StageLightRange ("Stage light world range", Float) = 12
        [HDR] _StageColor ("Stage warm key colour", Color) = (1,0.91,0.78,1)
        _StageAmbient ("Stage shadow fill / original surface", Range(0,1)) = 0.28
        _StageRimDirection ("Stage rim direction (surface toward light)", Vector) = (-0.7,0.35,0.6,0)
        [HDR] _StageRimColor ("Stage side rim colour", Color) = (0.78,0.88,1,1)
        _StageRimStrength ("Stage side rim strength", Range(0,3)) = 0.4
        _StageRimPower ("Stage side rim falloff", Range(0.5,8)) = 2.6
        [Toggle] _DepthWrite ("Front-surface depth prepass", Float) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Face culling", Float) = 2
    }
    SubShader
    {
        Tags { "Queue"="Transparent+10" "RenderType"="Transparent" "IgnoreProjector"="True" }
        // Draw after the glass colour pass, retaining the glass seen through
        // this front surface, but reject the model's hidden/internal surfaces.
        Pass
        {
            Name "NearestSurfaceDepth"
            ColorMask 0
            ZWrite [_DepthWrite]
            ZTest LEqual
            Cull [_Cull]
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex depthVert
            #pragma fragment depthFrag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; };
            float4 depthVert(appdata v) : SV_POSITION { return UnityObjectToClipPos(v.vertex); }
            float4 depthFrag() : SV_Target { return 0; }
            ENDCG
        }
        Pass
        {
            Name "GhostSurface"
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull [_Cull]
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 _Color, _RimColor, _AccentColor;
            float _Opacity, _RimStrength, _RimPower, _AccentStrength;
            float4 _StageLightPosition, _StageLightDirection, _StageColor;
            float4 _StageRimDirection, _StageRimColor;
            float _StageStrength, _StageSpotCos, _StageSpotSoftness, _StageLightRange;
            float _StageAmbient, _StageRimStrength, _StageRimPower;
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f
            {
                float4 position : SV_POSITION;
                float3 worldPosition : TEXCOORD0;
                float3 normal : TEXCOORD1;
            };
            v2f vert(appdata v)
            {
                v2f o;
                o.position = UnityObjectToClipPos(v.vertex);
                o.worldPosition = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.normal = UnityObjectToWorldNormal(v.normal);
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                float3 normal = normalize(i.normal);
                float3 view = normalize(_WorldSpaceCameraPos - i.worldPosition);
                float facing = saturate(abs(dot(normal, view)));
                float rim = pow(1.0 - facing, max(_RimPower, 0.5));
                float key = 0.58 + 0.42 * saturate(dot(normal, normalize(float3(-0.45, 0.75, -0.5))));
                float redReflection = pow(saturate(dot(normal, normalize(float3(0.8, 0.25, -0.5)))), 2.0);
                float3 colour = _Color.rgb * key;
                colour += _RimColor.rgb * rim * _RimStrength * 0.62;
                colour += _AccentColor.rgb * redReflection * _AccentStrength;
                // The disabled path retains the original surface, alpha and depth behaviour.
                // This explicit world-space light also works in this unlit transparent pass.
                float stageEnabled = saturate(_StageStrength);
                if (_StageStrength > 0.0001)
                {
                    float3 lightOffset = _StageLightPosition.xyz - i.worldPosition;
                    float lightDistance = max(length(lightOffset), 0.0001);
                    float3 towardLight = lightOffset / lightDistance;
                    float3 lightForward = normalize(_StageLightDirection.xyz + float3(0,-0.000001,0));
                    float spotCosine = dot(-towardLight, lightForward);
                    float outerCosine = clamp(_StageSpotCos, -1.0, 0.9998);
                    float innerCosine = min(0.9999, outerCosine + max(_StageSpotSoftness, 0.0001));
                    float spot = smoothstep(outerCosine, innerCosine, spotCosine);
                    // A smooth finite range avoids the harsh falloff of an inverse-square point.
                    float rangeFraction = lightDistance / max(_StageLightRange, 0.01);
                    float rangeFade = saturate(1.0 - pow(rangeFraction, 4.0));
                    float stageField = spot * rangeFade * rangeFade;
                    float diffuse = pow(saturate((dot(normal, towardLight) + 0.08) / 1.08), 1.15);
                    float3 halfDirection = normalize(towardLight + view + float3(0,0.000001,0));
                    float softHighlight = pow(saturate(dot(normal, halfDirection)), 24.0) * 0.085;
                    // Reduce the old fill for a theatrical contrast without changing opacity.
                    colour *= lerp(1.0, saturate(_StageAmbient), stageEnabled);
                    colour += _StageColor.rgb * _StageStrength * stageField * (0.90 * diffuse + softHighlight);
                    float3 rimDirection = normalize(_StageRimDirection.xyz + float3(0,0.000001,0));
                    float rimFacingLight = saturate(dot(normal, rimDirection) * 0.65 + 0.35);
                    float sideRim = pow(1.0 - facing, max(_StageRimPower, 0.5)) * rimFacingLight;
                    colour += _StageRimColor.rgb * sideRim * _StageRimStrength * _StageStrength * stageField * 0.60;
                }
                float alpha = saturate(_Opacity * _Color.a * (0.72 + 0.28 * facing) + rim * _RimStrength * 0.20);
                // Keep modest HDR headroom for stage highlights, with a bounded final output.
                float3 surfaceColour = lerp(saturate(colour), min(max(colour, 0.0), 1.18), stageEnabled);
                return float4(surfaceColour * alpha, alpha);
            }
            ENDCG
        }
    }
    Fallback Off
}
