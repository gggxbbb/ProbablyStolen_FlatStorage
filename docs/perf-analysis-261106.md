# 扁平存储 性能分析与修改建议（2026-11-06）

基于 `master` / tag `1.3.0`（`dec4f8e`）静态审阅。原则不变：物品移动仍走原生 `TryInventorySlot` / `MayPlayerInsertInto`，优化只动调度与缓存，不改存档语义。

## 结论

1.3.0 的搜索、小地图、树目录、自动入库、框选本身不是主因；卡顿来自它们叠在「每 0.25s 对所有可见窗口做全量子树 Validate」和「每帧多次 `RaycastAll`」上。优先做**脏刷新**和**射线缓存**，通常比继续拧 UI 细节更划算。自动入库保持按需执行即可。

零改代码也可先试：设置里把刷新间隔调到 `0.5`–`1.0` 秒，立刻减负，但物品进出画布的可见延迟会变大。

---

## 1.3.0 相对 1.2.0 加重 CPU 的功能

| 功能 | 额外成本形态 |
|---|---|
| 搜索 / 分类高亮 | 定时重建 `StorageCatalog`，有搜索时为每件物品建/改高亮边框 |
| 小地图 | 每帧投影全部 mark，并更新视口四边框 |
| 容器树 | signature 变时清容器重建行，并双次 `ForceUpdateCanvases` |
| 自动入库 | 按需：亲和度 + 批量预览/执行（非常驻帧） |
| 跨容器框选 | 候选物品多次射线命中判断遮挡 |
| 定时刷新连带 | 每 `ModSettings.RefreshInterval`（默认 0.25s）全量同步时重建目录 |

---

## 热路径

### 每帧（画布打开：`Update` → `UpdateFrame` → `UpdateCanvas`）

| 函数 | 位置 | 开销原因 |
|---|---|---|
| `UpdateCanvas` | `FlatStorageController.cs:352-397` | 每帧跑完再 `Apply` + `UpdateStatus`；拖动物品时还有边缘平移与 `UpdateDrag` |
| `PointerHit` / `PointerOnBlank` / `OwnsPointer` | `:280-313` | `EventSystem.RaycastAll`，整棵 UI 射线 |
| `SelectionRegionAt` | `:254-272` | 再 `RaycastAll` + `leases.Any(IsChildOf)` |
| `OwnsInventoryPointer` / `OwnsInventoryDropPointer` | `:219-240` | 经 `SelectionRegionAt`；Drop 还对每个 `MachineryTabs` 做嵌套 `Any` + `NativeTabsAccess.Get` + `FindDropInventoryAt` |
| Harmony `CanvasInputPriority` / `CanvasInventoryMove` | `NativePatches.cs:165-186`、`NativeSelection.cs:73-78` | 输入更新里反复读 `OwnsInventoryPointer` → 再射线 |
| `CanvasSelectionOcclusion` → `IsTopItemAt` | `NativeSelection.cs:140-148`、`FlatStorageController.cs:274-278` | 框选扫每个候选物品再 `RaycastAll` |
| `Apply` → `lease.Cull` + `tools.UpdateView` | `FlatStorageController.cs:438-450`、`WindowLease.Cull:175-184`、`StorageMiniMap.UpdateView:69-98` | 视口脏时遍历全部 lease；小地图重算投影并摆 mark |
| `UpdateStatus` | `:452-467` | 每帧字符串比较；有 notice 时 `GetPreferredValues` + `Place` |
| `UpdateDrag` / `ScaleDragPreview` / `RefreshDragTarget` | `:469-487`、`NativeAccess.cs:39-43` | 反射 `ItemMouseDragHandler.Drag`；预览缩放与居中 |

### 定时（默认刷新间隔 0.25s，可配 0.1–2）

| 函数 | 位置 | 开销原因 |
|---|---|---|
| `RefreshSources` | `FlatStorageController.cs:399-436` | 定时全量同步总入口 |
| `StorageDiscovery.Collect` | `StorageDiscovery.cs:21-96` | 递归 Emporium 图 + `visibleWindows`，再 `OrderBy` |
| `WindowLease.Refresh`（可见） | `NativeAccess.cs:126-161` | `GetComponentsInChildren(true)` + `CaptureLayoutSizes` + `window.Validate()` + Tab 快照 |
| `WindowLease.Refresh`（屏外） | `:128-133` | 已降到约 1s 只改布局，仍占定时循环 |
| `ArrangementChanges.Observe` | `ArrangementChanges.cs:23-27` | 每 tick `OrderBy.ToArray` + `SequenceEqual` |
| `CanvasTools.Refresh` | `CanvasTools.cs:80-102` | 新建 `StorageCatalog`、扫类型、`Filter`、小地图 `Refresh`；目录开着还可能 `BuildNavigation` |
| `StorageCatalog` 构造 | `StorageCatalog.cs:25-43` | 对每个 source 再整树 `Visit` |
| `Filter` → `SearchHighlights.Apply` | `CanvasTools.cs:112-121`、`StorageCatalog.cs:57-87` | 全物品匹配；有搜索时为每件物品建/改 4 条高亮边框 |
| `CatalogItem.Matches` / `StorageSearch.Matches` | `StorageCatalog.cs:13-17`、`StorageSearch.cs:9-15` | 每件物品 `ToArray` / 多次 `IndexOf` |
| 自动排列触发时 `ArrangeLayout` | `FlatStorageController.cs:537-548`、`StorageArrangement.cs:49+` | 相似度矩阵 O(n²)，再全体 `lease.Refresh` |

### 打开画布

| 函数 | 位置 | 开销原因 |
|---|---|---|
| `Open` → `RefreshSources`（可能 `ArrangeLayout`）→ `Apply` | `FlatStorageController.cs:87-143` | 首次挂载全部窗口 |
| `WindowLease` 构造 | `NativeAccess.cs:81-124` | 扫子树、禁用移动、`TryAddWindow`、`openCallback`、`Refresh` |
| `CanvasTools` 构造 | `CanvasTools.cs:31-62` | 搜索框 / 下拉 / 小地图等 UI |

### 拖放中 / 拖放后

| 函数 | 位置 | 开销原因 |
|---|---|---|
| `UpdateDrag`（拖中每帧） | `FlatStorageController.cs:469-474` | 预览缩放 + 反射刷新落点 |
| `ValidateDrop` Prefix | `NativePatches.cs:310-328` | 原生落点核验（正确性必需） |
| `RefreshNow`（自动入库执行后等） | `FlatStorageController.cs:518-523` | 立刻再走整套 `RefreshSources` |
| `AutoStorage.Preview` / `Build` / `Execute` | `AutoStorage.cs`、`BulkTransfer.cs` | 按需尖刺，不在常驻帧循环 |
| 树目录 `StorageTreeView.Build` | `StorageTreeView.cs:12-82` | 清容器重建行 + 两次 `Canvas.ForceUpdateCanvases` |

---

## 瓶颈 Top 5

1. **`WindowLease.Refresh`（`NativeAccess.cs:136-159`）**  
   每个可见窗口每 0.25s 全子树 `GetComponentsInChildren` + `window.Validate()`（并牵动 `FlatTabsLayout` 对各 page 的 `Validate`，`NativePatches.cs:40`）。窗口 / 机器一多就是主 CPU 峰。

2. **每帧多次 `RaycastAll`（`FlatStorageController.cs:284`，经 `Owns*` / Harmony / 框选遮挡）**  
   同一指针一帧里被输入补丁和画布逻辑重复打；框选时还按物品次数放大。

3. **`CanvasTools.Refresh` → `new StorageCatalog` + `Filter`（`CanvasTools.cs:80-99`，`StorageCatalog.cs:25-43`）**  
   发现已经走过一遍图，刷新又整树扫物品；无搜索时仍建目录并跑 `Filter`。

4. **`OwnsInventoryDropPointer`（`FlatStorageController.cs:225-240`）**  
   在射线之外再线性扫全部 Tab 节点；拖放过程中被 `ValidateDrop` / 组拖结束频繁读取。

5. **`ArrangementChanges.Observe` + 自动排列（`ArrangementChanges.cs:23-27`，`StorageArrangement.cs:70-72`）**  
   每 tick 排序对比；一旦 `Pending`，全量 O(n²) 重排并再次 `Refresh` 全部 lease。

---

## 修改建议（按收益 / 成本）

### P0 · 脏标记驱动 Refresh

- **改哪**：`WindowLease`、`RefreshSources`
- **怎么改**：结构、尺寸、`childItems` 数量/引用变化，或从屏外唤醒时，才做 `GetComponentsInChildren` + `Validate`；平时只 `RefreshPlacement` / 轻量核对。物品增减、分页变化、打开/关闭窗口路径必须标脏。
- **预期**：定时峰值下降约一个数量级。
- **风险**：漏脏会导致页签 / 尺寸短暂不同步——唤醒与物品增减路径要测全。

### P0 · 每帧只做一次 RaycastAll

- **改哪**：`FlatStorageController`（`PointerHit` / `Owns*` / `SelectionRegionAt` / `PointerOnBlank`）
- **怎么改**：增加 `frameHit`（帧号 + 缓存命中列表），同一帧指针未动则复用；Harmony 输入补丁走同一缓存入口。
- **预期**：指针相关 CPU 砍到约 `1/N`。
- **风险**：低。

### P1 · 框选遮挡少打射线

- **改哪**：`IsTopItemAt`、`CanvasSelectionOcclusion`
- **怎么改**：同屏坐标缓存结果；或先用 lease 包围盒粗滤再射线。
- **预期**：大范围框选更顺。
- **风险**：中——遮挡语义要与原生一致，别误选被挡住的格。

### P1 · 搜索目录与租赁刷新解耦

- **改哪**：`CanvasTools.Refresh`、`StorageCatalog`、`SearchHighlights`
- **怎么改**：无 query / category 时跳过 `new StorageCatalog` 与 `SearchHighlights.Apply`；有搜索再按间隔或物品脏重建。类型下拉可隔几拍或在脏时更新。
- **预期**：无搜索时定时成本接近「发现 + 轻量 placement」。
- **风险**：低；打开搜索瞬间可能有一帧尖刺。

### P1 · lease / window 查找改字典

- **改哪**：`RefreshSources`、`SelectionRegionAt`、`OwnsInventoryDropPointer`
- **怎么改**：用 `Dictionary<PixelWindow, WindowLease>`（可复用 `WindowLease.Active`）替代 `FirstOrDefault` / `Any`。
- **预期**：窗口多时线性扫描消失。
- **风险**：极低。

### P2 · 精简 OwnsInventoryDropPointer 的 Tab 探测

- **改哪**：`OwnsInventoryDropPointer`
- **怎么改**：先用单次射线命中判断是否在 plate / text 上，再查 `WindowLease.Tabs`；去掉每帧对所有 tabs 的嵌套 `Any`。
- **预期**：拖放帧更稳。
- **风险**：中——必须仍能点到分页标题落物。

### P2 · ArrangementChanges 廉价签名

- **改哪**：`ArrangementChanges.Observe`
- **怎么改**：用「数量 + 拼接 id/parent/宽高」或递增 version，避免每 tick `OrderBy.ToArray` + `SequenceEqual`；自动排列保持用户开关。
- **预期**：定时尾部变便宜，误触发全量 Arrange 减少。
- **风险**：低；别漏掉归属变化。

### P2 · StorageSearch.Matches 去掉热路径分配

- **改哪**：`StorageSearch.Matches`、`CatalogItem.Matches`
- **怎么改**：调用方传入已物化的类型列表 / 缓存，避免每件 `categories.ToArray()`。
- **预期**：有搜索时 GC 与过滤更快。
- **风险**：极低。

### P2 · 小地图增量更新

- **改哪**：`StorageMiniMap.UpdateView` / `Refresh`
- **怎么改**：缓存投影；仅 view 变时动四边框，groups 不变不重建 `MiniMapProjection`。
- **预期**：平移缩放更轻。
- **风险**：低。

### P3 · 树目录减负

- **改哪**：`StorageTreeView.Build`、`CanvasTools.Signature`（现为 `JsonConvert.SerializeObject`，约 `:130-131`）
- **怎么改**：去掉双次 `ForceUpdateCanvases`（必要时一次即可）；signature 改轻量字符串拼接。
- **预期**：开着目录时刷新尖刺下降。
- **风险**：低。

---

## 不建议动的地方

为正确性必须保留的成本，只能减调用频率，不能删逻辑或换成「猜位置」：

- `BulkTransfer.Execute` / `MayMove` / `TryInventorySlot` / `TryAcceptOnce`：存档与容量语义核心
- `ValidateDrop` / 组拖 `EndGroupDrag` 的原生落点核验：跨容器拖放安全阀
- 可见窗口进入视口时的完整 `Refresh`（`Cull` waking）：否则页签平铺与机器 UI 会错
- `FlatTabsLayout` 在真正需要重算时对 page 的 `Validate`：平铺尺寸依赖它
- `RenderingTransfer.KeepWorldTransform` 与拖放预览缩放补丁：保证画布缩放下预览 / 落点正确
- `AutoStorage` 执行前的 snapshot / `SameSteps` 重算：防并发改库存导致错搬（按需尖刺，非常驻帧）

---

## 建议落地顺序

1. P0 脏刷新 + 射线缓存（一起做，收益最大）
2. P1 目录解耦 + 字典查找 + 框选粗滤
3. P2 Tab 探测 / 排列签名 / 搜索分配 / 小地图增量
4. P3 树目录
5. 每步用大存档（多机器、多嵌套容器）对比：打开画布尖刺、空闲定时、拖放、框选、开着搜索与目录时的帧时

验收时建议在 F8 控制台顺带跑一遍 `mod-scan`，并确认跨容器拖放与自动入库仍走原生插入路径。
