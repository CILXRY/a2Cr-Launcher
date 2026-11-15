"""微软账号（正版）登录。

使用 Live Connect 的设备代码流程（device code flow）：
  1. 申请设备码，让用户在 https://www.microsoft.com/link 输入
  2. 轮询拿到微软 access token / refresh token
  3. 依次换取 Xbox Live token、XSTS token、Minecraft access token
  4. 拉取 Minecraft 档案（用户名 / UUID）

设备码流程不需要回调地址和客户端密钥，适合命令行启动器。
"""

import time
from datetime import datetime, timedelta

import requests

from a2cr_launcher.core.download import get_session

CLIENT_ID = "00000000402b5328"
SCOPE = "service::user.auth.xboxlive.com::MBI_SSL"

DEVICE_CODE_URL = "https://login.live.com/oauth20_connect.srf"
TOKEN_URL = "https://login.live.com/oauth20_token.srf"
XBOX_AUTH_URL = "https://user.auth.xboxlive.com/user/authenticate"
XSTS_AUTH_URL = "https://xsts.auth.xboxlive.com/xsts/authorize"
MC_LOGIN_URL = "https://api.minecraftservices.com/authentication/login_with_xbox"
MC_PROFILE_URL = "https://api.minecraftservices.com/minecraft/profile"

XSTS_ERRORS = {
    "2148916233": "这个微软账号没有购买 Minecraft（或没有创建 Xbox 档案）",
    "2148916235": "Xbox Live 在你的国家/地区不可用",
    "2148916238": "这是儿童账号，需要先加入家庭组才能登录",
    "2148916227": "这个账号已被 Xbox Live 封禁",
}


class AuthError(RuntimeError):
    """登录过程中的可读错误。"""


def _post_json(url: str, payload: dict, headers: dict | None = None) -> dict:
    response = get_session().post(url, json=payload, headers=headers or {}, timeout=30)
    if response.status_code != 200:
        raise AuthError(f"{url} 返回 {response.status_code}: {response.text[:300]}")
    return response.json()


def _format_uuid(raw: str) -> str:
    if len(raw) != 32:
        return raw
    return f"{raw[0:8]}-{raw[8:12]}-{raw[12:16]}-{raw[16:20]}-{raw[20:32]}"


def request_device_code() -> dict:
    """申请设备码。"""
    response = get_session().post(
        DEVICE_CODE_URL,
        data={"client_id": CLIENT_ID, "scope": SCOPE, "response_type": "device_code"},
        timeout=30,
    )
    if response.status_code != 200:
        raise AuthError(f"申请设备码失败：{response.status_code} {response.text[:300]}")
    return response.json()


def poll_device_token(device: dict, log=print) -> dict:
    """轮询直到用户完成授权，返回微软 token。"""
    interval = int(device.get("interval") or 5)
    deadline = time.monotonic() + int(device.get("expires_in") or 900)

    while time.monotonic() < deadline:
        time.sleep(interval)
        response = get_session().post(
            TOKEN_URL,
            data={
                "client_id": CLIENT_ID,
                "device_code": device["device_code"],
                "grant_type": "urn:ietf:params:oauth:grant-type:device_code",
                "scope": SCOPE,
            },
            timeout=30,
        )
        if response.status_code == 200:
            return response.json()

        try:
            error = response.json()
        except ValueError:
            raise AuthError(f"授权失败：{response.status_code} {response.text[:300]}")

        code = error.get("error", "")
        if code == "authorization_pending":
            continue
        if code == "slow_down":
            interval += 5
            continue
        if code in ("expired_token",):
            raise AuthError("设备码已过期，请重新运行 al login")
        if code in ("authorization_declined", "access_denied"):
            raise AuthError("你取消了对本次登录的授权")
        raise AuthError(f"授权失败：{code} {error.get('error_description', '')}".strip())

    raise AuthError("等待授权超时，请重新运行 al login")


def _xbox_token(ms_access_token: str) -> dict:
    return _post_json(XBOX_AUTH_URL, {
        "Properties": {
            "AuthMethod": "RPS",
            "SiteName": "user.auth.xboxlive.com",
            "RpsTicket": f"d={ms_access_token}",
        },
        "RelyingParty": "http://auth.xboxlive.com",
        "TokenType": "JWT",
    })


def _xsts_token(xbox_token: str) -> dict:
    response = get_session().post(XSTS_AUTH_URL, json={
        "Properties": {"SandboxId": "RETAIL", "UserTokens": [xbox_token]},
        "RelyingParty": "rp://api.minecraftservices.com/",
        "TokenType": "JWT",
    }, timeout=30)
    if response.status_code != 200:
        text = response.text
        for code, message in XSTS_ERRORS.items():
            if code in text:
                raise AuthError(message)
        raise AuthError(f"XSTS 授权失败：{response.status_code} {text[:300]}")
    return response.json()


def _minecraft_token(xsts_token: str, uhs: str) -> dict:
    return _post_json(MC_LOGIN_URL, {
        "identityToken": f"XBL3.0 x={uhs};{xsts_token}",
    })


def _minecraft_profile(access_token: str) -> dict:
    response = get_session().get(MC_PROFILE_URL, headers={
        "Authorization": f"Bearer {access_token}",
    }, timeout=30)
    if response.status_code == 404:
        raise AuthError("这个账号没有 Minecraft Java 版（未购买或未创建档案）")
    if response.status_code != 200:
        raise AuthError(f"获取 Minecraft 档案失败：{response.status_code} {response.text[:300]}")
    return response.json()


def login_with_microsoft_token(ms_token: dict) -> dict:
    """用微软 token 走完 Xbox / XSTS / Minecraft 流程，返回可保存的账号信息。"""
    xbox = _xbox_token(ms_token["access_token"])
    xsts = _xsts_token(xbox["Token"])

    try:
        claims = xsts["DisplayClaims"]["xui"][0]
        uhs = claims["uhs"]
        xuid = claims.get("xid", "")
    except (KeyError, IndexError, TypeError):
        raise AuthError("XSTS 返回里缺少用户信息，登录失败")

    minecraft = _minecraft_token(xsts["Token"], uhs)
    profile = _minecraft_profile(minecraft["access_token"])

    expires_at = datetime.now() + timedelta(seconds=int(minecraft.get("expires_in") or 86400))
    return {
        "name": profile["name"],
        "uuid": _format_uuid(profile["id"]),
        "access_token": minecraft["access_token"],
        "expires_at": expires_at.isoformat(timespec="seconds"),
        "refresh_token": ms_token.get("refresh_token", ""),
        "xuid": xuid,
        "client_id": CLIENT_ID,
    }


def refresh_account(refresh_token: str) -> dict:
    """用 refresh token 重新走一遍登录流程。"""
    if not refresh_token:
        raise AuthError("没有 refresh token，请重新运行 al login")

    response = get_session().post(TOKEN_URL, data={
        "client_id": CLIENT_ID,
        "grant_type": "refresh_token",
        "refresh_token": refresh_token,
        "scope": SCOPE,
    }, timeout=30)
    if response.status_code != 200:
        raise AuthError("登录已过期，请重新运行 al login")

    token = response.json()
    account = login_with_microsoft_token(token)
    if not account.get("refresh_token"):
        account["refresh_token"] = refresh_token
    return account
