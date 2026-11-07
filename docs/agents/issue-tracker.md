# Issue tracker: GitHub

任务与需求记录在 `gggxbbb/ProbablyStolen_FlatStorage` 的 GitHub Issues，使用 `gh` CLI 操作。

## 操作约定

在仓库目录执行命令。每次会话首次写入前，运行 `gh repo view --json nameWithOwner`，核对目标为 `gggxbbb/ProbablyStolen_FlatStorage`。

多行正文先通过文件编辑工具保存到已忽略的 `.work/issues/` 目录，再使用 `--body-file` 传入。命令失败时立即报告实际错误，核实原因后继续相应操作。

| 操作 | 命令或约定 |
| --- | --- |
| 创建任务 | `gh issue create --title "标题" --body-file .work/issues/body.md` |
| 读取任务 | `gh issue view <number> --json number,title,body,labels,comments` |
| 列出任务 | `gh issue list --state open --json number,title,body,labels,comments`，按需要添加 `--label` 和调整 `--state` |
| 创建子任务 | `gh issue create --parent <parent> --title "标题" --body-file .work/issues/body.md` |
| 关联已有子任务 | `gh issue edit <parent> --add-sub-issue <child>` |
| 添加前置任务 | `gh issue edit <child> --add-blocked-by <blocker>` |
| 统计未完成的前置任务 | `gh api repos/gggxbbb/ProbablyStolen_FlatStorage/issues/<number> --jq .issue_dependencies_summary.blocked_by`，返回 0 表示前置任务均已完成 |
| 添加评论 | `gh issue comment <number> --body-file .work/issues/comment.md` |
| 修改标签 | `gh issue edit <number> --add-label "标签"` 或 `--remove-label "标签"` |
| 关闭任务 | 先通过独立的 `gh issue comment` 命令记录处理结果，再执行 `gh issue close <number>`；处理已关闭任务时，评论同样单独提交 |

子任务关系与前置依赖使用 GitHub 原生功能。任务的所有前置依赖关闭后，该任务才具备开始条件。

技能要求“发布到任务系统”时，创建 GitHub Issue；要求“读取相关任务”时，读取对应 Issue 的正文、标签和评论。

## Pull requests as a triage surface

**PRs as a request surface: no.**

## Wayfinding operations

- 总任务使用 `wayfinder:map` 标签，正文记录笔记、已有决定和待解决的问题。
- 工作项作为总任务的 GitHub 子任务，按类型使用 `wayfinder:research`、`wayfinder:prototype`、`wayfinder:grilling` 或 `wayfinder:task` 标签。
- 按总任务中的顺序选择仍然打开、前置依赖均已完成且无人负责的子任务。
- 领取任务使用 `gh issue edit <number> --add-assignee @me`。
- 完成任务后添加结果评论、关闭子任务，并在总任务正文中补充结论与结果链接。
