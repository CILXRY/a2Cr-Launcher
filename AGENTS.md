# AGENTS.md

给在这个仓库里干活的 AI / 开发者看的说明。**先读这份**，再按需要读 `docs/`。

面向用户的文案、代码注释、提交信息一律用中文。

## 现在是什么状态

- **v1（Python + Flet）已冻结**，tag `v1-final`，全部代码在 `legacy/v1-python/`，只修致命 bug。
  它的开发规则在 `legacy/v1-python/AGENTS.md`（只在真的去改 v1 时才需要读）。
- **v2（C# + Avalonia）在设计阶段**，契约已定、**还没有一行代码**。
- 主分支：`elegant`。

动 v1 之前先问一句"这是不是应该进 v2"。默认答案：是。

## 目录职责

| 路径 | 职责 |
|---|---|
| `docs/产品想法.md` | **为什么**做这个启动器；判断功能该不该做的依据 |
| `docs/contracts/` | v2 的**唯一事实来源**：接口、磁盘布局、错误语义、验收 |
| `docs/guides/` | 环境配置、任务怎么写 |
| `docs/learning/` | 学习笔记，不参与实现 |
| `docs/screenshots/` | v1 界面截图 |
| `legacy/v1-python/` | v1 全套代码，只读参考 |

`src/` 和 `tests/` 目前不存在。v2 开工时按 `docs/contracts/02-目录结构.md` 和
`docs/guides/01b-C# 环境配置.md` 建：`src/a2Cr.Core`（核心）、`src/a2Cr.Cli`（命令行）、
`src/a2Cr.app`（Avalonia）、`tests/a2Cr.Core.Tests`。

## 铁律

1. **契约冲突时改实现，不改文档。** 要改文档，先在 `docs/contracts/12-决策记录.md` 加一行写清为什么。
2. **一份文档只管一件事。** 补充内容写进它该在的那份文件，不要顺手塞进别的文档。
3. **`a2Cr` 的写法不能错**（`A2cr` / `a2CR` 都是错的），全小写 `a2cr` 只用于包名。见 `docs/contracts/01-名称定义.md`。
4. **路径只有一个来源**：v2 由 `LauncherPaths` 统一提供，业务代码不许自己拼 `Environment.GetFolderPath`。
5. **底层不 print**：core 层不打日志、不写文件（写文件只走 `04-文件存储.md` 定义的入口），输出交给 CLI / UI。
6. **取消不是失败**：取消抛 `OperationCanceledException`，绝不能被包成普通 `RuntimeError` 之类的失败。
7. **游戏日志不集中**：永远读实例目录里的 `logs/latest.log`，不要做链接或集中收集。

## 验证底线

v2 代码还没开始，下面这条在 `src/` 建好之后生效：

```powershell
dotnet build
dotnet test
```

改 v1 的话，底线在 `legacy/v1-python/` 里：

```powershell
cd legacy/v1-python
uv run python -m tests.smoke
uv run al --help
```

## 不要提交

`build/`、`artifacts/`、`bin/`、`obj/`、`.venv/`、`*.DotSettings.user` —— `.gitignore` 已经挡住，
如果它们出现在 `git status` 里，说明是在别的目录建了项目或者忽略了规则。

## 改文档时

- 链接用相对路径，不要写绝对路径。
- 截图放 `docs/screenshots/`，临时日志不要进仓库。
- 新增契约文件：在 `docs/contracts/00-索引.md` 的表格里加一行，并续编号。
