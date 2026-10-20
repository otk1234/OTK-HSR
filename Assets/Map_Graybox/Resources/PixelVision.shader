Shader "Hidden/NarrativeGraybox/PixelVision"
{
    Properties { _MainTex ("Scene", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _Center, _Resolution;
            float _Radius, _Inspection, _PixelSize, _ColorSteps;
            fixed4 frag(v2f_img i) : SV_Target
            {
                float2 cell=floor(i.uv*_Resolution.xy/_PixelSize);
                float2 uv=(cell+.5)*_PixelSize/_Resolution.xy;
                float2 delta=(uv-_Center.xy)*float2(_Resolution.x/_Resolution.y,1);
                float fade=smoothstep(_Radius*.48,_Radius,length(delta));
                // 4x4 ordered dither: square dots between four brightness bands.
                int x=(int)cell.x%4, y=(int)cell.y%4;
                float bayer[16]={0,8,2,10,12,4,14,6,3,11,1,9,15,7,13,5};
                float threshold=(bayer[y*4+x]+.5)/16;
                float level=floor(fade*4 + threshold)/4;
                fixed4 color=tex2D(_MainTex,uv);
                float luminance=dot(color.rgb,float3(.299,.587,.114));
                color.rgb=lerp(luminance.xxx,color.rgb,.68);
                // Fixed screen-space grain avoids distracting animated noise.
                float grain=frac(sin(dot(cell,float2(12.9898,78.233)))*43758.5453)-.5;
                float2 texel=_PixelSize/_Resolution.xy;
                float nextLuma=dot(tex2D(_MainTex,uv+float2(texel.x,0)).rgb,float3(.299,.587,.114));
                float edge=saturate(abs(luminance-nextLuma)*2);
                color.rgb*=1-edge*.22;
                // Inspection retains the night palette, with only a small exposure lift.
                color.rgb*=lerp(.78,.88,_Inspection)*(1-saturate(level)*lerp(.94,.58,_Inspection));
                color.rgb=saturate(color.rgb+grain*.025);
                color.rgb=floor(color.rgb*_ColorSteps+threshold)/_ColorSteps;
                return color;
            }
            ENDCG
        }
    }
}
