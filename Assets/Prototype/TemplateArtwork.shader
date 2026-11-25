Shader "Between Us/Template Artwork"
{
 Properties {
  _MainTex("Original PNG",2D)="white" {}
  _Color("Tint",Color)=(1,1,1,1)
  _ClipRect("UV clip rectangle",Vector)=(0,0,1,1)
  _Roundness("Photo corner radius",Range(0,.49))=0
  _Aspect("Quad width / height",Float)=1
  _OutlinePixels("Subtle contrast outline",Range(0,2))=.5
 }
 SubShader {
  Tags {"Queue"="Overlay+2" "RenderType"="Transparent" "IgnoreProjector"="True"}
  Cull Off ZWrite Off ZTest Always Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
  Pass {
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile_instancing
   #include "UnityCG.cginc"
   struct appdata {float4 vertex:POSITION;float2 uv:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID};
   struct v2f {float4 vertex:SV_POSITION;float2 uv:TEXCOORD0;float2 raw:TEXCOORD1;UNITY_VERTEX_OUTPUT_STEREO};
   sampler2D _MainTex;float4 _MainTex_ST,_MainTex_TexelSize,_ClipRect;
   fixed4 _Color;float _Roundness,_Aspect,_OutlinePixels;
   v2f vert(appdata v){v2f o;UNITY_SETUP_INSTANCE_ID(v);UNITY_INITIALIZE_OUTPUT(v2f,o);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);o.vertex=UnityObjectToClipPos(v.vertex);o.raw=v.uv;o.uv=TRANSFORM_TEX(v.uv,_MainTex);return o;}
   fixed4 frag(v2f i):SV_Target {
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
    half4 source=tex2D(_MainTex,i.uv);float2 d=_MainTex_TexelSize.xy*_OutlinePixels;
    half edge=max(max(tex2D(_MainTex,i.uv+float2(d.x,0)).a,tex2D(_MainTex,i.uv-float2(d.x,0)).a),max(tex2D(_MainTex,i.uv+float2(0,d.y)).a,tex2D(_MainTex,i.uv-float2(0,d.y)).a));
    half alpha=max(source.a,edge*.38h);half3 rgb=lerp(half3(.04h,.05h,.065h),source.rgb,source.a/max(alpha,.0001h));
    float visible=step(_ClipRect.x,i.raw.x)*step(_ClipRect.y,i.raw.y)*step(i.raw.x,_ClipRect.z)*step(i.raw.y,_ClipRect.w);
    if(_Roundness>.0001){float2 q=(abs(i.raw-.5)-.5)*float2(max(_Aspect,.001),1)+_Roundness;float dist=length(max(q,0))+min(max(q.x,q.y),0)-_Roundness;visible*=1-smoothstep(-.003,.003,dist);}
    return half4(rgb*_Color.rgb,alpha*_Color.a*visible);
   }
   ENDCG
  }
 }
}

