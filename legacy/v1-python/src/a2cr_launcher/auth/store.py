"""正版账号的本地存储与自动续期。"""

import json
from datetime import datetime, timedelta
from pathlib import Path

from a2cr_launcher.auth import microsoft
from a2cr_launcher.core.paths import ROOT_DIR

AUTH_FILE = Path(ROOT_DIR, "auth.json")
# 提前 5 分钟续期，避免启动时 token 刚好过期
REFRESH_BUFFER = timedelta(minutes=5)


def load() -> dict | None:
    try:
        with open(AUTH_FILE, "r", encoding="utf-8-sig") as f:
            data = json.load(f)
    except (OSError, json.JSONDecodeError):
        return None
    if not isinstance(data, dict) or not data.get("name"):
        return None
    return data


def save(account: dict) -> None:
    AUTH_FILE.parent.mkdir(parents=True, exist_ok=True)
    temp = AUTH_FILE.with_name(AUTH_FILE.name + ".tmp")
    with open(temp, "w", encoding="utf-8") as f:
        json.dump(account, f, ensure_ascii=False, indent=2)
    temp.replace(AUTH_FILE)


def clear() -> None:
    try:
        AUTH_FILE.unlink()
    except OSError:
        pass


def get_valid(log=print) -> dict | None:
    """读取账号；快过期时自动用 refresh token 续期。失败返回 None。"""
    account = load()
    if account is None:
        return None

    try:
        expires_at = datetime.fromisoformat(account["expires_at"])
    except (KeyError, ValueError):
        expires_at = datetime.min

    if expires_at - datetime.now() > REFRESH_BUFFER:
        return account

    try:
        refreshed = microsoft.refresh_account(account.get("refresh_token", ""))
    except microsoft.AuthError as err:
        log(f"正版登录已失效：{err}")
        return None

    save(refreshed)
    return refreshed
