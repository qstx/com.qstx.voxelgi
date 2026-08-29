Shader "QSTX/VoxelGI/Blocker"
{
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "VoxelGIShadow"
            Tags { "LightMode" = "VoxelGIShadow" }
            Cull Back
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex VoxelGIShadowVertex
            #pragma fragment VoxelGIShadowFragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings VoxelGIShadowVertex(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                return output;
            }

            half4 VoxelGIShadowFragment() : SV_Target
            {
                return 0.0;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
