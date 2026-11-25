Shader "Between Us/Frosted Conversation Panel"
{
    Properties
    {
        _MainTex ("Original Figma shape and detail", 2D) = "white" {}
        _GlassStrength ("Glass finish (0 = original PNG)", Range(0,1)) = 1
        _Opacity ("Fallback glass opacity", Range(0,1)) = .5
        _TintTop ("Upper glass tint", Color) = (.96,.98,1,1)
        _TintBottom ("Lower glass tint", Color) = (.88,.92,.96,1)
        _BlurTex ("Local blurred camera image", 2D) = "white" {}
        _BlurTint ("Frost tint over camera", Range(0,1)) = .04
        _CameraGlassReady ("Fresh calibrated camera frame", Range(0,1)) = 0
        _BorderColor ("Neutral glass rim", Color) = (.90,.96,1,1)
        _BorderStrength ("Rim definition", Range(0,1)) = .9
        _FlowColor ("Reflection highlight", Color) = (1,.99,.97,1)
        _FlowSpeed ("Reflection cycles per second", Range(.01,.15)) = .085
        _FlowIntensity ("Moving reflection strength", Range(0,1)) = 1
        _FlowTime ("Preview time (-1 = live)", Float) = -1
        _EmotionColor ("Tentative interpretation tint", Color) = (1,1,1,1)
        _EmotionStrength ("Interpretation tint strength", Range(0,1)) = 0
        _VortexStrength ("Full-surface soft glass flow strength", Range(0,1)) = 0
        _VortexTime ("Soft flow preview time (-1 = live)", Float) = -1
        _IdleIridescence ("Standby rotating pastel glass", Range(0,1)) = 0
        _GrainStrength ("Fine frost grain", Range(0,.03)) = .006
        _SheenStrength ("Soft upper highlight", Range(0,.15)) = .018
    }
    SubShader
    {
        Tags { "Queue"="Overlay-1" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off ZWrite Off ZTest Always Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; float3 world : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            sampler2D _MainTex, _BlurTex;
            float4 _MainTex_TexelSize;
            half _GlassStrength, _Opacity, _BorderStrength, _GrainStrength, _SheenStrength;
            half4 _TintTop, _TintBottom, _BorderColor;
            half4 _FlowColor;
            float _FlowSpeed, _FlowIntensity, _FlowTime;
            half4 _EmotionColor;
            half _EmotionStrength, _VortexStrength, _IdleIridescence;
            float _VortexTime;
            float4x4 _GlassWorldToCamera;
            float4 _GlassIntrinsics;
            half _GlassFlipRows, _CameraGlassReady, _BlurTint;

            half ShapeMask(half artworkAlpha)
            {
                // Lift the translucent interior without filling the PNG's empty corners.
                // The speaker's asymmetric tail remains defined by the supplied artwork.
                return smoothstep(.035h, .11h, artworkAlpha);
            }
            v2f vert(appdata v)
            {
                v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_OUTPUT(v2f,o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex); o.uv = v.uv;
                o.world = mul(unity_ObjectToWorld, v.vertex).xyz; return o;
            }
            half4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half4 source = tex2D(_MainTex, i.uv);
                half mask = ShapeMask(source.a);
                half redChroma = source.r - max(source.g, source.b);
                // Use the original ink only as a contour mask; none of its
                // coral pigment contributes to the enhanced glass material.
                half artworkRim = smoothstep(.06h, .40h, redChroma);
                half outline = artworkRim * _BorderStrength;
                // A soft inner bevel gives the moving reflection enough width
                // to survive headset resolution. It stays inside the PNG mask.
                float2 bevelStep = _MainTex_TexelSize.xy * 6;
                half neighbourMask = min(
                    min(ShapeMask(tex2D(_MainTex, i.uv + float2(bevelStep.x, 0)).a),
                        ShapeMask(tex2D(_MainTex, i.uv - float2(bevelStep.x, 0)).a)),
                    min(ShapeMask(tex2D(_MainTex, i.uv + float2(0, bevelStep.y)).a),
                        ShapeMask(tex2D(_MainTex, i.uv - float2(0, bevelStep.y)).a)));
                half innerBevel = saturate(mask - neighbourMask);
                half reflectionMask = max(outline, innerBevel * .65h);

                float2 vortexShift = float2(0, 0);
                half vortexAmount = 0;
                half3 vortexColor = half3(0, 0, 0);
                if (_VortexStrength > .0001h)
                {
                    // Flow spans the complete frosted surface. Captions render
                    // separately; only transparent PNG corners skip this work.
                    UNITY_BRANCH
                    if (mask > .001h)
                    {
                        // Continuous artwork-space flow. The frosted reading
                        // surface has no quantised grid or block-shaped colour steps.
                        float panelAspect = _MainTex_TexelSize.z / max(_MainTex_TexelSize.w, 1);
                        float2 p = (i.uv - float2(.5, .52)) * float2(panelAspect, 1);
                        p -= float2(panelAspect * .20, -.06);
                        float radius = length(p);
                        float angle = atan2(p.y, p.x);
                        float clock = _VortexTime >= 0 ? _VortexTime : _Time.y;
                        // Broad continuous bands change over a few seconds. The
                        // earlier low-contrast 45-second phase was measurable in
                        // desktop captures but effectively invisible in a headset.
                        float spiral = sin(angle * 2 + radius * 5.5 - clock * .46
                            + sin(p.x * .8 + p.y * 1.7 - clock * .19) * .65);
                        // Soften the angular core as well as the colour bands.
                        // Angular frequency stays integral across the atan2 seam.
                        half core = smoothstep(.025, .16, radius);
                        half band = .5h + .5h * spiral * core;
                        half cloud = .5h + .5h * sin(p.x * .9 + p.y * 2.3 + spiral * .85 * core - clock * .26);
                        half effect = saturate(_VortexStrength) * mask;
                        // A restrained blue/champagne contrast makes neutral flow
                        // legible; most of the live frosted camera sample remains.
                        vortexColor = lerp(half3(.39h, .61h, .74h), half3(.88h, .66h, .46h), band);
                        vortexColor = lerp(vortexColor, _EmotionColor.rgb, saturate(_EmotionStrength) * .80h);
                        vortexAmount = effect * (.16h + .12h * cloud);
                        // Bounded to < .005 UV in magnitude even at full strength.
                        // This samples the existing blurred RGB image, never the
                        // artwork silhouette, tail, caption or moving rim texture.
                        vortexShift = float2(sin(angle + radius * 1.7 - clock * .29),
                            cos(angle * 2 - radius * 1.2 + clock * .23)) * (.0035 * effect * core);
                    }
                }
                half3 glass = lerp(_TintBottom.rgb, _TintTop.rgb, smoothstep(0, 1, i.uv.y));
                // The runtime owns the camera frame and acquisition-pose transform.
                // This is a planar approximation using the RGB camera, not a grab
                // of PICO's depth-reprojected system passthrough layer.
                float3 optical = mul(_GlassWorldToCamera, float4(i.world, 1)).xyz;
                float2 cameraUv = _GlassIntrinsics.zw +
                    float2(optical.x, -optical.y) * _GlassIntrinsics.xy / max(optical.z, .001);
                // Intrinsics use a top-left pixel origin; Unity textures use
                // bottom-left UV. The existing SDK upload is visually upright.
                cameraUv.y = 1 - cameraUv.y;
                cameraUv.y = lerp(cameraUv.y, 1 - cameraUv.y, _GlassFlipRows);
                float2 validEdge = smoothstep(.01, .05, cameraUv) * (1 - smoothstep(.95, .99, cameraUv));
                half cameraGlass = _CameraGlassReady * step(.05, optical.z) * validEdge.x * validEdge.y;
                half3 blurred = tex2D(_BlurTex, saturate(cameraUv + vortexShift * cameraGlass)).rgb;
                half luminance = dot(blurred, half3(.2126h, .7152h, .0722h));
                blurred = lerp(blurred, luminance.xxx, .16h);
                // Retain environmental colours while narrowing the luminance
                // range for dark caption contrast. Avoid a flat white backplate.
                blurred = saturate(blurred + (.24h + .65h * luminance - luminance));
                glass = lerp(glass, lerp(blurred, glass, _BlurTint), cameraGlass);
                half sourceLuma = dot(source.rgb, half3(.2126h, .7152h, .0722h));
                sourceLuma = lerp(sourceLuma, .94h, artworkRim);
                glass += (sourceLuma - .94h) * .055h;
                // Tint remains translucent; it is a possible AI reading,
                // not a categorical emotion label or a change to the real world.
                if (_EmotionStrength > .0001h)
                    glass = lerp(glass, _EmotionColor.rgb, saturate(_EmotionStrength) * .22h);
                if (_VortexStrength > .0001h)
                    glass = lerp(glass, vortexColor, vortexAmount);

                // Standby alone adds an iridescent silver film. Rotate only the
                // continuous colour field: camera sampling, artwork, text and
                // alpha keep their accepted projection and geometry.
                if (_IdleIridescence > .0001h && mask > .001h)
                {
                    float idleClock = _VortexTime >= 0 ? _VortexTime : _Time.y;
                    float idleAspect = _MainTex_TexelSize.z / max(_MainTex_TexelSize.w, 1);
                    float2 centred = (i.uv - .5) * float2(idleAspect, 1);
                    // One unhurried turn in 21 seconds. There is no quantised
                    // grid, angular seam, random frame noise or rotating panel.
                    float rotation = idleClock * .2991993;
                    float sine, cosine;
                    sincos(rotation, sine, cosine);
                    float2 rotated = float2(cosine * centred.x - sine * centred.y,
                        sine * centred.x + cosine * centred.y);
                    float spectrumPhase = rotated.x * 4 + rotated.y * 1.8
                        + .55 * sin(rotated.y * 2.1 + idleClock * .16);
                    half3 pastel = .76h + .18h * cos(spectrumPhase + float3(0, 2.0943951, 4.1887902));
                    half silver = .5h + .5h * sin(rotated.x * 1.7 - rotated.y * 2.5 + idleClock * .12);
                    half3 idleColor = lerp(pastel, half3(.93h, .95h, .98h), .18h + .24h * silver);
                    half idleAmount = saturate(_IdleIridescence) * (.26h + .08h * silver);
                    glass = lerp(glass, idleColor, idleAmount);
                }

                // Stable, very fine grain: no flashing or scrolling behind the words.
                float2 pixel = floor(i.uv * _MainTex_TexelSize.zw);
                half grain = frac(sin(dot(pixel, float2(12.9898,78.233))) * 43758.5453) - .5h;
                glass += grain * _GrainStrength;

                half above = ShapeMask(tex2D(_MainTex, i.uv + float2(0, _MainTex_TexelSize.y * 2)).a);
                half below = ShapeMask(tex2D(_MainTex, i.uv - float2(0, _MainTex_TexelSize.y * 2)).a);
                half topBevel = saturate(mask - above);
                half bottomBevel = saturate(mask - below);
                glass += topBevel * .12h - bottomBevel * .025h;
                glass += pow(saturate(i.uv.y), 8) * _SheenStrength;
                // Two broad reflections travel slowly around the PNG contour,
                // including its asymmetric speech tail. This reflection remains
                // on the rim; the separate caption geometry stays stationary.
                if (reflectionMask > .001h)
                {
                float aspect = _MainTex_TexelSize.z / max(_MainTex_TexelSize.w, 1);
                float2 radial = (i.uv - .5) * float2(aspect, 1);
                float perimeter = atan2(radial.y, radial.x) * .159154943 + .5;
                float clock = _FlowTime >= 0 ? _FlowTime : _Time.y;
                float phase = frac(perimeter - clock * _FlowSpeed);
                float firstDistance = min(phase, 1 - phase);
                float secondDistance = abs(phase - .47);
                half first = exp2(-firstDistance * firstDistance * 260);
                half second = exp2(-secondDistance * secondDistance * 120) * .55h;
                half reflection = saturate(first + second) * _FlowIntensity;
                half3 rimColor = lerp(_BorderColor.rgb, _FlowColor.rgb, first);
                if (_EmotionStrength > .0001h)
                    rimColor = lerp(rimColor, _EmotionColor.rgb, saturate(_EmotionStrength) * .07h);
                half rimLight = saturate(.10h + .10h * i.uv.y + reflection * 1.2h);
                glass = lerp(saturate(glass), rimColor, reflectionMask * rimLight);
                }

                half alpha = mask * lerp(lerp(_Opacity, .96h, cameraGlass), .97h, outline);
                // Fresh camera blur covers sharp VST detail only inside the PNG;
                // stale or out-of-view samples fall back to a translucent tint.
                // RGB is premultiplied by the blend operation into the transparent eye buffer.
                return lerp(source, half4(glass, alpha), _GlassStrength);
            }
            ENDCG
        }
    }
}
