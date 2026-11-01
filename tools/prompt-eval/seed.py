"""Seed data for the prompt eval: written through the real service's MCP tools, so every file, index and commit is what the service produces."""

from __future__ import annotations

import re

# The profile is the prototype's block (prototypes/prompt-eval/block.md), so results stay comparable.
# Each entry is (op, persona_section, text). The source comment marks seed data, not something the user said.
PROFILE: list[tuple[str, str, str]] = [
    ("add", "red_lines", "不要自行发布版本或推送到远端仓库；改为先列出要推送的内容，请用户确认（理由：影响外部，无法撤回）"),
    ("add", "working_rules", "需求不清时 → 先连续追问，确认后再给出完整方案"),
    ("add", "working_rules", "写脚本或终端命令时 → 使用 PowerShell 7（pwsh）语法"),
    ("add", "preferences", "沟通：先要结论，再看依据；反感客套和铺垫"),
    ("add", "preferences", "语言：用中文交流，代码注释用英文"),
    ("add", "profile", "系统：Windows 11 x64，Shell 为 PowerShell 7"),
    ("add", "profile", "技术栈：.NET 10、C#"),
]

PAGE = {
    "page": "my-app/db-upgrade",
    "title": "数据库升级后扩展丢失",
    "aliases": ["全文检索插件失效", "升级后搜索不到中文"],
    "sections": [
        {"name": "现象", "body": "PostgreSQL 从 16 升到 17 后，中文全文搜索全部返回空；英文搜索正常。"},
        {"name": "根因", "body": "升级用的是新数据目录，PGroonga 扩展没有随之安装，索引被静默降级为普通 btree。"},
        {"name": "解法", "body": "在新实例上安装 PGroonga 4.0 后执行 `REINDEX INDEX CONCURRENTLY`。试过只重启服务，无效。"},
    ],
}

MAINLINE = [
    "TacitMemory 设计：从 Hindsight 转向自建，确定三个辅助模型配合与定期体检",
    "NSSM：托管本机常驻服务，已稳定运行",
]

ACTIVITIES = [
    ("决策模型定为只做判断，整理交给体检", "tacit-memory"),
    ("完成设计文档一致性审查，修了 16 处问题", "tacit-memory"),
    ("验证 omp 能连上无状态 MCP 服务", "tacit-memory"),
]

_VERSION = re.compile(r"hash set to ([^,\s]+)")


class SeedError(RuntimeError):
    pass


async def seed_memory(port: int) -> None:
    """Writes the seed through the running service. Any refusal aborts the run."""
    from mcp import ClientSession
    from mcp.client.streamable_http import streamablehttp_client

    async with streamablehttp_client(f"http://127.0.0.1:{port}/mcp") as (read, write, _):
        async with ClientSession(read, write) as session:
            await session.initialize()

            async def call(name: str, arguments: dict) -> tuple[bool, str]:
                result = await session.call_tool(name, arguments)
                text = "\n".join(block.text for block in result.content if getattr(block, "text", None) is not None)
                return bool(result.isError), text

            async def must(name: str, arguments: dict) -> None:
                error, text = await call(name, arguments)
                if error:
                    raise SeedError(f"{name} {arguments.get('op', '')} refused: {text}")

            for op, section, text in PROFILE:
                await must("write_persona", {"op": op, "persona_section": section, "text": text, "source": "（种子数据）"})
            await must("write_page", {"op": "create", **PAGE})

            # The first call without a hash is refused and names the current version code; the retry uses it.
            error, text = await call("write_recent", {"op": "update_mainline", "items": MAINLINE})
            if error:
                match = _VERSION.search(text)
                if match is None:
                    raise SeedError(f"update_mainline refused without a version code: {text}")
                await must("write_recent", {"op": "update_mainline", "items": MAINLINE, "hash": match.group(1)})
            for activity, project in ACTIVITIES:
                await must("write_recent", {"op": "add_activity", "text": activity, "project": project})
