from platform import system, machine

import psutil


def current_platform() -> tuple[str, str]:
    cur_system, cur_arch = system(), machine()

    match cur_system:
        case "Windows":
            cur_system = "windows"
        case "Darwin":
            cur_system = "osx"
        case "Linux":
            cur_system = "linux"
        case _:
            cur_system = "unknown"

    if cur_arch == "AMD64" or cur_arch == "x86_64":
        cur_arch = "x86_64"
    elif cur_arch == "ARM64" or cur_arch == "aarch64":
        cur_arch = "arm64"
    elif cur_arch == "x86" or cur_arch == "i386" or cur_arch == "i686":
        cur_arch = "x86"
    else:
        cur_arch = "unknown"
    return cur_system, cur_arch


def system_info() -> dict:
    """给设置页展示的机器资源，单位 GiB。

    ``disk_usage`` 只统计当前盘符：整合包可能放在别的盘，这里不假装是整机容量。
    """
    memory = psutil.virtual_memory()
    gib = 1024 ** 3
    return {
        "ram": {
            "total": memory.total / gib,
            "used": memory.used / gib,
            "available": memory.available / gib,
        },
        "disk": psutil.disk_usage("/").total / gib,
    }
