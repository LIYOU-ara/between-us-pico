Shader "Prototype/StereoCaption" {
 Properties { _MainTex("Font atlas",2D)="white" {} _OutlineColor("Outline",Color)=(0,0,0,0.9) _OutlinePixels("Outline pixels",Range(0,2))=0.7 }
 SubShader { Tags {"Queue"="Overlay" "RenderType"="Transparent" "IgnoreProjector"="True"} Cull Off ZWrite Off ZTest Always Blend SrcAlpha OneMinusSrcAlpha
 Pass {
 CGPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #pragma multi_compile_instancing
 #include "UnityCG.cginc"
 struct appdata {float4 vertex:POSITION;float2 uv:TEXCOORD0;fixed4 color:COLOR;UNITY_VERTEX_INPUT_INSTANCE_ID};
 struct v2f {float4 vertex:SV_POSITION;float2 uv:TEXCOORD0;fixed4 color:COLOR;UNITY_VERTEX_OUTPUT_STEREO};
 sampler2D _MainTex;float4 _MainTex_TexelSize;fixed4 _OutlineColor;float _OutlinePixels;
 v2f vert(appdata v){v2f o;UNITY_SETUP_INSTANCE_ID(v);UNITY_INITIALIZE_OUTPUT(v2f,o);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);o.vertex=UnityObjectToClipPos(v.vertex);o.uv=v.uv;o.color=v.color;return o;}
 fixed4 frag(v2f i):SV_Target {
  UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
  float a=tex2D(_MainTex,i.uv).a;float2 d=_MainTex_TexelSize.xy*_OutlinePixels;
  float edge=max(max(tex2D(_MainTex,i.uv+float2(d.x,0)).a,tex2D(_MainTex,i.uv-float2(d.x,0)).a),max(tex2D(_MainTex,i.uv+float2(0,d.y)).a,tex2D(_MainTex,i.uv-float2(0,d.y)).a));
  float alpha=max(a,edge*_OutlineColor.a);return fixed4(lerp(_OutlineColor.rgb,i.color.rgb,a/max(alpha,0.0001)),alpha*i.color.a);
 }
 ENDCG
 }
 }
}
