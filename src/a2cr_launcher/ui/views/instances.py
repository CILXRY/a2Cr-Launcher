"""实例页：卡片网格、实例详情、更新和删除。"""

from __future__ import annotations

import asyncio

import flet as ft

from ... import services
from ...ui import components as ui
from ...ui.dialogs import show_confirm
from ...ui.theme import loader_meta
from ...ui.utils import format_relative, loader_display, shorten_path


def build(app) -> ft.Control:
    p = app.p
    import_button = ui.primary_button(
        "导入整合包", lambda e: app.page.run_task(app.show_import_dialog),
        icon=ft.Icons.ADD_ROUNDED, height=40,
    )
    create_button = ui.tonal_button(
        "新建实例", lambda e: show_create_dialog(app),
        icon=ft.Icons.ADD_CIRCLE_OUTLINE_ROUNDED, height=40,
    )
    refresh_button = ui.icon_action(
        ft.Icons.REFRESH_ROUNDED, "刷新实例",
        lambda e: app.page.run_task(app.refresh_instances),
    )
    header = ft.Row(
        controls=[
            ft.Column(
                controls=[
                    ft.Text("我的实例", size=26, weight=ft.FontWeight.W_800, color=p.text),
                    ft.Text("整合包、加载器和存档都在这里", size=12.5, color=p.text_muted),
                ],
                spacing=4,
                expand=True,
            ),
            refresh_button,
            create_button,
            import_button,
        ],
        vertical_alignment=ft.CrossAxisAlignment.CENTER,
    )

    if app.instances_loading:
        body: ft.Control = ft.Container(
            content=ft.Column(
                controls=[
                    ft.ProgressRing(width=28, height=28, stroke_width=2, color=p.accent),
                    ft.Text("正在读取实例…", size=12.5, color=p.text_muted),
                ],
                spacing=12,
                horizontal_alignment=ft.CrossAxisAlignment.CENTER,
            ),
            alignment=ft.Alignment.CENTER,
            height=240,
        )
    elif not app.instances:
        body = ui.empty_state(
            ft.Icons.VIEW_IN_AR_OUTLINED,
            "还没有整合包实例",
            "支持 Modrinth .mrpack、CurseForge .zip，以及 HMCL / PCL 导出的压缩包。",
            actions=[
                ui.primary_button("新建实例", lambda e: show_create_dialog(app),
                                  icon=ft.Icons.ADD_CIRCLE_OUTLINE_ROUNDED),
                ui.tonal_button("导入整合包",
                                lambda e: app.page.run_task(app.show_import_dialog),
                                icon=ft.Icons.ADD_ROUNDED),
                ui.tonal_button("安装原版", lambda e: app.navigate("versions"),
                                icon=ft.Icons.LAYERS_OUTLINED),
            ],
        )
    else:
        cards = []
        for item in app.instances:
            card = instance_card(app, item)
            card.col = {"sm": 12, "md": 6, "lg": 4}
            cards.append(card)
        body = ft.ResponsiveRow(controls=cards, spacing=14, run_spacing=14)

    return ft.Column(
        controls=[
            header,
            ft.Container(height=4),
            body,
        ],
        spacing=18,
        scroll=ft.ScrollMode.AUTO,
        expand=True,
    )


def instance_card(app, item: dict) -> ft.Container:
    """单个实例卡片。所有动作都在卡片底部，避免点击区域互相抢事件。"""
    p = app.p
    folder = item.get("folder", "")
    name = item.get("name") or folder
    loader = item.get("loader", {}).get("type", item.get("loader_type", "vanilla"))
    loader_version = item.get("loader", {}).get("version", item.get("loader_version", ""))
    label, loader_color = loader_meta(loader)
    adopted = bool(item.get("adopted"))
    broken = bool(item.get("broken"))

    play = ui.primary_button(
        "启动", lambda e: app.launch_instance(folder),
        icon=ft.Icons.PLAY_ARROW_ROUNDED, height=34, width=92, disabled=broken,
    )
    folder_button = ui.icon_action(
        ft.Icons.FOLDER_OPEN_ROUNDED, "打开实例目录",
        lambda e: app.page.run_task(_open_folder, app, folder),
    )
    # 未托管的旧目录没有整合包来源，更新按钮没有意义
    update_button = None if (adopted or broken) else ui.icon_action(
        ft.Icons.SYSTEM_UPDATE_ALT_ROUNDED, "更新整合包",
        lambda e: app.page.run_task(app.show_import_dialog, folder),
    )
    delete_button = ui.icon_action(
        ft.Icons.DELETE_OUTLINE_ROUNDED, "删除实例",
        lambda e: _confirm_delete(app, item), color=p.danger,
    )
    detail_button = ui.icon_action(
        ft.Icons.INFO_OUTLINE_ROUNDED, "实例详情",
        lambda e: show_detail(app, item),
    )

    pills = [
        ui.pill(loader_display(loader, loader_version), color=loader_color, bgcolor=p.surface_hi),
        ui.pill(f"MC {item.get('mc_version', '—')}", color=p.text_muted, bgcolor=p.surface_hi),
    ]
    if adopted:
        pills.append(ui.pill("未托管", color=p.text_muted, bgcolor=p.surface_hi))
    if broken:
        pills.append(ui.pill("元数据损坏", color=p.danger, bgcolor=p.surface_hi))

    meta = ft.Row(controls=pills, spacing=6, wrap=True)
    body = ft.Column(
        controls=[
            ui.cover_art(name, loader, height=94),
            ft.Column(
                controls=[
                    ft.Text(name, size=15, weight=ft.FontWeight.W_700, color=p.text,
                            max_lines=1, overflow=ft.TextOverflow.ELLIPSIS),
                    meta,
                    ft.Row(
                        controls=[
                            ft.Icon(ft.Icons.SCHEDULE_ROUNDED, size=12, color=p.text_faint),
                            ft.Text(format_relative(item.get("last_played")),
                                    size=11, color=p.text_faint),
                            ft.Text("·", size=11, color=p.text_faint),
                            ft.Text(f"{item.get('mods_count', 0)} 个 mod", size=11,
                                    color=p.text_faint),
                        ],
                        spacing=4,
                    ),
                ],
                spacing=8,
            ),
            ft.Container(height=1, bgcolor=p.border),
            ft.Row(
                controls=[control for control in (
                    play,
                    ft.Container(expand=True),
                    detail_button,
                    folder_button,
                    update_button,
                    delete_button,
                ) if control is not None],
                spacing=2,
                vertical_alignment=ft.CrossAxisAlignment.CENTER,
            ),        ],
        spacing=10,
    )
    card = ui.card(body, padding=10, hover=True)
    card.tooltip = shorten_path(item.get("path", ""), 60)
    return card


async def _open_folder(app, folder: str) -> None:
    try:
        await asyncio.to_thread(services.open_instance_folder, folder)
    except Exception as exc:
        app.show_toast(f"打开目录失败：{exc}", error=True)


def _confirm_delete(app, item: dict) -> None:
    folder = item.get("folder", "")
    name = item.get("name") or folder
    show_confirm(
        app,
        "删除实例",
        f"「{name}」会被移动到启动器回收站，不会立即永久删除。\n"
        f"目录：{item.get('path', '')}",
        lambda: app.page.run_task(app.trash_instance, folder),
        confirm_text="移入回收站",
        danger=True,
    )


def show_detail(app, item: dict) -> None:
    """实例详情对话框。"""
    p = app.p
    folder = item.get("folder", "")
    name = item.get("name") or folder
    loader = item.get("loader", {}).get("type", item.get("loader_type", "vanilla"))
    loader_version = item.get("loader", {}).get("version", item.get("loader_version", ""))
    source = item.get("source") or {}

    source_format = source.get("format", "—")
    source_label = {"adopted": "旧目录（未托管）", "broken": "元数据损坏"}.get(source_format, source_format)

    rows = [
        ("Minecraft", item.get("mc_version", "—")),
        ("启动版本", item.get("version_id", "—")),
        ("加载器", loader_display(loader, loader_version)),
        ("来源", source_label),
        ("整合包版本", source.get("pack_version") or "未标注"),
        ("最近游玩", format_relative(item.get("last_played"))),
        ("目录", item.get("path", "")),
    ]
    info = []
    for key, value in rows:
        info.append(ft.Row(
            controls=[
                ft.Text(key, size=12, color=p.text_muted, width=88),
                ft.Text(str(value), size=12, color=p.text, selectable=True, expand=True),
            ],
            vertical_alignment=ft.CrossAxisAlignment.START,
        ))

    dialog = ft.AlertDialog(
        modal=True,
        title=ft.Row(
            controls=[
                ui.cover_art(name, loader, height=56, radius=12, width=84, show_loader=False),
                ft.Column(
                    controls=[
                        ft.Text(name, size=17, weight=ft.FontWeight.W_700, color=p.text),
                        ft.Text(f"{item.get('mods_count', 0)} 个 mod", size=11, color=p.text_muted),
                    ],
                    spacing=3,
                ),
            ],
            spacing=12,
            vertical_alignment=ft.CrossAxisAlignment.CENTER,
        ),
        content=ft.Container(
            content=ft.Column(info, spacing=9, tight=True),
            width=480,
        ),
        actions=[
            ft.TextButton("关闭", on_click=lambda e: app.page.pop_dialog()),
            ui.ghost_button("删除", lambda e: _delete_from_detail(app, item), color=p.danger),
            ui.tonal_button("打开目录", lambda e: app.page.run_task(_open_folder, app, folder),
                            icon=ft.Icons.FOLDER_OPEN_ROUNDED),
            ui.primary_button("启动", lambda e: _launch_from_detail(app, folder),
                              icon=ft.Icons.PLAY_ARROW_ROUNDED),
        ],
        actions_alignment=ft.MainAxisAlignment.END,
        bgcolor=p.surface,
        shape=ft.RoundedRectangleBorder(radius=20),
    )
    app.page.show_dialog(dialog)


def _launch_from_detail(app, folder: str) -> None:
    app.page.pop_dialog()
    app.launch_instance(folder)


def _delete_from_detail(app, item: dict) -> None:
    app.page.pop_dialog()
    _confirm_delete(app, item)


def show_create_dialog(app) -> None:
    """新建实例：从已安装版本里选一个，起个名字。

    这是"原版也能有实例"的入口；导入整合包走的是另一条路（ImportDialog）。
    """
    p = app.p
    versions = services.list_installed_versions()
    if not versions:
        app.show_toast("还没有安装任何版本，先去「版本」页装一个", error=True)
        return

    options = [ft.DropdownOption(key=item["id"], text=item["id"]) for item in versions]
    default_version = versions[0]["id"]

    name_field = ft.TextField(
        label="实例名称",
        value=default_version,
        hint_text="例如：生存 1.21",
        autofocus=True,
        filled=True,
        bgcolor=p.surface_hi,
        border_color=p.border,
        color=p.text,
    )
    version_select = ft.Dropdown(
        label="Minecraft 版本",
        value=default_version,
        options=options,
        filled=True,
        bgcolor=p.surface_hi,
        border_color=p.border,
        color=p.text,
    )

    async def confirm(e) -> None:
        app.page.pop_dialog()
        version_id = version_select.value
        name = (name_field.value or "").strip() or version_id
        try:
            meta = await asyncio.to_thread(services.create_instance, name, version_id)
        except Exception as exc:
            app.show_toast(f"创建实例失败：{exc}", error=True)
            app.log_bus.push(f"[实例] 创建失败：{type(exc).__name__}: {exc}", "实例", "ERROR")
            return
        app.log_bus.push(f"[实例] 已创建 {meta.get('name')}（{version_id}）", "实例")
        app.show_toast(f"已创建实例 {meta.get('name')}")
        await app.refresh_instances()

    dialog = ft.AlertDialog(
        modal=True,
        title=ft.Text("新建实例", size=18, weight=ft.FontWeight.W_700, color=p.text),
        content=ft.Container(
            content=ft.Column(
                controls=[
                    ft.Text("会创建一个独立的游戏目录，存档和 mod 不会和其他实例混在一起。",
                            size=12, color=p.text_muted),
                    name_field,
                    version_select,
                ],
                spacing=14,
                tight=True,
            ),
            width=420,
        ),
        actions=[
            ft.TextButton("取消", on_click=lambda e: app.page.pop_dialog()),
            ui.primary_button("创建", confirm, icon=ft.Icons.ADD_ROUNDED),
        ],
        actions_alignment=ft.MainAxisAlignment.END,
        bgcolor=p.surface,
        shape=ft.RoundedRectangleBorder(radius=20),
    )
    app.page.show_dialog(dialog)
