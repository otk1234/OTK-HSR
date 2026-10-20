Shader "NarrativeGraybox/InspectionStroke"
{
 Properties { _Color ("Stroke", Color) = (1,0.85,0.25,1) }
 SubShader
 {
  Tags { "Queue"="Geometry+1" "RenderType"="Opaque" }
  Pass
  {
   Cull Front ZWrite Off ZTest LEqual
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "UnityCG.cginc"
   fixed4 _Color;
   float4 vert(float4 vertex:POSITION):SV_POSITION { return UnityObjectToClipPos(vertex); }
   fixed4 frag():SV_Target {return _Color;}
   ENDCG
  }
 }
}
