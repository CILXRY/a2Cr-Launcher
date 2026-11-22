# 为 a2Cr Launcher 做贡献

尽管这只是一个个人练习项目，但仍然对你的贡献予以感谢。

> **v1（Python + Flet）已冻结**，只接受 bugfix。新功能请按
> [`docs/a2Cr Launcher 2 Contracts/`](docs/a2Cr%20Launcher%202%20Contracts/00-索引.md)
> 的 v2（C# / Avalonia）契约来做。
## 从"要做什么"开始

这里 ➡️ 是 [项目 Issues](https://github.com/CILXRY/a2cr-Launcher/issues)，  
你可以修复其中的 [Bugs（问题）](https://github.com/CILXRY/a2cr-Launcher/issues?q=is%3Aissue+state%3Aopen+label%3ABug)，  
实现其中的 [Features（功能）](https://github.com/CILXRY/a2cr-Launcher/issues?q=is%3Aissue+state%3Aopen+label%3Aenhancement)，  
又或者只是单纯地 [提出一个想法](https://github.com/CILXRY/a2cr-Launcher/issues/new/choose)。

不确定某件事该进 v1 还是 v2 时，默认答案是 **v2**，并且先去
[`docs/产品想法.md`](docs/产品想法.md) 看它是否属于这个项目想解决的问题。

## 开发

### 1. 环境 & 分支

- **主分支**：`elegant`
- **主分支保护**：需要 Pull Request、需要线性历史、阻止强制 Push

```powershell
git switch -c feat/xxx origin/elegant
# ... 改代码 ...
git add -A
git commit -m "feat(xxx): 一句话说清楚做了什么"
git push -u origin feat/xxx
```

- 合并前先 `git fetch && git rebase origin/elegant`

### 2. 确认环境

- 写 v2：按 [`docs/guides/01b-C# 环境配置.md`](docs/guides/01b-C%23%20环境配置.md) 装好 SDK 和 Avalonia 模板。
- 改 v1：

  ```powershell
  cd legacy/v1-python
  uv sync
  uv run al --help        # CLI 能起来就说明环境 OK
  uv run python -m tests.smoke   # 冒烟测试，必须全绿
  ```

### 3. 目录归属

| 路径 | 说明 |
|---|---|
| `docs/产品想法.md` | 需求依据，改需求先改这里 |
| `docs/contracts/` | v2 契约，**改接口前先动这里 + 决策记录** |
| `docs/guides/` `docs/learning/` | 上手文档与学习笔记 |
| `legacy/v1-python/` | v1 代码，只修 bug |

### 4. 冻结的东西

- v1 的接口（`download`、`install`、`services.*`、`get_version_meta`）见
  [`legacy/v1-python/docs/CONTRACT.md`](legacy/v1-python/docs/CONTRACT.md)：只许加可选参数，不许改语义。
- v2 的接口见 `docs/contracts/`。要改，先在 `12-决策记录.md` 补一行说明为什么。

### 5. 提交前自检（硬性）

写 v2 时：

```powershell
dotnet build
dotnet test
```

改 v1 时：

```powershell
cd legacy/v1-python
uv run --group lint pyrefly check   # 类型检查
uv run python -m tests.smoke        # 冒烟测试全绿
uv run al --help                    # CLI 能起来
```

**没有跑过的改动不要提 PR。** PR 描述里贴验证结果（命令输出或截图）。

### 6. 禁止事项

1. `core/` 层里出现 `print` / 直接写文件（输出归 CLI / UI，写文件走唯一入口）
2. 同一个功能写两套实现（进度、重试、校验……发现重复先合并）
3. 改冻结接口不通知、不更新文档
4. 一个 PR 做三件事、改二十个文件
5. 提了 AI 生成的大段代码，但自己解释不了它在干什么
6. 把 `build/`、`bin/`、`obj/`、`artifacts/` 提交进仓库

### 7. 任务怎么写

不好的任务：**"把整合包导入做得稳一点"**

好的任务包含四样：

```text
目标：导入 mrpack 时，若实例目录已存在则跳过已下载文件，不重复下载
涉及：src/a2Cr.Core/Modpack/
不要动：Download/（接口已冻结）
验收：
  1. a2Cr import X.mrpack 跑两次
  2. 第二次日志里全是 skipped，不产生网络请求
  3. 实例目录内容不变
```

**"正确"必须是可验证的。** 给不出验收命令，就说明任务还没想清楚。

## 提交与版本

使用 [Conventional Commit messages](https://www.conventionalcommits.org/zh-hans/v1.0.0/) 作为提交信息，
PR 用 Squash Merge 合并到 `elegant`，保持线性历史。

版本由 [Release Please](https://github.com/googleapis/release-please) 管理：它读 Conventional
Commits 决定版本号、维护 `CHANGELOG.md` 并打 tag，配置在根目录的
`release-please-config.json`。细节见 [`docs/conventions/release.md`](docs/conventions/release.md)。

问文件，不问功能——功能描述会骗人，文件列表不会。
