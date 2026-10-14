Shader "Unlit/VertexOffset"
{
    Properties  {
        _ColorA ("Color A", Color) = (1,1,1,1)
        _ColorB ("Color B", Color) = (1,1,1,1)
        _ColorStart ("Color Start", Range(0,1) ) = 0
        _ColorEnd ("Color End", Range(0,1) )= 1
        _WaveAmp ("Wave Amplitude", Range(0,0.2) )= 0.1
    }
    SubShader 
    {  
        Tags { 
            "RenderType"="Opaque" //tag to inform the render pipeline of what type this is      
        }

        Pass{ 

            CGPROGRAM
            #pragma vertex vert //what function is vertex shader
            #pragma fragment frag //what function is fragment shader
            
            #include "UnityCG.cginc"
            
            #define TAU 6.28318530718

            float4 _ColorA;
            float4 _ColorB;
            float _ColorStart;
            float _ColorEnd;
            float _WaveAmp;
            //automatically filled out by unity
            struct meshData { //per-vertex mesh data
                float4 vertex : POSITION; //vertex position
                float3 normals : NORMAL; //local space normal direction
                float4 uv0 : TEXCOORD0; //uv0 diffuse/normal map textures
                
            };

            struct Interpolators //vertex to fragment shader
            {
                float4 vertex : SV_POSITION; //clip space position
                float3 normal : TEXCOORD0;
                float2 uv : TEXCOORD1;
            };
            
            float GetWave(float2 uv){
                float2 uvsCentered = uv * 2 - 1;
                float radialDistance = length(uvsCentered);
                //return float4(radialDistance.xxx, 1);

                float wave = cos((radialDistance + _Time.y * 0.1) * TAU * 5) * 0.5 + 0.5;
                wave *= 1-radialDistance;
                return wave;
            }
        
            

            Interpolators vert (meshData v) {
                Interpolators o;
                
                v.vertex.y += GetWave(v.uv0.xy) * _WaveAmp; //offset vertex y position by wave value
                o.vertex = UnityObjectToClipPos(v.vertex); //local space to clip space
                o.normal = UnityObjectToWorldNormal(v.normals);
                o.uv = v.uv0; 
                return o;
            }
            
                
            float InverseLerp(float a, float b, float v){
                return (v-a)/(b-a);
            }

             

            
            float4 frag (Interpolators i) : SV_Target {
                
                return GetWave(i.uv);
            }
            
            ENDCG
        }
    }    
}
