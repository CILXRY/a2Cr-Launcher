# 发布规范

## 1. 版本号

SemVer：`主.次.修`。

- `0.x` 阶段允许破坏性变更（v2.0 之前的都算探索）；
- 加了功能 → 次版本 +1；只修 bug → 修订号 +1；
- **版本号由 [Release Please](https://github.com/googleapis/release-please) 管理**，
  它读 Conventional Commits（见 `git.md` 第 2 节）决定 bump 哪一位，并维护
  `.release-please-manifest.json`。

**唯一来源就是 manifest 里那个值。** 任何别处（`.csproj`、界面显示的版本号）都必须
**从它派生**，不许手写第二份。v1 的教训是版本号写在两处（`pyproject.toml` + `__init__.py`），
忘同步就不一致。

派生方式二选一：

1. **Release Please 直接更新项目文件**（它有针对 .NET 的更新策略，会改 `<Version>`）；
2. 用 `simple` 策略只维护 manifest + CHANGELOG，构建时由 `Directory.Build.props`
   读 manifest 注入 `$(Version)`。

优先 1：少一层间接。用不了 1 再用 2。

## 2. 版本节奏

- 初始 manifest 设成 `2.0.0`；
- **M5 完成时手动发一次 `v2.0.0`**（第一次发布值得干净、人工确认）；
- 之后每次合入带 `feat` / `fix` 的 PR，Release Please 自己开 release PR，合并即发版。

## 3. CHANGELOG

Release Please 自动生成 `CHANGELOG.md`，**但发版前要人工润色一遍**：

- 它生成的是 commit 标题的汇总，会混进"重构了 xxx"这种用户不关心的条目；
- 发版前把用户能感知的变化整理到前面，内部改动挪到"内部变更"小节；
- 版本小节标题保持 `## [2.1.0] - 2026-12-01` 的格式，方便 diff；
- 现有的 `CHANGELOG.md` 里 v1 的历史保留不动，v2 小节从 `2.0.0` 开始往下追加。

## 4. tag

| tag | 谁打 | 什么时候 |
|---|---|---|
| `docs-v2-spec` | 手动 | 契约冻结，可以开始照着写代码 |
| `v2-mN` | 手动 | 里程碑完成（M1–M5），例如 `v2-m1` |
| `v2.x.y` | **Release Please 自动** | 合并 release PR 时 |

- 手动只打前两种；`v2.x.y` 不要手打，否则和 manifest 里的值对不上；
- 打 tag 前工作区必须干净、CI 必须绿（见 `git.md` 第 4 节）。

## 5. 构建产物

| 指标 | 上限 | 为什么 |
|---|---|---|
| 单文件体积 | **60 MB** | 直接对标 v1 的 132 MB（其中 33 MB 是误打包的 `pyrefly`） |
| 输出文件数 | **20 个** | v1 有 1900 多个文件，解压慢、杀软扫描慢 |

超出就要在 PR 里解释，或者砍依赖。

## 6. 发布流程

1. 跑 `12-验收与回归.md` 第 5 节的检查清单；
2. 合并 Release Please 开出的 release PR（它会 bump 版本 + 更新 CHANGELOG + 打 tag）；
3. **润色** CHANGELOG 里那一节（用户能感知的放前面）；
4. 构建 Windows 产物，附到对应的 GitHub Release 上；
5. Release 说明指向 CHANGELOG 的对应小节。

> 构建和上传在 M5 之前保持手动：出问题时少一层流水线，排查快得多。
> Release Please 只负责版本号、CHANGELOG 和 tag，不负责打包。

## 7. 回滚

- 线上问题是**打包/配置**造成的 → 重新发一个修订版，不回退 tag；
- 问题是**代码逻辑**造成的 → 从 release 分支 cherry-pick 修复，同样发修订版；
- **永远不要**把已经发布的 tag 改指向。

## 8. 暂不做

- 自动更新（差分更新要等单文件发布稳定后再设计）；
- 自动构建与上传产物（M5 之后再评估）。
