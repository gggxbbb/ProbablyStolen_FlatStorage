# 扁平存储 Flat Storage 路线图

基于 1.2.0 代码审阅整理。原则：物品移动始终走原生 `TryInventorySlot` / `MayPlayerInsertInto`，不绕过占格与容量规则。

## 1.2.x 稳定性修复（优先，低成本）

- [ ] **场景切换保存布局**：`FlatStorageController.cs:215-216` 改为按旧场景键 `Save()` 后再关闭，不再 `CloseWithoutSave()`。
- [ ] **失败降级**：`ReportFailure`（`65-75`、`225-229`）只关闭画布并告警，不设 `enabled=false`、不 rethrow；`OpenTrading`（`363-368`）交易 UI 未就绪时安全跳过。日志统一用 `ModLog`。
- [ ] **清理 modData**：读档时剪枝已不存在物品的 `opened:{uniqueId}`（`OpenedContainers.cs:16-29`）和无用分组坐标；处理 `uniqueId == 0`。
- [ ] **钉版本**：`manifest.xml` 的 `<GameVersions>` 填 `100-Rev6`。
- [ ] 删除或标注已弃用的 `CanvasMath.Arrange`（`CanvasModel.cs:69-81`）。

## 1.3 体验与性能

- [ ] **稳定分组尺寸**：宽高以窗口像素为准，标题溢出省略（`NativeAccess.cs:74`）；内容/相似度变化与结构变化解耦，避免拖放后整图跳动。
- [ ] **视口外真正休眠**：屏外 lease 跳过 `Validate`/子树扫描并降频（`WindowLease.Cull`、`RefreshSources 273-298`）；`Apply()` 仅在脏时执行。
- [ ] **本地化**：新增 `Localization/zh.csv`、`en.csv`，改用 `ModHelper.GetLocalized`，替换 `FlatUI.cs`、`FlatStorageController.cs`、`ModEntry.cs`、`StorageDiscovery.cs` 中的硬编码文案。
- [ ] **配置项**：默认缩放、刷新间隔、边缘平移速度、自动排列默认值。

## 1.4 新功能

- [ ] **物品搜索 / 过滤 / 高亮**：按名称或类别过滤，非匹配格变暗，回车逐个跳转聚焦。
- [ ] **小地图 / 分组目录**：点击聚焦到对应容器，配合柜台中心布局。

## 2.0 进阶

- [ ] **批量整理**：如“同类归并到指针下容器”，逐件走原生插入与占格检测，可撤销或先预览。
- [ ] **收缩 Harmony 面**：去掉对 `MachineryTabs` 子节点 `i*2`/`i*2+1` 结构的假设（`NativePatches.cs:27-28`、`NativeAccess.cs:212/223`），能用 `ModHook` 就不用补丁；界面外框迁移到 `CustomUIManager`。

## 发布

- [ ] `preview.png` 与 Workshop 描述（中英文）。
- [ ] steamcmd 上传 vdf 脚本，本地一键发布。
- [ ] 每个游戏小版本更新后跑一遍 `Tests` 并在 F8 控制台执行 `mod-scan`。
