using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace QSTX.VoxelGI
{
    internal readonly struct VoxelGIAccumulationBuffers
    {
        public readonly GraphicsBuffer Albedo;
        public readonly GraphicsBuffer Normal;
        public readonly GraphicsBuffer Emissive;
        public readonly GraphicsBuffer Opacity;

        public VoxelGIAccumulationBuffers(GraphicsBuffer albedo, GraphicsBuffer normal,
            GraphicsBuffer emissive, GraphicsBuffer opacity)
        {
            Albedo = albedo;
            Normal = normal;
            Emissive = emissive;
            Opacity = opacity;
        }
    }

    internal static class ComputeVoxelizer
    {
        static class IDs
        {
            public static readonly int PositionBuffer = Shader.PropertyToID("_VoxelGIPositionBuffer");
            public static readonly int NormalBuffer = Shader.PropertyToID("_VoxelGINormalBuffer");
            public static readonly int UVBuffer = Shader.PropertyToID("_VoxelGIUVBuffer");
            public static readonly int IndexBuffer = Shader.PropertyToID("_VoxelGIIndexBuffer");
            public static readonly int AlbedoAccumulation = Shader.PropertyToID("_VoxelGIAlbedoAccumulation");
            public static readonly int NormalAccumulation = Shader.PropertyToID("_VoxelGINormalAccumulation");
            public static readonly int EmissiveAccumulation = Shader.PropertyToID("_VoxelGIEmissiveAccumulation");
            public static readonly int OpacityAccumulation = Shader.PropertyToID("_VoxelGIOpacityAccumulation");
            public static readonly int AlbedoOutput = Shader.PropertyToID("_VoxelGIAlbedoOutput");
            public static readonly int NormalOutput = Shader.PropertyToID("_VoxelGINormalOutput");
            public static readonly int EmissiveOutput = Shader.PropertyToID("_VoxelGIEmissiveOutput");
            public static readonly int BaseMap = Shader.PropertyToID("_VoxelGIBaseMap");
            public static readonly int EmissionMap = Shader.PropertyToID("_VoxelGIEmissionMap");
        }

        static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionMap = Shader.PropertyToID("_EmissionMap");
        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        static readonly int AlphaClip = Shader.PropertyToID("_AlphaClip");
        static readonly int Cutoff = Shader.PropertyToID("_Cutoff");
        static readonly HashSet<Mesh> WarnedMeshes = new HashSet<Mesh>();

        public static void Dispatch(CommandBuffer cmd, VoxelGIRuntimeResources resources,
            VoxelGICameraContext context, VoxelGIAccumulationBuffers accumulation,
            VoxelGISettingsSnapshot settings, Matrix4x4 worldToVoxel, Bounds voxelBounds,
            IReadOnlyList<VoxelGIRendererEntry> entries)
        {
            ComputeShader compute = resources.ComputeShader;
            int resolution = settings.Voxelization.Resolution;
            int voxelCount = checked(resolution * resolution * resolution);

            BindAccumulationBuffers(cmd, compute, resources.Kernels.Clear.Index, accumulation);
            cmd.SetComputeIntParam(compute, "_VoxelGIElementCount", voxelCount);
            cmd.DispatchCompute(compute, resources.Kernels.Clear.Index, resources.Kernels.Clear.GroupsX(voxelCount), 1, 1);

            cmd.SetComputeMatrixParam(compute, VoxelGIShaderIDs.WorldToVoxel, worldToVoxel);
            cmd.SetComputeIntParam(compute, VoxelGIShaderIDs.Resolution, resolution);
            cmd.SetComputeIntParam(compute, "_VoxelGIConservativeRasterization",
                settings.Voxelization.ConservativeRasterization ? 1 : 0);
            cmd.SetComputeFloatParam(compute, "_VoxelGIConservativeScale", settings.Voxelization.ConservativeScale);

            for (int i = 0; i < entries.Count; i++)
            {
                VoxelGIRendererEntry entry = entries[i];
                Renderer renderer = entry.Renderer;
                if ((!entry.ContributeSurface && !entry.OccludeRadiance) ||
                    !ShouldVoxelize(renderer, settings.Voxelization.LayerMask, voxelBounds))
                    continue;
                bool opacityOnly = !entry.ContributeSurface && entry.OccludeRadiance;

                if (renderer is MeshRenderer meshRenderer)
                {
                    MeshFilter filter = meshRenderer.GetComponent<MeshFilter>();
                    if (filter != null && filter.sharedMesh != null)
                        DispatchRenderer(cmd, resources, accumulation, renderer, filter.sharedMesh, null,
                            opacityOnly);
                }
                else if (renderer is SkinnedMeshRenderer skinnedRenderer && skinnedRenderer.sharedMesh != null)
                {
                    DispatchRenderer(cmd, resources, accumulation, renderer, skinnedRenderer.sharedMesh,
                        skinnedRenderer, opacityOnly);
                }
            }

            int resolve = resources.Kernels.Resolve.Index;
            BindAccumulationBuffers(cmd, compute, resolve, accumulation);
            cmd.SetComputeTextureParam(compute, resolve, IDs.AlbedoOutput, context.AlbedoOpacity.rt);
            cmd.SetComputeTextureParam(compute, resolve, IDs.NormalOutput, context.Normal.rt);
            cmd.SetComputeTextureParam(compute, resolve, IDs.EmissiveOutput, context.Emissive.rt);
            cmd.DispatchCompute(compute, resolve,
                resources.Kernels.Resolve.GroupsX(resolution),
                resources.Kernels.Resolve.GroupsY(resolution),
                resources.Kernels.Resolve.GroupsZ(resolution));
        }

        static bool ShouldVoxelize(Renderer renderer, LayerMask layerMask, Bounds voxelBounds)
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer.forceRenderingOff)
                return false;
            if ((layerMask.value & (1 << renderer.gameObject.layer)) == 0)
                return false;
            return renderer.bounds.Intersects(voxelBounds);
        }

        static void DispatchRenderer(CommandBuffer cmd, VoxelGIRuntimeResources resources,
            VoxelGIAccumulationBuffers accumulation, Renderer renderer, Mesh mesh, SkinnedMeshRenderer skinnedRenderer,
            bool opacityOnly)
        {
            if (mesh.subMeshCount == 0 || !mesh.HasVertexAttribute(VertexAttribute.Position))
                return;

            try
            {
                mesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
                mesh.indexBufferTarget |= GraphicsBuffer.Target.Raw;
                if (skinnedRenderer != null)
                    skinnedRenderer.vertexBufferTarget |= GraphicsBuffer.Target.Raw;

                Material[] materials = renderer.sharedMaterials;
                for (int subMeshIndex = 0; subMeshIndex < mesh.subMeshCount; subMeshIndex++)
                {
                    SubMeshDescriptor subMesh = mesh.GetSubMesh(subMeshIndex);
                    if (subMesh.topology != MeshTopology.Triangles || subMesh.indexCount < 3)
                        continue;
                    Material material = subMeshIndex < materials.Length ? materials[subMeshIndex] : null;
                    if (material == null || material.renderQueue > (int)RenderQueue.GeometryLast)
                        continue;
                    DispatchSubMesh(cmd, resources, accumulation, renderer, mesh, skinnedRenderer, material,
                        subMesh, opacityOnly);
                }
            }
            catch (Exception exception)
            {
                if (WarnedMeshes.Add(mesh))
                    Debug.LogWarning($"VoxelGI skipped mesh '{mesh.name}': {exception.Message}", mesh);
            }
        }

        static void DispatchSubMesh(CommandBuffer cmd, VoxelGIRuntimeResources resources,
            VoxelGIAccumulationBuffers accumulation, Renderer renderer, Mesh mesh,
            SkinnedMeshRenderer skinnedRenderer, Material material, SubMeshDescriptor subMesh, bool opacityOnly)
        {
            ComputeShader compute = resources.ComputeShader;
            int kernel = resources.Kernels.Voxelize.Index;
            int positionStream = mesh.GetVertexAttributeStream(VertexAttribute.Position);
            bool hasNormals = mesh.HasVertexAttribute(VertexAttribute.Normal);
            bool hasUV = mesh.HasVertexAttribute(VertexAttribute.TexCoord0);
            int normalStream = hasNormals ? mesh.GetVertexAttributeStream(VertexAttribute.Normal) : positionStream;
            int uvStream = hasUV ? mesh.GetVertexAttributeStream(VertexAttribute.TexCoord0) : positionStream;

            GraphicsBuffer positionBuffer = null;
            GraphicsBuffer normalBuffer = null;
            GraphicsBuffer uvBuffer = null;
            GraphicsBuffer indexBuffer = null;
            try
            {
                positionBuffer = skinnedRenderer != null && positionStream == 0
                    ? skinnedRenderer.GetVertexBuffer()
                    : mesh.GetVertexBuffer(positionStream);
                normalBuffer = skinnedRenderer != null && normalStream == 0
                    ? skinnedRenderer.GetVertexBuffer()
                    : mesh.GetVertexBuffer(normalStream);
                uvBuffer = skinnedRenderer != null && uvStream == 0
                    ? skinnedRenderer.GetVertexBuffer()
                    : mesh.GetVertexBuffer(uvStream);
                indexBuffer = mesh.GetIndexBuffer();

                cmd.SetComputeBufferParam(compute, kernel, IDs.PositionBuffer, positionBuffer);
                cmd.SetComputeBufferParam(compute, kernel, IDs.NormalBuffer, normalBuffer);
                cmd.SetComputeBufferParam(compute, kernel, IDs.UVBuffer, uvBuffer);
                cmd.SetComputeBufferParam(compute, kernel, IDs.IndexBuffer, indexBuffer);
                BindAccumulationBuffers(cmd, compute, kernel, accumulation);

                SetAttribute(cmd, compute, "_VoxelGIPosition", mesh, VertexAttribute.Position, positionStream, true);
                SetAttribute(cmd, compute, "_VoxelGINormal", mesh, VertexAttribute.Normal, normalStream, hasNormals);
                SetAttribute(cmd, compute, "_VoxelGIUV", mesh, VertexAttribute.TexCoord0, uvStream, hasUV);
                cmd.SetComputeIntParam(compute, "_VoxelGIHasNormals", hasNormals ? 1 : 0);
                cmd.SetComputeIntParam(compute, "_VoxelGIHasUV", hasUV ? 1 : 0);
                cmd.SetComputeIntParam(compute, "_VoxelGIIndexStart", (int)subMesh.indexStart);
                cmd.SetComputeIntParam(compute, "_VoxelGIIndexCount", (int)subMesh.indexCount);
                cmd.SetComputeIntParam(compute, "_VoxelGIBaseVertex", subMesh.baseVertex);
                cmd.SetComputeIntParam(compute, "_VoxelGIIndexFormat", mesh.indexFormat == IndexFormat.UInt32 ? 1 : 0);

                Matrix4x4 objectToWorld = renderer.localToWorldMatrix;
                cmd.SetComputeMatrixParam(compute, "_VoxelGIObjectToWorld", objectToWorld);
                cmd.SetComputeMatrixParam(compute, "_VoxelGINormalToWorld", objectToWorld.inverse.transpose);
                BindMaterial(cmd, compute, kernel, material, opacityOnly);

                int triangleCount = (int)subMesh.indexCount / 3;
                cmd.DispatchCompute(compute, kernel, resources.Kernels.Voxelize.GroupsX(triangleCount), 1, 1);
            }
            finally
            {
                positionBuffer?.Dispose();
                normalBuffer?.Dispose();
                uvBuffer?.Dispose();
                indexBuffer?.Dispose();
            }
        }

        static void SetAttribute(CommandBuffer cmd, ComputeShader compute, string prefix, Mesh mesh,
            VertexAttribute attribute, int stream, bool present)
        {
            cmd.SetComputeIntParam(compute, prefix + "Stride", mesh.GetVertexBufferStride(stream));
            cmd.SetComputeIntParam(compute, prefix + "Offset", present ? mesh.GetVertexAttributeOffset(attribute) : 0);
            cmd.SetComputeIntParam(compute, prefix + "Format", present ? (int)mesh.GetVertexAttributeFormat(attribute) : 0);
            cmd.SetComputeIntParam(compute, prefix + "Dimension", present ? mesh.GetVertexAttributeDimension(attribute) : 0);
        }

        static void BindMaterial(CommandBuffer cmd, ComputeShader compute, int kernel, Material material,
            bool opacityOnly)
        {
            Texture baseTexture = material.HasProperty(BaseMap) ? material.GetTexture(BaseMap) : null;
            Texture emissionTexture = material.HasProperty(EmissionMap) ? material.GetTexture(EmissionMap) : null;
            Color baseTint = material.HasProperty(BaseColor) ? material.GetColor(BaseColor) : Color.white;
            Color emissionTint = material.HasProperty(EmissionColor) ? material.GetColor(EmissionColor) : Color.black;
            Vector2 baseScale = material.HasProperty(BaseMap) ? material.GetTextureScale(BaseMap) : Vector2.one;
            Vector2 baseOffset = material.HasProperty(BaseMap) ? material.GetTextureOffset(BaseMap) : Vector2.zero;
            Vector2 emissionScale = material.HasProperty(EmissionMap) ? material.GetTextureScale(EmissionMap) : Vector2.one;
            Vector2 emissionOffset = material.HasProperty(EmissionMap) ? material.GetTextureOffset(EmissionMap) : Vector2.zero;
            bool alphaClip = material.IsKeywordEnabled("_ALPHATEST_ON") ||
                             (material.HasProperty(AlphaClip) && material.GetFloat(AlphaClip) > 0.5f);
            float cutoff = material.HasProperty(Cutoff) ? material.GetFloat(Cutoff) : 0.5f;

            cmd.SetComputeTextureParam(compute, kernel, IDs.BaseMap, baseTexture != null ? baseTexture : Texture2D.whiteTexture);
            cmd.SetComputeTextureParam(compute, kernel, IDs.EmissionMap,
                emissionTexture != null ? emissionTexture : Texture2D.whiteTexture);
            cmd.SetComputeVectorParam(compute, "_VoxelGIBaseColor", baseTint);
            cmd.SetComputeVectorParam(compute, "_VoxelGIEmissionColor", emissionTint);
            cmd.SetComputeVectorParam(compute, "_VoxelGIBaseMapST",
                new Vector4(baseScale.x, baseScale.y, baseOffset.x, baseOffset.y));
            cmd.SetComputeVectorParam(compute, "_VoxelGIEmissionMapST",
                new Vector4(emissionScale.x, emissionScale.y, emissionOffset.x, emissionOffset.y));
            cmd.SetComputeIntParam(compute, "_VoxelGIAlphaClip", alphaClip ? 1 : 0);
            cmd.SetComputeFloatParam(compute, "_VoxelGIAlphaCutoff", cutoff);
            cmd.SetComputeIntParam(compute, "_VoxelGIOpacityOnly", opacityOnly ? 1 : 0);
        }

        static void BindAccumulationBuffers(CommandBuffer cmd, ComputeShader compute, int kernel,
            VoxelGIAccumulationBuffers accumulation)
        {
            cmd.SetComputeBufferParam(compute, kernel, IDs.AlbedoAccumulation, accumulation.Albedo);
            cmd.SetComputeBufferParam(compute, kernel, IDs.NormalAccumulation, accumulation.Normal);
            cmd.SetComputeBufferParam(compute, kernel, IDs.EmissiveAccumulation, accumulation.Emissive);
            cmd.SetComputeBufferParam(compute, kernel, IDs.OpacityAccumulation, accumulation.Opacity);
        }
    }
}
