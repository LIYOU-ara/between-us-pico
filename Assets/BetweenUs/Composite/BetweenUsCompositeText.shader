Shader "Between Us/Composite/Curved Text"
{
    Properties
    {
        [PerRendererData] _MainTex ("Font atlas", 2D) = "white" {}
        _TextColor ("Text tint", Color) = (1, 1, 1, 1)
        _Opacity ("Opacity", Range(0, 1)) = 1
        _CurveRadius ("Panel curve radius", Float) = 0
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("Depth test", Float) = 4
    }
    SubShader
    {
        Tags { "Queue"="Transparent+20" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend One OneMinusSrcAlpha
        Cull Off
        ZWrite Off
        ZTest [_ZTest]
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _TextColor;
            float _Opacity, _CurveRadius;
            float4x4 _PanelWorldToLocal, _PanelLocalToWorld;
            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };
            struct v2f
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };
            v2f vert(appdata v)
            {
                v2f o;
                float4 world = mul(unity_ObjectToWorld, v.vertex);
                if (_CurveRadius > 0.0001)
                {
                    float3 flat = mul(_PanelWorldToLocal, world).xyz;
                    float angle = flat.x / _CurveRadius;
                    float3 curved = float3(_CurveRadius * sin(angle), flat.y,
                        -_CurveRadius * (1.0 - cos(angle)) + flat.z);
                    world = mul(_PanelLocalToWorld, float4(curved, 1.0));
                }
                o.position = mul(UNITY_MATRIX_VP, world);
                o.uv = v.uv;
                o.color = v.color * _TextColor;
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                float coverage = tex2D(_MainTex, i.uv).a;
                float alpha = saturate(coverage * i.color.a * _Opacity);
                return float4(i.color.rgb * alpha, alpha);
            }
            ENDCG
        }
    }
    Fallback Off
}

