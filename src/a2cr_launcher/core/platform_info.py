from platform import system, machine


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
