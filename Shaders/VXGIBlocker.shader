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
            // Blocker 不贡献材质颜色，只通过 VoxelGIShadow Pass 写入方向光深度，
            // 并由体素化阶段写入不透明度以阻挡 Emissive 的 Cone Tracing。
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
                // 使用当前 Pass 注入的光源 View/Projection 将阻挡几何体变换到 ShadowMap。
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                return output;
            }

            half4 VoxelGIShadowFragment() : SV_Target
            {
                // ColorMask 0 已关闭颜色写入，这里只保留深度副作用。
                return 0.0;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
