Shader "Between Us/Composite/Frosted Vortex Glass"
{
    Properties
    {
        _FrostColor ("Smoked frosted glass", Color) = (0.12, 0.125, 0.14, 1)
        _TintA ("Deep red flow", Color) = (0.36, 0.035, 0.065, 1)
        _TintB ("Coral red flow", Color) = (0.78, 0.16, 0.20, 1)
        _TintStrength ("Red film strength", Range(0, 1)) = 0.38
        _Opacity ("Glass opacity", Range(0, 1)) = 0.78
        _Aspect ("Panel width / height", Float) = 2.4
        _Radius ("Corner radius / panel height", Range(0.002, 0.25)) = 0.07
        [Toggle] _TailEnabled ("Speech bubble tail", Float) = 0
        _TailAnchor ("Tail anchor / panel UV X", Range(0, 1)) = 0.68
        _TailLength ("Tail length / original panel height", Range(0.02, 0.8)) = 0.32
        _TailWidth ("Tail root width / original panel height", Range(0.04, 1.2)) = 0.45
        _TailLean ("Tail tip offset / original panel height", Range(-0.8, 0.8)) = -0.15
        _EdgeWidth ("Silver edge width / panel height", Range(0.0003, 0.02)) = 0.0018
        _BevelWidth ("Soft internal bevel", Range(0.002, 0.12)) = 0.028
        _RimColor ("Silver reflection", Color) = (0.87, 0.89, 0.94, 1)
        _RimStrength ("Silver edge definition", Range(0, 1)) = 0.68
        _FlowIntensity ("Traveling silver reflection", Range(0, 2)) = 0.9
        _FlowSpeed ("Reflection cycles per second", Range(0.005, 0.2)) = 0.065
        _VortexStrength ("Continuous red vortex", Range(0, 1)) = 0.8
        _RotationSpeed ("Vortex radians per second", Range(-1, 1)) = 0.2991993
        _FrostGrain ("Stable fine frost grain", Range(0, 0.025)) = 0.004
        _ShowcaseTime ("Deterministic time / -1 for live", Float) = -1
        _BackdropTex ("Optional compositing source plate", 2D) = "black" {}
        _BackdropStrength ("Use optional source plate", Range(0, 1)) = 0
        [Toggle] _BackdropScreenSpace ("Align source plate to camera viewport", Float) = 0
        _BlurRadius ("Optional source blur / texels", Range(0.5, 40)) = 12
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        // Premultiplied source-over produces clean black plates and RGBA edges.
        // No additive halo is emitted outside the rounded panel silhouette.
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

            float4 _FrostColor, _TintA, _TintB, _RimColor;
            float _TintStrength, _Opacity, _Aspect, _Radius;
            float _TailEnabled, _TailAnchor, _TailLength, _TailWidth, _TailLean;
            float _EdgeWidth, _BevelWidth, _RimStrength;
            float _FlowIntensity, _FlowSpeed, _VortexStrength, _RotationSpeed;
            float _FrostGrain, _ShowcaseTime, _BackdropStrength, _BackdropScreenSpace, _BlurRadius;
            sampler2D _BackdropTex;
            float4 _BackdropTex_TexelSize;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                // A cylindrical mesh supplies the spatial curvature. Keeping
                // the material in UV space avoids moving text or the mesh.
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.screenPos = ComputeScreenPos(o.vertex);
                return o;
            }

            float RoundedRectangle(float2 p, float2 extent, float radius)
            {
                float2 q = abs(p) - extent + radius;
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - radius;
            }

            float SegmentDistanceSquared(float2 p, float2 a, float2 b)
            {
                float2 edge = b - a;
                float t = saturate(dot(p - a, edge) / max(dot(edge, edge), 0.000001));
                float2 delta = p - (a + t * edge);
                return dot(delta, delta);
            }

            float Cross2D(float2 a, float2 b)
            {
                return a.x * b.y - a.y * b.x;
            }

            float TriangleDistance(float2 p, float2 a, float2 b, float2 c)
            {
                float d = sqrt(min(SegmentDistanceSquared(p, a, b),
                    min(SegmentDistanceSquared(p, b, c), SegmentDistanceSquared(p, c, a))));
                float ab = Cross2D(b - a, p - a);
                float bc = Cross2D(c - b, p - b);
                float ca = Cross2D(a - c, p - c);
                bool inside = (ab >= 0 && bc >= 0 && ca >= 0)
                    || (ab <= 0 && bc <= 0 && ca <= 0);
                return inside ? -d : d;
            }

            float SoftUnion(float a, float b, float smoothing)
            {
                float h = max(smoothing - abs(a - b), 0.0) / smoothing;
                return min(a, b) - h * h * smoothing * 0.25;
            }

            float SpeechBubbleDistance(float2 p, float2 extent, float radius, float aspect)
            {
                float rectangle = RoundedRectangle(p, extent, radius);
                UNITY_BRANCH
                if (_TailEnabled > 0.5)
                {
                    // UVs remain in the original rectangle's coordinate system.
                    // The export mesh must extend below UV.y=0 to carry this tail.
                    float halfWidth = min(max(_TailWidth, 0.04) * 0.5,
                        max(extent.x - radius - 0.01, 0.02));
                    float anchorLimit = max(extent.x - radius - halfWidth - 0.01, 0.0);
                    float anchor = clamp((saturate(_TailAnchor) - 0.5) * aspect,
                        -anchorLimit, anchorLimit);
                    float rootY = -extent.y + 0.022;
                    float2 a = float2(anchor - halfWidth, rootY);
                    float2 b = float2(anchor + halfWidth, rootY);
                    float2 tip = float2(anchor + _TailLean,
                        -extent.y - max(_TailLength, 0.02));
                    // The root overlaps the rectangle, then shares a single SDF.
                    // No separate quad, internal root line or double-alpha seam.
                    float tail = TriangleDistance(p, a, b, tip) - 0.006;
                    return SoftUnion(rectangle, tail, 0.024);
                }
                return rectangle;
            }

            float3 BlurredPlate(float2 uv)
            {
                float2 step = abs(_BackdropTex_TexelSize.xy) * _BlurRadius;
                float3 c = tex2D(_BackdropTex, saturate(uv)).rgb * 0.24;
                c += tex2D(_BackdropTex, saturate(uv + float2(step.x, 0))).rgb * 0.12;
                c += tex2D(_BackdropTex, saturate(uv - float2(step.x, 0))).rgb * 0.12;
                c += tex2D(_BackdropTex, saturate(uv + float2(0, step.y))).rgb * 0.12;
                c += tex2D(_BackdropTex, saturate(uv - float2(0, step.y))).rgb * 0.12;
                c += tex2D(_BackdropTex, saturate(uv + step)).rgb * 0.07;
                c += tex2D(_BackdropTex, saturate(uv - step)).rgb * 0.07;
                c += tex2D(_BackdropTex, saturate(uv + float2(step.x, -step.y))).rgb * 0.07;
                c += tex2D(_BackdropTex, saturate(uv + float2(-step.x, step.y))).rgb * 0.07;
                return c;
            }

            float4 frag(v2f i) : SV_Target
            {
                float time = _ShowcaseTime >= 0 ? _ShowcaseTime : _Time.y;
                float aspect = max(_Aspect, 0.1);
                float2 p = (i.uv - 0.5) * float2(aspect, 1.0);
                float2 extent = max(float2(aspect, 1.0) * 0.5 - 0.003, 0.005);
                float radius = min(_Radius, min(extent.x, extent.y) * 0.95);
                float distance = SpeechBubbleDistance(p, extent, radius, aspect);
                float aa = max(fwidth(distance) * 0.75, 0.00025);
                float mask = 1.0 - smoothstep(-aa, aa, distance);
                clip(mask - 0.0001);

                float sine, cosine;
                sincos(time * _RotationSpeed, sine, cosine);
                float2 q = float2(cosine * p.x - sine * p.y, sine * p.x + cosine * p.y);
                // Broad continuous colour bands, not quantised pixels. Two
                // offset cores keep the vortex soft and avoid a hard centre.
                float2 core = q - float2(aspect * 0.16, -0.07);
                float r = length(core);
                float angle = atan2(core.y, core.x);
                float smoothCore = smoothstep(0.025, 0.16, r);
                float wave = sin(angle * 2.0 + r * 5.2 - time * 0.22
                    + sin(q.x * 1.2 + q.y * 2.1 + time * 0.12) * 0.6);
                float band = 0.5 + 0.5 * wave * smoothCore;
                float cloud = 0.5 + 0.5 * sin(q.x * 1.3 - q.y * 2.0 + wave * 0.7 - time * 0.17);
                float silk = 0.5 + 0.5 * sin(q.x * 2.5 + q.y * 1.4 + time * 0.09);
                float3 redFilm = lerp(_TintA.rgb, _TintB.rgb, smoothstep(0.12, 0.88, band));
                float vortex = saturate(_VortexStrength);
                float3 glass = _FrostColor.rgb * (0.84 + 0.17 * cloud + 0.08 * silk);

                UNITY_BRANCH
                if (_BackdropStrength > 0.0001)
                {
                    // This is an optional supplied plate, never system MR
                    // passthrough or an invisible desktop camera dependency.
                    float2 warp = float2(sin(q.y * 2.2 + time * 0.15),
                        cos(q.x * 1.6 - time * 0.13)) * vortex * 0.002;
                    // ComputeScreenPos corrects the camera projection flip for
                    // an imported Texture2D plate. Every panel now samples the
                    // same viewport-aligned image rather than repeating it.
                    // Local UV mode remains the default for existing materials.
                    float2 viewportUV = i.screenPos.xy / max(i.screenPos.w, 0.00001);
                    float2 plateUV = lerp(i.uv, viewportUV, step(0.5, _BackdropScreenSpace));
                    float3 plate = BlurredPlate(plateUV + warp);
                    float luma = dot(plate, float3(0.2126, 0.7152, 0.0722));
                    plate = lerp(plate, luma.xxx, 0.24);
                    glass = lerp(glass, plate * 0.72 + _FrostColor.rgb * 0.28,
                        saturate(_BackdropStrength));
                }

                float filmAmount = saturate(_TintStrength) * (0.46 + 0.54 * vortex)
                    * (0.62 + 0.38 * cloud);
                glass = lerp(glass, redFilm, filmAmount);
                // A stable fine texture stays fixed in artwork UVs. Its small
                // amplitude avoids the previously rejected blocky appearance.
                float grain = frac(sin(dot(i.uv * float2(1731, 1157),
                    float2(12.9898, 78.233))) * 43758.5453) - 0.5;
                glass += grain * _FrostGrain;

                float innerDistance = max(-distance, 0.0);
                float bevel = exp2(-innerDistance / max(_BevelWidth, 0.002) * 2.4) * mask;
                float rim = 1.0 - smoothstep(_EdgeWidth, _EdgeWidth + aa, abs(distance));
                float perimeter = atan2(p.y / extent.y, p.x / extent.x) * 0.159154943 + 0.5;
                float phase = frac(perimeter - time * _FlowSpeed);
                float firstDistance = min(phase, 1.0 - phase);
                float secondDistance = abs(phase - 0.46);
                float first = exp2(-firstDistance * firstDistance * 240.0);
                float second = exp2(-secondDistance * secondDistance * 130.0) * 0.4;
                float reflection = saturate(first + second) * _FlowIntensity;
                float upperLight = smoothstep(0.24, 0.92, i.uv.y);
                glass += _RimColor.rgb * bevel * (0.018 + reflection * 0.065);
                glass += _RimColor.rgb * pow(saturate(i.uv.y), 10.0) * 0.012;

                float edgeAmount = saturate(rim * _RimStrength * (0.38 + 0.62 * reflection));
                float3 edgeColor = lerp(_TintB.rgb * 0.48, _RimColor.rgb,
                    saturate(0.48 + reflection * 0.52 + upperLight * 0.07));
                glass = lerp(glass, edgeColor, edgeAmount);

                // Uncoloured procedural meshes never require a vertex colour
                // channel, so an absent colour stream cannot hide the panel.
                float opacity = saturate(_Opacity);
                float alpha = mask * lerp(opacity, max(opacity, 0.94), edgeAmount);
                return float4(max(glass, 0.0) * alpha, alpha);
            }
            ENDCG
        }
    }
    Fallback Off
}

