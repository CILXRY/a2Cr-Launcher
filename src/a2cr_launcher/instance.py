"""整合包实例的元数据管理。

每个实例是 ``instances/<名字>/`` 目录，里面除了游戏文件（mods、config、saves 等）
还有一个 ``instance.json`` 记录它用的是哪个 Minecraft 版本、哪个加载器。
"""

import json
import re
import shutil
from datetime import datetime
from pathlib import Path

from a2cr_launcher.core.paths import INSTANCES_DIR, TRASH_DIR

META_FILE = "instance.json"


def sanitize_name(name: str) -> str:
    """把整合包名字变成可以当文件夹名的形式。"""
    cleaned = re.sub(r'[<>:"/\\|?*\x00-\x1f]', "_", name).strip().strip(".")
    return cleaned[:64] or "instance"


def instance_dir(name: str) -> Path:
    return Path(INSTANCES_DIR, name)


def _new_instance_dir(name: str) -> tuple[str, Path]:
    """重名时自动加后缀，避免覆盖已经导入的整合包。"""
    folder = sanitize_name(name)
    candidate = folder
    index = 2
    while instance_dir(candidate).is_dir() and any(instance_dir(candidate).iterdir()):
        candidate = f"{folder}-{index}"
        index += 1
    return candidate, instance_dir(candidate)

def _meta_define(name: str, folder: str, mc_version: str, version_id: str,
                 loader: dict | None = None, source: dict | None = None) -> dict:
    """只负责组装元数据，不做任何文件系统操作（folder 必须是已经确定好的目录名）。"""
    return {
        "name": name,
        "folder": folder,
        "mc_version": mc_version,
        "version_id": version_id,
        "loader": loader or {"type": "vanilla", "version": ""},
        "source": source or {},
        "created_at": datetime.now().isoformat(timespec="seconds"),
        # 记录整合包托管了哪些文件；更新整合包时只清理这些文件，不碰玩家自己放进去的东西
        "managed_files": [],
    }


def create(name: str, mc_version: str, version_id: str, loader: dict | None = None,
           source: dict | None = None) -> dict:
    """
    创建一个实例并写入 instance.json。

    Args:
        name: 显示名字，同时用来生成目录名（重名会自动加后缀）
        mc_version: 基础 Minecraft 版本
        version_id: 启动时使用的版本 id（可能是加载器版本，如 fabric-loader-0.16.9-1.21.1）
        loader: {"type": "fabric", "version": "0.16.9"} 之类的加载器信息
        source: 整合包来源信息

    Returns:
        dict: 实例元数据（含实际目录名）
    """
    folder, directory = _new_instance_dir(name)
    directory.mkdir(parents=True, exist_ok=True)

    meta = _meta_define(name, folder, mc_version, version_id, loader, source)
    with open(Path(directory, META_FILE), "w", encoding="utf-8") as f:
        json.dump(meta, f, ensure_ascii=False, indent=2)
    return meta


def load(name: str) -> dict:
    """
    按照 实例名/实例目录，读取实例元数据
    Args:
        name (str): 实例名/实例目录

    Returns:
        dict: 实例所存储的元数据，.get("status", None) 获取实例元数据状态
    """
    instance_directory = Path(INSTANCES_DIR, name)
    meta_path = Path(instance_directory, META_FILE)

    # region 确认是否存在文件
    meta_exists = meta_path.is_file()
    if not meta_exists:
        return {"status": "MetaNotExist"}
    # endregion

    # region 尝试读取文件
    try:
        # utf-8-sig：兼容用记事本改过、带 BOM 的 instance.json
        with open(meta_path, "r", encoding="utf-8-sig") as f:
            meta = json.load(f)
    # endregion

    # region 处理非 Dict 的情况
    except (OSError, json.JSONDecodeError):
        return {"status": "MetaBroken"}
    if not isinstance(meta, dict):
        return {"status": "MetaBroken"}
    # endregion

    # region 默认值
    meta.setdefault("status", "Success")
    meta.setdefault("folder", name)
    meta.setdefault("loader", {"type": "vanilla", "version": ""})
    meta.setdefault("source", {})
    meta.setdefault("managed_files", [])
    # endregion

    return meta


def _adopted_meta(name: str) -> dict:
    """给没有 instance.json 的目录合成一份"未托管实例"元数据（只读，不写盘）。

    早期版本启动原版时直接在 instances/<版本号> 下建游戏目录、不写元数据，
    于是这些目录永远不出现在实例列表里。这里让它们能被看见；
    真正补写 instance.json 发生在 ensure() 里。
    """
    return {
        "status": "Success",
        "name": name,
        "folder": name,
        "mc_version": name,
        "version_id": name,
        "loader": {"type": "vanilla", "version": ""},
        "source": {"format": "adopted"},
        "created_at": "",
        "managed_files": [],
        "_adopted": True,
    }


def list_all() -> list[dict]:
    """列出所有实例（按名字排序）。

    - 正常：instance.json 可读（status == "Success"）
    - 未托管（_adopted=True）：目录里没有 instance.json，按目录名推断成原版实例
    - 损坏（_broken=True）：instance.json 存在但读不出来，保留现场，绝不覆盖
    """
    result = []
    try:
        folders = sorted(Path(INSTANCES_DIR).iterdir())
    except OSError:
        return result
    for folder in folders:
        if not folder.is_dir():
            continue
        meta = load(folder.name)
        status = meta.get("status")
        if status == "MetaBroken":
            broken = _adopted_meta(folder.name)
            broken.pop("_adopted", None)
            broken["_broken"] = True
            broken["status"] = "MetaBroken"
            broken["source"] = {"format": "broken"}
            result.append(broken)
        elif status == "Success":
            result.append(meta)
        else:
            result.append(_adopted_meta(folder.name))
    return result


def ensure(version_id: str, mc_version: str | None = None) -> dict:
    """保证 instances/<version_id> 是一个可用实例，返回它的元数据。

    - 已有且版本对得上 → 直接返回
    - 目录存在但没有元数据 → 收养（补写 instance.json，source.format = adopted）
    - 目录不存在 → 新建
    - 同名目录属于别的版本 → 自动加后缀，不覆盖
    - 元数据损坏 → 抛 ValueError，交给用户处理
    """
    meta = load(version_id)
    status = meta.get("status")

    if status == "MetaBroken":
        raise ValueError(
            f"实例元数据损坏，请手动修复或删除：{Path(instance_dir(version_id), META_FILE)}"
        )

    if status == "Success":
        if meta.get("version_id") == version_id:
            return meta
        # 目录名被别的实例占用了，换一个名字
        return create(name=version_id, mc_version=mc_version or version_id, version_id=version_id)

    base = mc_version or version_id
    directory = instance_dir(version_id)
    if directory.is_dir():
        adopted = _adopted_meta(version_id)
        adopted.pop("_adopted", None)
        adopted["mc_version"] = base
        save(adopted)
        return adopted

    return create(name=version_id, mc_version=base, version_id=version_id)


def find_by_source(file: str) -> dict | None:
    """按整合包文件路径找已经导入过的实例，用于失败后重跑/更新同一个实例。"""
    try:
        wanted = Path(file).resolve()
    except OSError:
        return None
    for meta in list_all():
        source_file = (meta.get("source") or {}).get("file")
        if not source_file:
            continue
        try:
            if Path(source_file).resolve() == wanted:
                return meta
        except OSError:
            continue
    return None


def save(meta: dict) -> None:
    """把实例元数据写回 instance.json。"""
    directory = instance_dir(meta["folder"])
    directory.mkdir(parents=True, exist_ok=True)
    with open(Path(directory, META_FILE), "w", encoding="utf-8") as f:
        json.dump(meta, f, ensure_ascii=False, indent=2)


def trash(folder: str) -> Path:
    """把实例目录移到启动器回收站，而不是直接删除。

    返回移动后的路径。这样即使误点删除，也能在 ``~/.a2CrLauncher/.trash``
    里把整个实例目录拖回去。
    """
    directory = instance_dir(folder)
    if not directory.is_dir():
        raise FileNotFoundError(f"实例目录不存在：{directory}")
    # 只允许操作 instances 下的一级目录，避免 ../ 之类的路径逃逸
    if directory.resolve().parent != Path(INSTANCES_DIR).resolve():
        raise ValueError("实例目录不在 instances 下，拒绝移动")

    TRASH_DIR.mkdir(parents=True, exist_ok=True)
    stamp = datetime.now().strftime("%Y%m%d-%H%M%S")
    target = Path(TRASH_DIR, f"{folder}-{stamp}")
    index = 2
    while target.exists():
        target = Path(TRASH_DIR, f"{folder}-{stamp}-{index}")
        index += 1
    shutil.move(str(directory), str(target))
    return target
