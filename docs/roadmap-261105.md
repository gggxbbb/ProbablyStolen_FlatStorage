# 扁平存储 Flat Storage 路线图

本路线图的实现版本为 1.3.0，兼容游戏 `100-Rev6`。物品移动使用原生 `TryInventorySlot`、`MayPlayerInsertInto` 和 `SlotMarker.TryAcceptOnce`，遵循占格、容量、归属、嵌套及机器限制。

## 1.3.x · 一、稳定性修复（优先）

- [x] **场景切换保存布局**：`FlatStorageController.Close()` 按旧场景键保存，`Save()` 核对存档身份。读档前通过 `ModHook.OnGameLoadedInit` 释放原生窗口。
- [x] **异常处理与重新打开**：当前画布操作遇到异常立即终止，恢复原生窗口，通过 `ModLog` 记录异常并显示告警；用户可以重新打开画布。交易界面尚未就绪时提示原因并停止当前操作。
- [x] **清理 modData**：`SaveDataMaintenance` 和 `SaveDataPruning` 在读档后清理失效的打开记录及分组坐标，保留完整物品图中的跨场景记录。持久化标识必须为正数。
- [x] **声明兼容版本**：`manifest.xml` 使用 `<GameVersions><Version>100-Rev6</Version></GameVersions>`。
- [x] **排列入口**：排列统一使用 `StorageArrangement.Arrange`，`CanvasMath` 提供视口计算。

## 1.3.x · 二、体验与性能

- [x] **稳定分组尺寸**：宽高由窗口像素尺寸决定，标题溢出省略。内容数量、相似度及同一库存内的位置变化保留分组位置；容器归属、页面尺寸及可访问分组变化触发自动排列。
- [x] **视口外减少更新**：屏外 `WindowLease` 每秒更新位置和标题，跳过 `Validate` 与渲染节点扫描；返回视口时立即完整刷新。视图变化时才执行 `Apply()`，拖放预览和通知到期独立更新。
- [x] **本地化**：`Localization/zh.csv`、`en.csv` 经 `ModText.Get` 调用 `ModHelper.GetLocalized`，中文词条注册到游戏的 `zh-Hans` 语言。
- [x] **配置项**：`ModSettings` 提供新场景默认缩放、刷新间隔、边缘平移速度及新场景自动排列默认值，使用游戏配置与选项界面。

## 1.3.x · 三、新功能

- [x] **物品搜索、过滤与高亮**：按名称、标识符或类别查询，支持类别交集筛选；匹配物品显示边框，其他物品变暗。回车或“下一个”逐项聚焦，滚动库存同步滚动到对应物品。
- [x] **小地图与分组目录**：点击目录或地图中的容器定位分组，点击地图空白处移动视口。地图显示当前可见范围，并随布局和窗口尺寸更新。

## 1.3.x · 四、进阶

- [x] **批量整理**：按照真实指针命中选择目标页面，将其他可访问页面中相同 `identifier` 的物品归并到目标已有样品。预览列出目标、逐项数量与限制，确认时重新核验整批状态，逐件调用原生插入。条件变化时停止，已完成的转移保留。
- [x] **原生接口接入**：`NativeTabsAccess` 根据真实分页对象的 `plate`、`text`、`page` 字段绑定节点；读档与出行使用 `ModHook`。外框由 `CustomUIManager` 管理，仓储区域保留原生物品指针行为。

## 1.3.x · 五、发布

- [x] **发布资源**：保留 `preview.png` 原图，打包时生成小于 1 MB 的上传副本；中英文描述位于 `Workshop/description.zh.txt` 与 `description.en.txt`。
- [x] **本地发布脚本**：`Publish.cmd` 调用 `scripts/Publish-Workshop.ps1`，使用 VDF 库生成上传文件。`-ValidateOnly` 执行构建、游戏外测试、资源核验、预览处理和安装包验证。
- [x] **版本更新验证流程**：发布脚本执行 `Tests`，`GAMEPLAY_CHECKLIST.md` 记录每次游戏版本更新后的交互与 F8 `mod-scan flat_storage` 检查。

## 验收与发布

游戏内测试及 F8 控制台检查由使用者执行，结果记录在 [游戏内验证清单](../GAMEPLAY_CHECKLIST.md)。Steam Workshop 发布等待使用者验收完成后进行。
