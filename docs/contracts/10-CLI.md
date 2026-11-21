# CLI

> **完善中**：未定稿，`【待确认】` 处需要拍板；和 [`产品想法.md`](../产品想法.md) 冲突时改本文。

对于 CLI，
Python 的入口点在： `src/a2cr_launcher/__init__.py`
C# 的入口点在：`src/a2Cr.Cli/a2Cr.cs`

## C# 说明

CLI 与 GUI 共用 `A2cr.Core`，不允许各自实现一套。  
CLI 是薄壳：解析参数 → 调 Core → 打印结果。

## 1. 全局选项

| 选项            | 说明                                                    |
| --------------- | ------------------------------------------------------- |
| `--root <path>` | 覆盖数据根目录，优先级高于 `A2CR_ROOT`                  |
| `--json`        | 机器可读输出（stdout 只输出 JSON，进度与提示走 stderr） |
| `--verbose`     | 输出调试信息                                            |
| `--yes`         | 跳过交互确认                                            |

## 2. 命令集

| 命令       | 参数                                                                     | 说明                         |
| ---------- | ------------------------------------------------------------------------ | ---------------------------- |
| `version`  | `--type release\|snapshot\|all` `--limit N` `--refresh`                  | 列出可安装版本               |
| `install`  | `<version_id>`                                                           | 安装原版或已存在的加载器版本 |
| `launch`   | `<version_id>` `--memory 4G` `--name` `--java`                           | 启动已安装版本               |
| `login`    | —                                                                        | 设备码登录微软账号           |
| `logout`   | `[--all]`                                                                | 退出当前账号 / 全部          |
| `account`  | `[list\|use <id>]`                                                       | 查看或切换账号               |
| `import`   | `<路径>` `--name` `--update <folder>`                                    | 导入或更新整合包             |
| `instance` | `list` / `clone <folder> <新名字>` / `remove <folder>` / `open <folder>` | 实例管理                     |
| `mod`      | `search <关键词>` / `install <project> <folder>` / `list <folder>`       | Modrinth 模组                |
| `java`     | `list`                                                                   | 列出发现的 Java              |
| `settings` | `get [key]` / `set <key> <value>`                                        | 读写设置                     |

约定：

- 命令名与参数名一旦发布不再改，脚本会坏。新增能力用新命令或新可选参数。
- 删除实例进回收站，CLI 也如此，不提供 `--hard`（要硬删就自己删目录）。
- `instance clone` 默认硬链接，可用 `--copy` 强制全量复制。
- 交互式命令（`login`、有风险的删除）在 `--yes` 下不提问。

## 3. 退出码

| 码  | 含义                 |
| --- | -------------------- |
| 0   | 成功（含"全部跳过"） |
| 1   | 执行失败             |
| 2   | 参数错误 / 用法错误  |
| 3   | 用户取消             |

取消与失败必须用不同退出码，脚本才能区分"我不想装了"和"装不上"。

## 4. 输出约定

- 默认给人看：中文、短句、每条结论一行。
- `--json`：stdout 只有一个 JSON 对象，结构稳定；所有进度条和警告走 stderr。
- 进度条只在 `--verbose` 或交互终端里显示；重定向到文件时自动关闭。
- 错误统一格式：`错误：<一句话> (<可选的下一步建议>)`，不要打印堆栈（`--verbose` 例外）。
- **禁止**打印 `access_token` / `refresh_token`。

## 5. 与 GUI 的一致性

CLI 支持的每个操作都对应 Core 里一个方法，GUI 调同一个方法。任何"只在 CLI 里实现"的行为都是 bug。

CLI 不需要实现的功能（v2.0）：界面主题、窗口大小、截图、更新整合包向导的交互步骤（`import --update` 直接跑完整流程即可）。

## 6. Smoke 检查

CI 至少跑：

```powershell
dotnet run --project src/A2cr.Cli -- --help
dotnet run --project src/A2cr.Cli -- version --json --root ./artifacts/ci-root
```

第二条不联网时应能从缓存或明确报错，不能挂住。
