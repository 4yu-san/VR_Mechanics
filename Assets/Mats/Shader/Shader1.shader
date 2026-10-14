Shader "Unlit/Shader1"
{
    Properties  {
        _ColorA ("Color A", Color) = (1,1,1,1)
        _ColorB ("Color B", Color) = (1,1,1,1)
        _ColorStart ("Color Start", Range(0,1) ) = 0
        _ColorEnd ("Color End", Range(0,1) )= 1
    }
    SubShader 
    {  
        Tags { "RenderType"="Transparent" //tag to inform the render pipeline of what type this is
                "Queue" = "Transparent" //changes the render order      
        }

        Pass{ 

            Cull Back
            ZWrite Off
            ZTest LEqual
            Blend One One //additive
            

            //Blend Dstcolor Zero //multiply
            CGPROGRAM
            #pragma vertex vert //what function is vertex shader
            #pragma fragment frag //what function is fragment shader
            
            #include "UnityCG.cginc"
            #define TAU 6.28318530718

            float4 _ColorA;
            float4 _ColorB;
            float _ColorStart;
            float _ColorEnd;
            //automatically filled out by unity
            struct meshData { //per-vertex mesh data
                float4 vertex : POSITION; //vertex position
                float3 normals : NORMAL; //local space normal direction
                //float4 color: COLOR;
                //float4 tangent : TANGENT;
                float4 uv0 : TEXCOORD0; //uv0 diffuse/normal map textures
                float4 uv1 : TEXCOORD1; //uv1 coordinates lightmap coordinates
            };

            struct Interpolators //vertex to fragment shader
            {
                float4 vertex : SV_POSITION; //clip space position
                float3 normal : TEXCOORD0;
                float2 uv : TEXCOORD1;
               // float2 uv : TEXCOORD0; //
            };
        
            

            Interpolators vert (meshData v) {
                Interpolators o;
                o.vertex = UnityObjectToClipPos(v.vertex); //local space to clip space
                o.normal = UnityObjectToWorldNormal(v.normals); //just pass thru, to not affect normal to rotation
                o.uv = v.uv0; //(v.uv0 + _Offset) * _Scale ; //pass through
                return o;
            }
                //float (32 bit float)
                // half (16 bit float)
                
            float InverseLerp(float a, float b, float v){
                return (v-a)/(b-a);
            }
            
            float4 frag (Interpolators i) : SV_Target {
                //blend between two colors based on the X UV coordinate
                //float t = saturate(InverseLerp(_ColorStart, _ColorEnd, i.uv.x));
                //t = frac(t);
                //return float4(i.normal, 0);
                //_Time.y // y is sec w is sec/20

                float xOffset = cos( i.uv.x * TAU * 8) * 0.01;
                float t = cos((i.uv.y + xOffset + _Time.y * 0.25)* TAU * 5) * 0.5 + 0.5;
                t *= 1 - i.uv.y;
                
                float topBottomRemover = (abs(i.normal.y) < 0.999);
                float waves = t * topBottomRemover;
                //float4 outColor = lerp(_ColorA, _ColorB, t);
                //return outColor; 
                float4 gradient = lerp (_ColorA, _ColorB, t) *waves;
                return gradient;
            }
            
            ENDCG
        }
    }    
}
    
