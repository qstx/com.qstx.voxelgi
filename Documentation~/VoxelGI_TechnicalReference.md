# VoxelGI 技术参考

本文只描述当前插件的实际实现，目标版本固定为 **Unity 6000.3.16f1（Unity 6.3）** 与 **URP 17.3.0**。API、Render Graph Builder 和 Shader Library 以 Unity 6.3 官方 API 与项目使用的 URP 17.3 源码为准。插件包路径为 `Packages/com.qstx.voxelgi`。README 负责项目介绍、安装和快速开始；本文负责运行时架构、资源所有权、Shader/Compute 细节与限制。

## 1. 总体架构

VoxelGI 的实际运行路径如下：

```text
Volume Stack + 独立 Voxel Bounds
        ↓
VoxelGI 总控 ScriptableRenderPass.RecordRenderGraph
        ├─ Shadow：标准 URP ShadowCaster + VoxelGIShadow Blocker
        ├─ Voxelization：Compute Shader 读取 Raw Mesh Buffer
        ├─ Lighting：Direct / Optional Indirect / Manual Mipmap
        ├─ ScreenTrace：全屏体素 Cone Tracing
        ├─ Temporal：可选 Motion Vector 历史累积
        ├─ Bilateral：可选 Compute 双边滤波
        ├─ Debug：可选中间结果显示
        └─ Composite：复制并合成回相机颜色
```

普通表面渲染不由 VoxelGI 重写。场景材质直接使用 URP 的 `Universal Render Pipeline/Lit` 或其他具备标准 URP Pass 的 Shader；VoxelGI 只处理体素数据、屏幕空间 GI 和 Blocker 特殊行为。

## 2. 程序集与目录

```text
Packages/com.qstx.voxelgi/
├── package.json
├── Runtime/
│   ├── QSTX.VoxelGI.Runtime.asmdef
│   ├── VoxelGIRendererFeature.cs
│   ├── VoxelGIRendererRegistry.cs
│   ├── VoxelGIContributor.cs
│   ├── VoxelGIRuntimeResources.cs
│   ├── VoxelGISettings.cs
│   ├── VoxelGITypes.cs
│   ├── VoxelGIVolume.cs
│   ├── VoxelGIShaderIDs.cs
│   ├── ComputeVoxelizer.cs
│   └── Passes/VoxelGIRenderPass*.cs
├── Editor/
│   ├── QSTX.VoxelGI.Editor.asmdef
│   └── VoxelGIVolumeEditor.cs
├── Samples/CameraControls/
│   ├── QSTX.VoxelGI.Samples.asmdef
│   └── FreeFlyCameraController.cs / Touch*.cs
├── Samples/SampleScene/
│   ├── SampleScene.unity
│   ├── Voxel GI Volume Profile.asset
│   ├── Global Volume Profile.asset
│   ├── Rendering/                         # 示例 URP Pipeline/Renderer Data
│   └── Materials/                         # 场景专用材质与纹理
├── Tests/
│   ├── Editor/QSTX.VoxelGI.Tests.Editor.asmdef
│   └── Runtime/QSTX.VoxelGI.Tests.Runtime.asmdef
└── Shaders/
    ├── VoxelGICompute.compute
    ├── VoxelGI_URP.shader
    ├── VXGIBlocker.shader
    └── Includes/*.hlsl
```

`QSTX.VoxelGI.Runtime` 只引用 URP/Core Runtime；Editor Inspector 单独引用；相机和 uGUI 依赖隔离在 Samples 程序集。公共类型集中在 `QSTX.VoxelGI`，渲染内部类型默认 `internal`。

## 3. Volume 与帧上下文

### 3.1 `VoxelGIVolume`

`VoxelGIVolume` 继承 `UnityEngine.Rendering.Volume`，自身 Collider 负责影响区域，`m_VoxelizationBounds` 引用另一个 GameObject 上的 BoxCollider，负责体素网格覆盖范围。

`TryGetActive` 每次按以下规则选择 Volume：

1. 过滤未启用、Weight 为 0 或没有有效独立 Bounds 的对象。
2. Local Volume 只接受距离不大于 Blend Distance 的相机。
3. Global Volume 不做距离过滤。
4. 优先级最高者胜出；优先级相同则选择距离相机最近者。

体素 Bounds 会标准化为世界轴对齐立方体：

```text
side = max(sourceBounds.size.x, sourceBounds.size.y, sourceBounds.size.z)
gridBounds = Bounds(sourceBounds.center, (side, side, side))
voxelSize = side / voxelResolution
voxelToWorld = TRS(gridBounds.min, identity, (voxelSize, voxelSize, voxelSize))
```

因此体素单元始终是立方体，不会因长方体 Volume 产生非均匀体素。

### 3.2 `VoxelGISettings`

`VoxelGISettings` 是 Volume Profile 中唯一的 GI 参数组件，`enable` 控制是否激活。Renderer Feature 只持有 Fullscreen Shader、Compute Shader 和 Renderer 后备扫描间隔，不再重复保存 GI 参数。

运行时通过 `Resolve()` 将 Volume Parameters 转为一次性的只读 `VoxelGISettingsSnapshot`；分辨率限制为 16–256/64–4096，并归一为最接近的 2 的幂。Depth/Normal 上下阈值会保证上界严格大于下界，避免双边滤波除零。

### 3.3 Camera Context

每个 Base Game Camera 都有一个 `VoxelGICameraContext`，包含：

- 跨帧体素 3D 纹理：AlbedoOpacity、Normal、Emissive、DirectRadiance、FinalRadiance
- 方向光 Shadow Depth Texture
- Temporal History A/B、Ping-Pong 状态和 Jitter 状态
- 体素化更新所需的 Settings、Bounds、Light、Registry 和 Renderer Hash

四个原子累积 Buffer 与 MipScratch 由 Render Graph 在对应 Pass 内创建，只保留到当前帧中最后一次使用。Overlay、Scene View、Preview Camera 不执行总控 Pass。分辨率或上下文描述变化时释放并重建跨帧资源。

## 4. Renderer Registry 与更新模式

`VoxelGIRendererRegistry` 首次激活时扫描 `MeshRenderer`/`SkinnedMeshRenderer`，之后只在场景加载/卸载、Contributor 注册或 Feature 后备扫描周期到达时更新列表。每帧只遍历缓存并检查状态，不再每帧执行 `FindObjectsByType`。

`VoxelGIContributor` 可附加在运行时生成的 prefab 根对象上：

- `includeChildren`：是否注册子级 Renderer
- `contributeSurface`：是否注入 Albedo/Normal/Emissive；关闭但保留 `castVoxelShadow` 时仍写入 Opacity，作为辐射遮挡体
- `castVoxelShadow`：是否参与 VoxelGI 阴影

没有 Contributor 的 Renderer 默认同时参与表面、辐射遮挡和阴影；只有 `VoxelGIShadow`、没有 `ShadowCaster` 的材质会自动被识别为 Blocker-only。Blocker-only 不写 Albedo/Normal/Emissive，但会写入体素 Opacity，并参与方向光阴影。

体素更新模式：

| 模式 | 更新条件 |
|------|----------|
| `EveryFrame` | 每帧清空、体素化、解析并重新计算光照 |
| `OnChange` | Settings、Bounds、Volume UpdateVersion、方向光、Registry、Renderer 状态/矩阵变化；活动 Skinned Mesh 每帧更新 |
| `Manual` | 首次建立、资源描述变化或 `VoxelGIVolume.RequestVoxelizationUpdate()` |

材质属性脚本原地改变没有通用变更事件；在 OnChange/Manual 下需要显式调用更新 API。

## 5. Render Graph 阶段

`VoxelGIRenderPass.RecordRenderGraph` 是唯一入队入口，使用 Unity 6.3/URP 17.3 的当前 API。各阶段名称和类型如下：

| 名称 | Graph 类型 | 主要资源 |
|------|------------|----------|
| `VoxelGI/Shadow` | Raster | Depth Attachment、Renderer Draw |
| `VoxelGI/Voxelization` | Unsafe | Raw Mesh Buffer、原子 Buffer、3D UAV |
| `VoxelGI/Lighting` | Compute | GBuffer、Shadow、Radiance、Mip Scratch |
| `VoxelGI/ScreenTrace` | Raster | Camera Depth/Normals、Radiance、Transient Texture |
| `VoxelGI/Temporal` | Raster | ScreenTrace、Motion、Persistent History |
| `VoxelGI/Bilateral` | Compute | Input、Depth、Normals、Transient UAV |
| `VoxelGI/Debug` | Raster | 持久 3D 数据、2D 中间结果 |
| `VoxelGI/Composite` | Raster + Copy | Scene Color Copy、Indirect、Camera Color |

只有需要直接访问外部 Raw Mesh Buffer 的体素化阶段使用 Unsafe Pass。Shadow 使用正式 Depth Attachment；Bilateral 使用 Compute Pass；屏幕输出优先使用 Render Graph 帧内 TextureHandle，避免不必要的持久全分辨率 RT。

全屏绘制使用 URP/Core 的 Fullscreen Triangle 顶点函数和 `DrawProcedural`，不再创建自定义 Quad Mesh。Shader Pass 通过名称缓存，不依赖数字索引。

## 6. 资源格式与所有权

需要跨帧复用的体素纹理由 `RTHandles.Alloc` 创建并由 `VoxelGICameraContext` 独占释放，格式为跨 Windows/Metal 验证的 `R16G16B16A16_SFloat`：

| 资源 | Mip | 内容 |
|------|:---:|------|
| `AlbedoOpacity` | 否 | RGB Albedo，A Opacity |
| `Normal` | 否 | RGB 世界法线 0–1 编码，A 占用标记 |
| `Emissive` | 否 | Resolve 后的纯 HDR Emissive，供重光照与 Debug 复用 |
| `DirectRadiance` | 是 | Emissive + 方向光 |
| `FinalRadiance` | 是 | Direct + 第二次反弹 |
| `ShadowDepth` | 否 | 硬件 Depth32 Shadow Texture |

原子累积 Buffer 使用 transient BufferHandle；MipScratch 与屏幕资源 ConeTrace、Bilateral、Scene Copy、Composite 使用 transient TextureHandle。它们只在 Voxelization/Lighting 实际执行时创建并可由 Render Graph 资源池复用。Temporal History A/B 按相机分辨率持久化。

## 7. Compute 体素化

### 7.1 材质与网格输入

`ComputeVoxelizer` 从缓存 Registry 获取 Renderer，并过滤：启用状态、活动层级、`forceRenderingOff`、LayerMask、Bounds 相交、Triangle SubMesh、Opaque Render Queue。

每个 SubMesh Dispatch 一个 `VoxelizeMesh`：

- 普通 Mesh 使用 `Mesh.GetVertexBuffer`/`GetIndexBuffer`。
- Skinned Mesh 的 stream 0 使用 `SkinnedMeshRenderer.GetVertexBuffer` 获取当前变形结果。
- C# 将 stride、offset、format、dimension、index format、index start 和 base vertex 传给 Compute。

材质读取 URP/Lit 标准属性：

| 属性 | 用途 |
|------|------|
| `_BaseMap` / `_BaseColor` | Albedo 与 Opacity |
| `_EmissionMap` / `_EmissionColor` | HDR RGB Emissive |
| `_AlphaClip` / `_Cutoff` | Alpha Clip 判断 |

### 7.2 Kernel 与并发合并

| Kernel | 线程组 | 作用 |
|--------|--------|------|
| `ClearVoxelAccumulation` | `(256,1,1)` | 清空四个原子 Buffer |
| `VoxelizeMesh` | `(64,1,1)` | 每线程处理一个三角形 |
| `ResolveVoxelAccumulation` | `(8,8,8)` | 写入浮点 3D GBuffer |

三角形使用世界空间主轴投影：根据面法线最大分量选择 X/Y/Z 投影，在投影平面计算边函数、包围矩形、保守扩展和重心坐标。每个覆盖体素：

1. CAS 循环平均 Albedo。
2. CAS 循环平均 Normal（先编码到 0–1）。
3. `InterlockedMax` 合并 Opacity。
4. `InterlockedAdd` 累积 HDR Emissive RGB 和样本数。

Emissive 累积 Buffer 使用 `uint4`，RGB 以 1024 固定点缩放并限制到 64；Resolve 时除以样本数并写入持久 Emissive 纹理。四个累积 Buffer 均为当前 Voxelization Pass 的瞬态资源。所有 Kernel 都使用 `CeilDiv` 计算 Dispatch，并检查 `SV_DispatchThreadID` 边界。

## 8. 方向光阴影与光照

Shadow 阶段使用标准 URP/Lit 的 `LightMode="ShadowCaster"` Pass，手动设置方向光 View/Projection 并写入 Depth32 纹理；URP/Lit 的 Alpha Clip 和常规阴影属性因此可以直接复用。`VXGIBlocker.shader` 只实现 `LightMode="VoxelGIShadow"`，不含 UniversalForward；Compute 体素化会为它写入 Opacity，但不写表面颜色、法线或辐射。

`VoxelDirectLighting`：

- 从 Normal 纹理解码世界法线。
- 从独立 Emissive 纹理读取 Resolve 阶段写入的纯发光结果，避免重新光照时累积上一轮方向光结果。
- 根据 `WorldToShadow` 将带 Sun/Normal Bias 的体素位置映射到 Shadow Depth。
- 使用平台反向 Z 标记比较阴影。
- 计算 `saturate(dot(normal, -sun.forward))`，叠加方向光颜色/强度和 Albedo。
- 写回 `DirectRadiance`。

方向光阴影视锥由立方体 Bounds 半对角线拟合；没有活动方向光时保留 Emissive，但不采样阴影。

## 9. 间接光与 Mipmap

启用 Second Bounce 时，`VoxelIndirectLighting` 从 DirectRadiance 的 Mip 链沿法线半球进行 Fibonacci Cone Tracing：

```text
FinalRadiance = DirectRadiance + bouncedRadiance * Albedo * indirectIntensity
```

Indirect Cone 质量为 VeryLow/Low/Medium/High，对应 1/4/8/16 根 Cone；每根按 `max(dot(coneDir, normal), 0)` 加权，最后按权重总和归一。

Direct 和 Final 分别生成完整 Mip 链。每一级：`MipmapGeneration` 对源 Mip 的 2×2×2 体素求均值写入 Scratch，再由 `CopyTexture3D` 拷回目标 Mip。Unity 不自动生成此类 Random Write 3D Mip，因此由 Compute 显式执行。

## 10. 屏幕空间阶段

### 10.1 ScreenTrace

`ScreenTrace` 复用 URP 提供的 Camera Depth/Normals 输入，使用 `ComputeWorldSpacePosition` 重建世界坐标，经 `WorldToVoxel` 转为归一化体素坐标。天空像素提前返回；Ray/AABB 相交确定最大距离；Cone 半径决定采样 Mip。

非 Temporal 模式根据质量发射 1/2/4/8 根 Cone 并除以根数。Temporal 模式每帧发射一根 Cone，方向来自 Blue Noise 或程序化 Hash，Jitter 使用 Golden Ratio/Halton。

### 10.2 TemporalFilter

Temporal 只在 `temporalFilter` 开启且 Motion Vector 有效时记录：

1. `historyUV = currentUV - motionVector` 重投影。
2. 当前帧 3×3 邻域转 YCoCg，计算均值/方差。
3. 以 `temporalClampScale` 裁剪历史颜色。
4. 根据运动速度和 `temporalCurrentFrameWeight` 混合。
5. 输出到每相机 History Ping-Pong。

首次使用、分辨率/Volume/Bounds/设置变化时关闭 History 读取并从当前帧开始。

### 10.3 BilateralFilter

`BilateralFiltering` 使用固定 8 点 Poisson 邻域加中心样本；深度和法线权重分别由阈值范围计算，再写入 transient ARGBHalf UAV。上下阈值在 Settings Resolve 阶段保证严格不相等。

### 10.4 Composite

先用 Render Graph Copy Pass 保存相机当前颜色：

```text
CameraColor = SceneColorCopy + FinalIndirect
```

FinalIndirect 按 Bilateral → Temporal → ScreenTrace 的优先级选择，最终 Copy 回 Active Color。

## 11. Shader 组织与 Pass 复用

### URP 复用

VoxelGI 不维护自定义 Lit 表面 Shader。普通材质直接使用 URP/Lit 的：

- `ForwardLit`
- `GBuffer`
- `ShadowCaster`
- `DepthOnly`
- `DepthNormals`
- `Meta`
- `MotionVectors`

因此材质的常规 PBR、Alpha Clip、深度、法线和运动矢量行为跟随 URP 17.3。

### VoxelGI 专用 Pass

`VoxelGI_URP.shader` 只保留：

| Pass | 用途 |
|------|------|
| `ScreenTrace` | 屏幕空间 Cone Tracing |
| `TemporalFilter` | Motion Vector 历史滤波 |
| `Composite` | GI 与相机颜色合成 |
| `DebugVisualization` | 3D/2D 中间结果显示 |

`VXGIBlocker.shader` 只保留 `VoxelGIShadow`，用于不可见但同时阻挡方向光和体素辐射的对象。Compute Shader 入口文件只声明 8 个 Kernel，具体函数按 Common、Voxelization、Lighting 和 Filtering include 拆分。

## 12. 调试模式

`VoxelGISettings.debugMode` 提供：

`Disabled`、`Albedo`、`Normal`、`Emissive`、`Shadow`、`DirectRadiance`、`FinalRadiance`、`ScreenTrace`、`Temporal`、`Bilateral`。

Albedo/Normal/Emissive/Direct/Final 使用世界空间体素盒 Ray Marching，其中 Emissive 直接采样持久 Emissive 纹理；Shadow 显示 Depth Texture；ScreenTrace/Temporal/Bilateral 直接显示对应屏幕 Texture。调试模式会尽量提前结束不相关的后续阶段。

## 13. 当前限制

1. 当前只选择第一个活动 Directional Light。
2. 透明 Render Queue 不参与体素化。
3. `OnChange` 无法自动识别材质脚本原地修改，需要显式请求更新。
4. Runtime 新对象若没有 Contributor，要等待 Registry 后备刷新周期才能进入缓存。
5. 当前正式 GI 验收平台是 Windows D3D11/D3D12/Vulkan 和 macOS Metal；移动端只保证 Samples 相机与工程编译。
6. Editor 测试与 Runtime PlayMode 测试分属两个程序集，运行时需要分别选择对应程序集。

## 14. 验证流程

每次改动后：

1. 确认 Unity `6000.3.16f1`、URP `17.3.0`，并等待脚本编译完成。
2. 检查 Renderer Feature、Volume Profile、材质和场景引用。
3. 在 Unity Test Runner 中分别运行 `QSTX.VoxelGI.Tests.Editor` 与 `QSTX.VoxelGI.Tests.Runtime`。包内 Shader/Compute 测试通过稳定 `.meta` GUID 解析资源，不依赖包的绝对或固定安装路径。
4. Play Mode 中把 Main Camera 推进到局部 Volume 影响区，检查正常模式和各 Debug Mode 的画面。
5. 通过 Frame Debugger 或 Render Graph Viewer 确认上述阶段及 URP ShadowCaster 复用；用 Unity Profiler 检查 OnChange/Manual 静止帧没有体素化 Dispatch 和 Renderer 全量扫描。
6. 测试结束后退出 Play Mode，确认场景没有残留测试对象或运行时设置。
