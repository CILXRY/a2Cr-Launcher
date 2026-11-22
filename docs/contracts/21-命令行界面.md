# CLI

> **完善中**：未定稿，`【待确认】` 处需要拍板；和 [`产品想法.md`](../产品想法.md) 冲突时改本文。

CLI 是**薄壳**：解析参数 → 调 Core → 打印结果。和 GUI 共用同一套 Core，
任何"只在 CLI 里实现"的行为都是 bug。

- 可执行文件：`a2Cr`（Windows 上是 `a2Cr.exe`；大小写按 [`01-名称定义.md`](01-名称定义.md)，不许写成 `a2cr` / `A2cr`）
- v1 的入口是 `al`，v2 改名为 `a2Cr`；需要的话可以再挂一个 `al` 别名，但**新文档只写 `a2Cr`**

## 1. 全局选项

| 选项 | 说明 |
|---|---|
| `--root <path>` | 覆盖数据根目录，优先级高于 `A2CR_ROOT` |
| `--json` | 机器可读输出：**stdout 只有一个 JSON 对象**，进度和警告走 stderr |
| `--verbose` | 输出调试信息（含网络请求、命令行预览，**已脱敏**） |
| `--yes` | 跳过交互确认；非交互环境必须显式给这个参数 |
| `--no-progress` | 关闭进度条（重定向到文件时自动关闭，无需手动给） |

## 2. 命令集

| 命令 | 参数 | 里程碑 | 说明 |
|---|---|---|---|
| `version` | `[--type release\|snapshot\|all] [--limit N] [--refresh]` | M1 | 列出可安装版本；`--refresh` 强制联网 |
| `install` | `<version_id> [--loader fabric\|quilt\|forge\|neoforge] [--loader-version V]` | M1 / M2 | 安装原版，或直接装加载器版本 |
| `launch` | `--version <id>` \| `--instance <folder>`，外加 `--memory`、`--java-major`、`--offline`、`--account <id>` | M1 | 启动；默认**前台**跑并转发日志，`--detach` 才脱离 |
| `java` | `list` | M1 | 列出发现到的 Java（路径 + 主版本） |
| `doctor` | — | M1 | 自检：目录可写、磁盘余量、Java、硬链接、代理/镜像连通性 |
| `instance` | `list` / `create <name> --version <id>` / `clone <folder> <new>` / `remove <folder>` / `open <folder>` | M2 | 实例管理 |
| `mod` | `search <关键词>` / `install <project> --instance <folder>` / `list --instance <folder>` | M2 | Modrinth 模组 |
| `import` | `<路径> [--name X] [--update <folder>] [--dry-run]` | M3 | 导入 / 更新整合包；`--dry-run` 只预检不落盘 |
| `login` | — | M4 | 设备码登录微软账号 |
| `account` | `list` / `use <id>` / `remove <id>` | M4 | 多账号管理 |
| `logout` | `[--all]` | M4 | 退出当前 / 全部 |
| `settings` | `get [key]` / `set <key> <value>` | M5 | 读写设置；`key` 用点号路径，如 `download.max_concurrency` |
| `storage` | `usage` / `gc [--yes]` | M5 | 占用分析、清理无人引用的池内容 |

约定：

- **`launch` 用选项而不是位置参数**（`--version` / `--instance`），因为它是"二选一的选择器"，
  位置参数在这里会含糊；其余命令的必填对象仍然用位置参数。
- 命令名和参数名一旦发布不再改，脚本会坏。新增能力用新命令或新的可选参数。
- 删除实例进回收站，CLI 也一样，**不提供 `--hard`**；要硬删自己删目录。
- `instance clone` 默认走对账 + 硬链接，`--copy` 强制全量复制。
- `storage gc` 会删东西，**必须** `--yes` 或在交互终端里确认。

## 3. 退出码

| 码 | 含义 |
|---|---|
| 0 | 成功（含"全部跳过"） |
| 1 | 执行失败 |
| 2 | 参数错误 / 用法错误（含"需要确认但没给 `--yes`"） |
| 3 | 用户取消（Ctrl+C、取消下载） |

**取消和失败必须用不同退出码**，否则脚本分不清"我不想装了"和"装不上"。
`Ctrl+C` 也要归到 3，不要变成 1。

## 4. 输出约定

- 默认给人看：中文、短句、一条结论一行。
- `--json`：stdout 只有一个**顶层对象**，结构稳定：

  ```json
  { "ok": true,  "data": { } }
  { "ok": false, "error": { "code": "download_failed", "message": "…", "hint": "…" } }
  ```

  顶层永远是对象，不是裸数组——这样以后加字段不会破坏调用方。
- 进度条、警告、日志一律走 **stderr**；stdout 只放结果。
- 错误统一格式：`错误：<一句话>（建议：<下一步>）`，不打堆栈；`--verbose` 例外。
- **禁止**打印 `access_token` / `refresh_token` / `curseforge_key`；`--verbose` 下的命令行预览
  也必须脱敏（见 [`08-账号.md`](08-账号.md) 第 6 节）。
- 非交互环境（没有 TTY）默认不提问：需要确认的操作直接以退出码 2 失败并提示加 `--yes`。

## 5. `doctor` 检查什么

这是 M1 的验收工具，也是用户报问题时第一个该跑的命令：

| 检查 | 失败时的提示方向 |
|---|---|
| 数据目录存在且可写 | 权限 / 杀软拦截 / 路径在只读盘 |
| 目标盘剩余空间 | 给具体数字，不要只说"空间不足" |
| Java 发现结果 | 按主版本列出找到的，没有的给出安装建议 |
| 硬链接可用性 | 同卷内建一个临时硬链接再删掉；失败说明会降级为复制 |
| 代理 / 镜像连通性 | 分项报告，一项失败不影响其它项 |
| 版本元数据缓存 | 有没有、多久以前 |

`doctor` 只读，不修任何东西（除了一对临时的硬链接测试文件，用完立刻删）。

## 6. 与 GUI 的一致性

CLI 的每个操作都对应 Core 里的一个方法，GUI 调同一个方法。

CLI **不需要**实现：界面主题、窗口尺寸、截图、导入向导的逐步交互。
`import --update` 直接跑完整流程即可。

## 7. Smoke 检查

CI 至少跑：

```powershell
dotnet run --project src/a2Cr.Cli -- --help
dotnet run --project src/a2Cr.Cli -- version --json --root ./artifacts/ci-root
dotnet run --project src/a2Cr.Cli -- doctor --json --root ./artifacts/ci-root
```

- 第二条不联网时应能从缓存或明确报错，**不能挂住**；
- 第三条在没有 Java 的 CI 机器上也要以退出码 0 结束（`doctor` 是"报告"，不是"断言"）。
