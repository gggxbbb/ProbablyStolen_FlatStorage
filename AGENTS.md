## Agent skills

### Issue tracker

任务与需求记录在本仓库的 GitHub Issues。读取或修改任务前，阅读 `docs/agents/issue-tracker.md`。

### Triage labels

任务分类使用五个默认标签。判定任务状态或修改分类标签前，阅读 `docs/agents/triage-labels.md`。

### Domain docs

领域文档采用 single-context 布局。调查代码、描述领域概念或提出架构改动前，阅读 `docs/agents/domain.md`。

## 发布

执行发版前，阅读 `README.md` 的“打包与发布”、`GAMEPLAY_CHECKLIST.md` 和 `scripts/Publish-Workshop.ps1`。构建与打包规则以 `scripts/Validate-Release.ps1`、`scripts/prepare_workshop.py` 和项目文件中的 `ModFile` 清单为准。

### 发布范围与版本

- 常规发版包含版本更新、验证、Git 提交与同名注释标签、推送和 Steam Workshop 更新。用户明确限定范围时，执行其指定部分；GitHub Release 按用户的明确要求创建。
- 使用用户指定的版本号；需要自行确定版本时，根据改动选择符合现有版本规则的版本号，并在发布前说明。同步修改 `ProbablyStolen_FlatStorage.csproj` 的 `Version`、`manifest.xml` 的 `ModVersion`，以及 README、验收清单中的版本和安装包名称。
- 在 `Workshop/changelogs/<version>.txt` 中维护对应版本的中英文更新日志，发布脚本会将全文写入上传配置。实际游戏截图保存到 `Workshop/screenshots/<version>/`。
- 游戏兼容版本取自实际游戏资源的 `PlayerSettings.bundleVersion`。修改兼容范围时，同时更新清单与相关验证；主菜单显示的版本文字用于记录实测环境。
- 检查工作区、当前分支、`origin` 和已有 Git 标签，只暂存本次发布的文件。GitHub CLI 在权限受限环境中返回认证错误时，先按工具权限流程到正常用户环境验证，再判定凭据状态。

### 构建与验证

使用 PowerShell 7、.NET SDK 10、Python 3 和本地游戏程序集。Python 发布依赖由 `scripts/requirements-release.txt` 固定，发布脚本安装到已忽略的 `.work/release-python/`。自动执行时直接调用 PowerShell 脚本：

```powershell
pwsh -NoProfile -File .\scripts\Publish-Workshop.ps1 -ValidateOnly
```

游戏位于其他目录时，通过 `-GameDir` 指定路径；工具位置通过 `-Python`、`-DotNet` 指定。`Publish.cmd -ValidateOnly` 是对应的交互入口。

- 确认发行 DLL 使用普通 Release 构建，`PerformanceRuntimeValidation` 保持关闭。实际游戏自动验证使用隔离存档副本及独立验证目录。
- 发布验证必须全部通过：构建、游戏外测试、程序集与清单版本、实际资源及哈希、双语 CSV、预览图、Workshop 配置和 ZIP 内容。测试数量以本次执行结果为准。
- 将待发布内容安装到本地游戏，按 `GAMEPLAY_CHECKLIST.md` 检查基础打开与关闭流程及本次改动涉及的操作，记录游戏版本、步骤和实际结果。性能改动同时保存可比较的场景条件与录制数据。
- 发布产物为 `dist/FlatStorage-<version>.zip`；独立内容目录、JPEG 预览图和生成的 `upload.vdf` 位于本次 `.work/releases/<run>/`。后续上传使用本次脚本返回的路径，并核对待上传 DLL 与实际验收构建的哈希。

### 提交与 Git 标签

验证通过后提交发布文件，使用完整版本号作为注释标签名称，例如 `<version>`。标签必须指向本次发布提交；已有同名标签指向其他提交时，停止该次发布并报告冲突。

将发布分支与指定版本标签推送至已核对的 `origin`。通过远程查询确认分支提交和标签解引用结果均指向发布提交。

### Steam Workshop 上传

更新现有条目 `3833606510`，应用为 `4348910`，可见性为公开。目标配置取自 `Workshop/upload.vdf`。常规发布保留现有中英文标题和简介；用户要求更新文案时，分别维护 `Workshop/description.zh.txt` 与 `Workshop/description.en.txt`，每份 UTF-8 长度小于 8000 字节，并按对应语言更新页面。

保留 `preview.png` 源文件，使用发布脚本生成的 JPEG 上传。通过以下入口执行验证与上传，SteamCMD 位置可通过 `-SteamCmd` 指定：

```powershell
pwsh -NoProfile -File .\scripts\Publish-Workshop.ps1
```

脚本会重新构建和验证，随后读取 `PUBLISH` 与 Steam 账户名。用户已经明确要求发布时，按本次授权完成上传确认；密码和 Steam Guard 验证由用户在 SteamCMD 中输入。核对本次实际生成的版本、内容目录和上传配置后继续上传。

更新多语言说明或附加截图时，可通过已登录 Steam 客户端的 Steamworks 接口提交。使用已验证的同一份发布内容，核对条目所有者、目标语言、截图文件和提交结果；附加图片保持真实游戏画面，每张小于 1 MB。

### 发布完成条件

核对 SteamCMD 或 Steamworks 接口的实际上传成功结果、Workshop 页面更新时间与变更说明、中英文标题及简介。附加截图上传后核对服务端图片清单。下载已发布内容，检查版本、文件清单与关键文件哈希；核对本地游戏安装内容与发行包一致。

最终记录发布版本、提交、Git 标签、Workshop 链接、验证结果和安装包路径。只有远程 Git 与 Workshop 内容均已核实，才将完整发版标记为完成；限定范围的任务按其实际完成部分报告。
