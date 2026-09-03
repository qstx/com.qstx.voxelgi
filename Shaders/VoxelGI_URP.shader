Shader "Hidden/QSTX/VoxelGI"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest Always

        HLSLINCLUDE
        // 全屏 Pass 仅负责把各阶段的屏幕纹理绘制到当前 Render Graph 目标。
        #pragma enable_d3d11_debug_symbols
        #include "Includes/VoxelGIFullscreenCommon.hlsl"
        #include "Includes/VoxelGIScreenTrace.hlsl"
        #include "Includes/VoxelGITemporal.hlsl"
        #include "Includes/VoxelGIComposite.hlsl"
        #include "Includes/VoxelGIDebug.hlsl"
        ENDHLSL

        Pass
        {
            // 从深度/法线重建世界位置，执行体素 Cone Tracing。
            Name "ScreenTrace"
            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex VoxelGI_FullscreenVertex
            #pragma fragment VoxelGI_ScreenTraceFragment
            ENDHLSL
        }

        Pass
        {
            // 重投影并融合上一帧结果，降低屏幕追踪噪声。
            Name "TemporalFilter"
            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex VoxelGI_FullscreenVertex
            #pragma fragment VoxelGI_TemporalFragment
            ENDHLSL
        }

        Pass
        {
            // 将场景颜色与滤波后的间接光相加。
            Name "Composite"
            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex VoxelGI_FullscreenVertex
            #pragma fragment VoxelGI_CompositeFragment
            ENDHLSL
        }

        Pass
        {
            // 根据 DebugMode 显示体素纹理、阴影或指定中间结果。
            Name "DebugVisualization"
            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex VoxelGI_FullscreenVertex
            #pragma fragment VoxelGI_DebugFragment
            ENDHLSL
        }
    }
    Fallback Off
}
