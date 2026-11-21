# Python 环境配置

既然使用 Python 开发了，那么，你需要知道这些事情：

1. `uv` 是这里的包管理器，也就是说，它是这个项目 Python 侧的管理工具。
2. 这个项目的 Python 版本是 3.14
3. 我假定你对 Python 是有所了解的。

## 如果用 PyCharm 开发
## Step 1 准备软件

1. `git`
  这是一个用来进行代码同步的工具，需要这个软件才能克隆这个项目的源代码。  
  安装 [Git](https://git-scm.com/install/windows)，如果还没有安装的话，点这个链接下载安装包，安装一路下一步就可以。

1. `PyCharm`
  `PyCharm` 是在这里主要使用的编写工具。
  [PyCharm 下载地址](https://www.jetbrains.com/pycharm/download/)  

1. `uv`


## Step 1 克隆源代码和准备开发环境


1. 其次，是 Python 环境，这里推荐使用 `uv`， 一个极其轻量的 Python 虚拟环境和包管理器。  
   把这段脚本复制到 PowerShell 里即可（桌面右键 --> 终端）

   ```bash
   powershell -ExecutionPolicy ByPass -c "irm https://astral.sh/uv/install.ps1 | iex"
   ```



4. 接下来，是仓库的配置  
   如果需要对这个项目开发的话，需要 [Fork 代码](https://github.com/CILXRY/a2cr-Launcher/fork)
   ，也就是给代码仓库创建一个副本。  
   在创建一个 Fork 之后的项目，点击右上角的绿色的 `code`， 复制地址
5. 然后，在文件管理器里找一个存放代码的位置，打开终端

   ```bash
   git clone <刚刚复制的地址> # 比如， git clone https://github.com/CILXRY/a2cr-Launcher

   # Git 的配置
   git config --global user.name " 你的名字 "
   git config --global user.email " 你的邮箱 " # 设置和 GitHub 相同的邮箱
   ```

6. 最后，在 PyCharm 里打开这个存放代码的文件夹，在终端里运行 `uv run al` 即可。


## 如果用 VS Code 开发

## 如果是 Vibe Coding

我并不反对 Vibe Coding，
但是我更希望 AI 生成的东西是有价值的内容，
而非随随便便的东西就提交。

对于 AI 工具的选择有很多，不同于前面需要用编辑器，AI 工作甚至都不需要编辑器（但不建议这样做）

这里以 DeepSeek Harness 为例