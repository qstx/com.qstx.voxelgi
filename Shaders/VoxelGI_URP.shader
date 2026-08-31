Shader "Hidden/QSTX/VoxelGI"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest Always

        HLSLINCLUDE
        #pragma enable_d3d11_debug_symbols
        #include "Includes/VoxelGIFullscreenCommon.hlsl"
        #include "Includes/VoxelGIScreenTrace.hlsl"
        #include "Includes/VoxelGITemporal.hlsl"
        #include "Includes/VoxelGIComposite.hlsl"
        #include "Includes/VoxelGIDebug.hlsl"
        ENDHLSL

        Pass
        {
            Name "ScreenTrace"
            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex VoxelGI_FullscreenVertex
            #pragma fragment VoxelGI_ScreenTraceFragment
            ENDHLSL
        }

        Pass
        {
            Name "TemporalFilter"
            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex VoxelGI_FullscreenVertex
            #pragma fragment VoxelGI_TemporalFragment
            ENDHLSL
        }

        Pass
        {
            Name "Composite"
            HLSLPROGRAM
            #pragma target 5.0
            #pragma vertex VoxelGI_FullscreenVertex
            #pragma fragment VoxelGI_CompositeFragment
            ENDHLSL
        }

        Pass
        {
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
