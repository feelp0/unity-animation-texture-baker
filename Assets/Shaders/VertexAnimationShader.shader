Shader "Loopcifer/Tests/VertexAnimationShader"
{
    Properties
    {
        _MainTex("Texture", 2D) = "white" {}
        _AnimationTexture("Animation Texture", 2D) = "black" {}
        _AnimationsCount("Animation Count", Float) = 1
        _MainColor("Main Color", COLOR) = (1,1,1,1)
    }

    HLSLINCLUDE
    
        //Add library here
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl" //utils
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl" //for unity lights

        //add non static properties in the command buffer to make the shader SRP Batchable
        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            float4 _AnimationTexture_TexelSize;
            float _AnimationsCount;
            half4 _MainColor;
        CBUFFER_END

    ENDHLSL

    SubShader
    {
        // Tags { "RenderPipeline" = "LoopPipeline" }
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "GBColor"
            // Tags { "Lightmode" = "LoopciferDeferred" }
            Tags { "Lightmode" = "UniversalForward" }
            Cull Back

            HLSLPROGRAM

                #pragma vertex vertSurface
                #pragma fragment fragSurface

                //Include some lights calculations
                #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
                #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
                #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
                #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
                #pragma multi_compile _ _SHADOWS_SOFT

                sampler2D _MainTex;
                sampler2D _AnimationTexture;

                struct Attributes //vertexInput
                {
                    float4 posOS : POSITION;
                    float2 uv : TEXCOORD0;
                    uint id : SV_VertexID;
                    float3 normalOS : NORMAL;
                };

                struct Varyings //vertexOutput
                {
                    float2 uv : TEXCOORD0;
                    float4 posCS : SV_POSITION;
                    float3 normalWS : NORMAL;
                };

                Varyings vertSurface(Attributes IN)
                {
                    Varyings OUT = (Varyings)0;

                    float frame = (IN.id + 0.5) / _AnimationTexture_TexelSize.z;
                    float time = _Time.y / _AnimationsCount;
					float3 animTex = tex2Dlod(_AnimationTexture, float4(frame, time, 0, 0)).xyz;
                    //IN.posOS.xyz = (animTex.xyz * 2 - 1) * 8;
                    //Same here as compute shader comment. This solution seems to be the best but it's not working right now
                    IN.posOS.xyz -= animTex.xyz ; //????????????????
                    //input.posOS.xyz = lerp(input.posOS.xyz, animTex, _Debug);

                    VertexPositionInputs vertexInput = GetVertexPositionInputs(IN.posOS.xyz);
					
                    VertexNormalInputs vertexNormalInput = GetVertexNormalInputs(IN.normalOS);
					
                    OUT.posCS = vertexInput.positionCS;

                    OUT.normalWS = vertexNormalInput.normalWS;

                    OUT.uv = IN.uv;

                    return OUT;
                }

                half4 fragSurface(Varyings IN) : SV_Target
                {
                    half4 col = tex2D(_MainTex, IN.uv) * _MainColor;

                    Light mainLight = GetMainLight();
                    float3 L = normalize(mainLight.direction);
                    float3 N = normalize(IN.normalWS);
                    float nol = saturate(dot(N, L));

                    col.rgb *= mainLight.color + _GlossyEnvironmentColor.rgb * nol;
                    
                    return col;
                }

            ENDHLSL
        }
    }
}
