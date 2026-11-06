# Game Grid Tile 模块速查文档

本模块实现一个基于网格数据的 2D 地形系统：用 bool 网格存储每个格子的占用情况，
运行时根据邻居关系计算 mask，从 TileSet（ScriptableObject）解析出对应 Sprite，
再合并成 Mesh 渲染；动画格子则生成独立的 SpriteRenderer 播放帧动画。

## 文件地图

| 文件 | 职责 |
| --- | --- |
| `GameGridRenderer.cs` | 核心渲染器（MonoBehaviour）。持有网格尺寸/层数、terrainLayers 数据，负责 mask 计算、静态地形合批、AnimatedTile 生成与清理、编辑器 paintMode 预览。 |
| `GameGridTerrainLayer.cs` | 单个地形层的数据容器：TileSet 引用 + `bool[] cells`（按 `z*width*height + y*width + x` 索引）、排序设置。内含被注释掉的 bit 压缩存储方案。 |
| `GameGridTileSet.cs` | TileSet 抽象基类（ScriptableObject）。定义 `GameGridTileSetMode`、`MaskTileVariant`（随机变体/帧动画）、`Resolve(mask, position)` 统一入口。 |
| `DualGrid16TileSet.cs` | Dual Grid 模式：16 张图，sprite 放在格子角上，由周围 4 个数据格决定。`UsesCornerLayout = true`。含 `SliceToTileSlot` 贴图槽位映射。 |
| `Blob47TileSet.cs` | Blob 模式：47 张图，sprite 放在格子中心，由 8 邻居 mask 决定。含 256 种 mask 到 47 规范形的 `CanonicalizeBlobMask` 归一化。 |
| `CustomMaskRulesTileSet.cs` | 自定义规则模式：`customRules` 列表，精确 mask 匹配优先，否则取第一个“位全部命中”的规则。 |
| `GameGridAnimatedTile.cs` | 挂在 AnimatedTile 对象上的帧动画组件（SpriteRenderer 轮播 frames）。 |
| `GameGridItemInstance.cs` | 物件刷（item brush）放置的道具标记，记录 `cell` 与 `prefab`，用于按格擦除。 |

编辑器代码在 `Assets/Editor/GameGridTileEdit/`：

| 文件 | 职责 |
| --- | --- |
| `GameGridRendererEditor.cs` | Renderer 的 Inspector 与 Scene 视图工具：Terrain 层列表、Brush Layer/Z/Size、Paint/Item 模式、笔刷预览与绘制、道具放置/擦除、`__Grid_Items` 容器管理。菜单 `GameObject/2D Object/Game Grid Renderer`。热键 `[` / `]` 循环切换 Brush Layer。 |
| `GameGridTileSetEditor.cs` | TileSet 资产 Inspector：贴图槽位网格、拖拽/对象选择赋图、`AutoAssignTiles` 一键从源图集切片分配、自定义 mask 矩阵绘制（含等距菱形）、`MaskTileVariantDrawer` 变体行绘制。 |
| `GameGridTileSetMenus.cs` | 创建菜单 `Assets/Create/Game Grid Tile Set/...`，按 Rectangle / Isometric 创建三种 TileSet。 |
| `GameGridTileSetPickerWindow.cs` | 从工程里筛选与 Renderer 类型兼容的 TileSet 弹窗（按具体类名搜索）。 |
| `GameGridItemPaletteWindow.cs` | 道具面板（菜单 `Tools/Game Grid Item Palette`），浏览 prefab 并设置当前刷的道具。 |
| `GameGridItemBrushState.cs` | 全局静态状态：`SelectedPrefab`（当前道具刷选中的 prefab）。 |

## 核心数据流

1. **数据存储**：`GameGridRenderer.terrainLayers` 每层一个 `GameGridTerrainLayer`，
   `cells` 是 `bool[width * height * LevelCount]`，索引 `GetCellIndex(x, y, z)`。
2. **Rebuild**：`GameGridRenderer.Rebuild()` 先 `ClearGenerated()` 再逐层 `BuildTerrain`。
   编辑器下只有 `Application.isPlaying` 或 `editorPaintMode` 才真正构建，否则只清空预览。
3. **Mask 计算**：
   - Corner 布局（DualGrid16）：遍历 `(width+1) * (height+1)` 个角点，
     用周围 4 格拼出 0-15 的 mask（`BuildDualGridTerrain`）。
   - Blob / Custom 布局：遍历每个被占用的格子，用 8 邻居拼 mask（`BuildBlobTerrain` + `GetBlobMask`）。
4. **解析 Sprite**：`set.Resolve(mask, cellPos)` 先查 `maskVariants`（随机变体或帧动画），
   没有命中再走子类的 `GetTileForMask(mask)`。
5. **渲染**：
   - 静态格子按 Sprite 纹理分组（`Dictionary<Texture2D, MeshBuffers>`），
     合并为一个 GameObject 的多 subMesh Mesh（`CreateTerrainObject`），
     每个纹理一个材质（`CreateMaterial`）。
   - 动画格子不走合批，`CreateAnimatedTile` 生成独立对象 + `SpriteRenderer` +
     `GameGridAnimatedTile`，用 TileSet 的 `materialTemplate` 直接赋 `sharedMaterial`。

## 关键约定与陷阱

- **生成对象清理**：所有运行时生成的对象（地形 Mesh、AnimatedTile）都用
  `HideFlags.DontSave | HideFlags.HideInHierarchy`，名字以 `__Grid_` 开头；
  `ClearGenerated()` 先销毁追踪列表，再按 `__Grid_` 前缀清扫残留子对象，
  防止域重载/场景重载后遗留（`generatedObjects` 列表不序列化）。
- **材质策略**：静态地形优先直接复用 `materialTemplate`（其 `_MainTex`
  与子网格贴图一致时）；当模板为空或贴图不同时，才 `new Material(template)`
  克隆并按 subMesh 设置 `_MainTex`，避免多图集合批串图。只有克隆出的材质
  登记进 `generatedMaterials` 统一销毁，模板材质不会被清理。
  AnimatedTile 用 `SpriteRenderer`，直接引用模板材质即可。
- **cell 索引**：`z * width * height + y * width + x`；改尺寸走 `Resize` /
  `EnsureData` 的内容保留拷贝。`cells` 目前是 `bool[]`，bit 压缩方案在
  `GameGridTerrainLayer.cs` 中以注释形式保留。
- **mask 位定义**：Blob 的 8 邻居位是 `N=1, NE=2, E=4, SE=8, S=16, SW=32, W=64, NW=128`；
  DualGrid 的角点 mask 是 bit0=左下、bit1=右下、bit2=左上、bit3=右上。
- **槽位映射**：Blob47/DualGrid16 的 `blobTiles` / `tiles` 数组按“源图集切片顺序”
  存储，运行时通过 `SliceToTileSlot` / `Blob47TileSlotToSlice` 换算，
  手工补图时注意方向，不要按 mask 顺序直接填。
- **编辑器状态**：paintMode / itemMode / brush 参数都存在 Renderer 的
  `editor*` 序列化字段上（仅 Editor 程序集可见的包装属性），
  改完记得 `EditorUtility.SetDirty`。

## 常见修改入口

| 想做的事 | 位置 |
| --- | --- |
| 调整渲染方式（合批/排序/材质） | `GameGridRenderer.CreateTerrainObject` / `CreateMaterial` |
| 加新的 TileSet 模式 | 继承 `GameGridTileSet`，实现 `Mode/SpriteCount/GetTileForMask/GetSpriteAt/UsesCornerLayout`，并在 `GameGridTileSetPickerWindow.SearchTypes` 注册 |
| 改 mask/邻居规则 | `GameGridRenderer.GetBlobMask`（Blob）、`BuildDualGridTerrain`（Dual）、`CustomMaskRulesTileSet.GetTileForMask` |
| 改动画行为 | `GameGridTileSet.MaskTileVariant.TryGetAnimation`（参数）+ `GameGridAnimatedTile.Update`（播放） |
| 改笔刷/绘制交互 | `GameGridRendererEditor.PaintAt / PaintLine / ApplyBrushCells / AppendBrushCells` |
| 改道具刷 | `GameGridRendererEditor.HandleItemEditing / PlaceItemAt / EraseItemsAt`，面板在 `GameGridItemPaletteWindow` |
| 改层级填充/清空 | `GameGridRendererEditor.DrawTools` 中的 `Fill Brush Terrain` / `Clear Brush Level` 按钮（只作用于当前 Brush Layer + Brush Z） |
| 改清理策略 | `GameGridRenderer.ClearGenerated` |
| 一键自动分配贴图 | `GameGridTileSetEditor.AutoAssignTiles` |
