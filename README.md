<div align="center">
  <img src="docs/images/logo.png" width="88" alt="TacitMemory" />
  <h1>TacitMemory</h1>
  <p>A personal, white-box memory system shared across AI agent clients and machines—stored as plain Markdown in Git and served over local HTTP MCP on macOS, Windows, and Linux.</p>
  <p>
    <a href="https://github.com/kearril/tacit-memory/releases"><img src="https://img.shields.io/github/v/release/kearril/tacit-memory?include_prereleases&label=version&color=4C566A" alt="Version" /></a>
    <a href="LICENSE"><img src="https://img.shields.io/badge/license-Apache--2.0-5E81AC" alt="License" /></a>
    <img src="https://img.shields.io/badge/.NET-11-512BD4?logo=dotnet&logoColor=white" alt=".NET 11" />
    <img src="https://img.shields.io/badge/Bun-Web_UI-14151a?logo=bun&logoColor=fbf0df" alt="Bun" />
    <img src="https://img.shields.io/badge/MCP-HTTP-2E3440" alt="MCP" />
    <img src="https://img.shields.io/badge/platform-macOS%20%7C%20Windows%20%7C%20Linux-434C5E" alt="Platforms" />
  </p>
  <p>
    <strong>English</strong> · <a href="README.zh-CN.md">简体中文</a>
  </p>
</div>

![TacitMemory Dashboard](docs/images/dashboard.png)

## Why TacitMemory

**Tools change fast. Your memory shouldn't reset every time you switch.**

AI models and agent clients evolve rapidly—Claude Code today, Codex, OpenCode, or Pi tomorrow, and something brand new next month, while tools you once relied on may stop being maintained. Switching clients is rarely painful because of setup; it is painful because you have to re-explain your working rules, preferences, past pitfalls, and ongoing projects to yet another fresh agent starting from zero.

TacitMemory decouples your personal memory from any single client: it stores everything as plain, white-box Markdown files under Git and connects to any agent through just a context file, skills, and a local HTTP MCP endpoint. **Plug in a new client, and pick up right where you left off.**

## Key Features

- **Memory follows you, not a vendor or client**: Relies only on three universal primitives—a context file, skills, and a local HTTP MCP endpoint—working seamlessly across Claude Code, Codex, OpenCode, Pi, DeepSeek Harness, and more, with concurrent multi-agent reads and writes. Sync across multiple machines through your own private Git repository.
- **Zero background generative LLMs—predictable and low-cost**: Understanding, summarizing, and drafting consolidation proposals are all performed by the main-session agent that already holds the full conversation context. The background service never runs hidden LLM rewrites, burns zero extra chat tokens, and never silently mutates or deletes past memories.
- **Markdown + Git white-box storage**: Every memory is human-readable, editable in any text editor, and auto-committed on write. The SQLite full-text and vector index is strictly a derived cache that can be deleted and rebuilt at any time—zero vendor lock-in.
- **Layered memory with progressive disclosure**: Global persona stays resident, domain rules load on trigger, ongoing workstreams condense into structured briefings, and detailed notes are recalled on demand—balancing low token noise with high continuity.
- **Ultra-lightweight and minimal to start**: Runs at ~20 MB resident memory out of the box with no external database or heavy runtime dependencies. The three auxiliary models (embedding, reranker, and judgment) degrade gracefully when omitted or unavailable without blocking writes, though configuring all three is strongly recommended for the best retrieval and consolidation quality.
- **Built-in Web UI & pipeline-matched Recall Trace**: A single process serves memory browsing, a yearly activity heatmap, live editing for all four built-in prompts, and **Recall Trace** running the exact same retrieval pipeline as your agents so you can see why every result ranked where it did.

## Design Trade-offs

These three characteristics are deliberate trade-offs made in exchange for white-box transparency, user control, cross-client portability, and zero background LLM cost:

- **Memory quality depends on the main-session model**: Because the service runs no background generative LLM, when `recall` is called, what gets recorded, and how sharp consolidation proposals are all depend on how well your active agent model follows instructions. In return, you pay zero hidden token overhead and never lose control of your notes.
- **Not a "set-and-forget" black box—expects light human stewardship**: TacitMemory does not hook into clients for silent post-session extraction. You trigger `tacit-remember` before compacting context or wrapping up a task, and spend a minute or two periodically (or when notified by scheduled consolidation checks) confirming a numbered `tacit-consolidate` proposal list in one reply. In return, long-term memory stays free of hallucinated drift.
- **Multi-machine & multi-agent sharing requires scoping awareness**: When several machines and agents share one memory store, mixing machine-specific paths or client-specific quirks into global persona can pollute other environments. TacitMemory isolates these with explicit machine tags (`on <machine>`), scoped layers (global / domain / workstream), and write guidelines, though you still want to keep general habits separate from single-machine or single-client details during writes and consolidation.

## Screenshots

Beyond the dashboard and yearly activity heatmap at the top, the built-in **Recall Trace** runs queries with the exact same parameters as an agent and lets you inspect full-text, vector, RRF fusion, and rerank stages side by side:

![Recall Trace](docs/images/recall-trace.png)

<details>
<summary>Expand more screenshots (Persona, Workstreams, Pages, Activities, Prompts, Settings)</summary>

| Persona | Workstreams |
| :---: | :---: |
| ![Persona](docs/images/memory-persona.png) | ![Workstreams](docs/images/memory-workstreams.png) |

| Pages | Activities |
| :---: | :---: |
| ![Pages](docs/images/memory-pages.png) | ![Activities](docs/images/memory-activities.png) |

| Prompt Editor | Settings |
| :---: | :---: |
| ![Prompt Editor](docs/images/prompts.png) | ![Settings](docs/images/settings.png) |

</details>

## How It Works

### Memory Layers

| Layer | What it holds | How it enters context |
| :--- | :--- | :--- |
| **Persona** | Red lines, working rules, preferences, and profile (scoped globally, by domain, or by workstream) | Global entries and the domain index stay resident; domain rules and workstream rules are fetched on demand |
| **Workstreams** | Ongoing efforts (projects, ops, etc.) with a stable background and recent summary | Resident in the context file (recent summary included for workstreams active in the last 28 days) |
| **Activities** | One-line session outcomes archived monthly, plus weekly and monthly summaries | Last 7 days delivered via MCP server instructions; older entries retrieved on demand |
| **Pages** | Topic-focused Markdown pages (`incident`, `decision`, `entity`, `work`) | Retrieved on demand by the agent via `recall` |

### Workflow

```mermaid
flowchart LR
    Talk[Conversation] -->|Write in-session| Mem[(Memory Repo<br>Markdown + Git)]
    Remember[tacit-remember] -->|Summarize session| Mem
    Mem -->|Consolidation report| Consolidate[tacit-consolidate]
    Consolidate -->|Summaries, workstreams &<br>one-reply proposals| Mem
    Mem -->|Resident block +<br>server instructions| Session[Next Session]
    Mem -->|recall| Session
```

## Prerequisites

- **Git** available on `PATH`.
- **Auxiliary models** (optional, strongly recommended): Supports an embedding model (OpenAI Embeddings), a reranker (Cohere / Jina Rerank), and a judgment model (System One), with built-in provider support for Cloudflare Workers AI as well. All three are optional and degrade gracefully when omitted or unavailable. Default judgment thresholds are calibrated against TypeSafe `jev-latest`; see [`docs/configuration.md`](docs/configuration.md) before switching judgment models.

## Installation

Download the release archive for your platform from [GitHub Releases](https://github.com/kearril/tacit-memory/releases) and extract it into an empty directory:

| Platform | Archive |
| :--- | :--- |
| Windows x64 | `tacit-<version>-win-x64.zip` |
| macOS (Apple Silicon) | `tacit-<version>-osx-arm64.tar.gz` |
| Linux x64 | `tacit-<version>-linux-x64.tar.gz` |

Add that directory to `PATH` or create a symlink:

```bash
mkdir -p <install-dir> && tar -xzf tacit-<version>-osx-arm64.tar.gz -C <install-dir>
ln -s <install-dir>/tacit ~/.local/bin/tacit
```

<details>
<summary>Build from source</summary>

Requires **.NET 11 SDK** and **Bun** (for building the Web UI):

```bash
dotnet publish src/TacitMemory.Cli -c Release -o <install-dir>
```

This produces a framework-dependent build (requires the ASP.NET Core 11 runtime). See [`docs/operations.md`](docs/operations.md) for self-contained builds or skipping the Web UI build.

</details>

## Quick Start

1. **Initialize configuration and the memory repository**

   ```bash
   tacit init
   ```

   Creates `config.yaml` and the memory repository under `~/.tacit`. Interactive terminals prompt for the memory language (`zh` or `en`); in scripts, pass `--language en` or `--language zh` explicitly.

2. **Start the background service**

   ```bash
   tacit serve
   ```

   Runs in the foreground by default (run it in a dedicated terminal or under your OS service manager). Once started, open `http://127.0.0.1:18888/` in your browser to access the Web UI.

3. **Bind a client and connect MCP**

   Open **Settings → Bindings** in the Web UI, pick a built-in client preset (Claude Code, Codex, OpenCode, Pi, and more) for one-click binding, and add the displayed local HTTP MCP endpoint (`http://127.0.0.1:18888/mcp`) to your client's MCP configuration.

   Prefer the CLI? Run:

   ```bash
   tacit connect --client my-agent --context ~/.my-agent/AGENTS.md --skills ~/.my-agent/skills
   ```

   Binding writes the resident memory block into your context file and links the `tacit-memory`, `tacit-remember`, and `tacit-consolidate` skills into the skills directory. Start a new client session for it to take effect.

4. **Configure the three auxiliary models** (optional, strongly recommended)

   In the Web UI, open **Settings → Models**, enter the endpoints and API keys for the embedding, reranker, and judgment models, save, and click **Probe** in the card header to verify connectivity.

<details>
<summary>Access the Web UI from another device</summary>

1. On the host machine, open the Web UI and go to **Settings → Remote access** to set an access password.
2. In the same section, add **Extra listen addresses** (for example, a LAN/VPN IP or `0.0.0.0`), save, and restart `tacit serve`.
3. From another device, open `http://<address>:<port>/` and sign in with the password.

The `/mcp` endpoint always accepts loopback connections only. The service does not terminate TLS—passwords and keys travel in plaintext, so only expose extra listeners on a trusted network (such as an encrypted VPN).

</details>

## Daily Usage

- **Automatic injection & on-demand recall**: Every new session loads the resident block and MCP server instructions automatically; during a conversation, the agent reads via `recall` and writes via `write_*` (keep `tacit serve` running).
- **Workstream briefings**: When entering an ongoing workstream, the agent calls `recall` with only `workstream` to load its rules, open items, page directory, and recent activity in one shot.
- **Manual capture (`tacit-remember`)**: Trigger `tacit-remember` before compacting context or ending a session to summarize and store outcomes immediately.
- **Periodic consolidation & scheduled reminders (`tacit-consolidate`)**: When notified that consolidation is due, run `tacit-consolidate`—the agent auto-fixes formatting, cleans redundant candidates, writes weekly/monthly summaries, updates workstream recents, and presents decisions as a numbered list for one-reply batch approval. The service also exposes a boolean signal via `tacit consolidation` and `GET /api/consolidation` (with a one-click copyable setup prompt on the dashboard) for clients that support scheduled checks.
- **Direct editing**: Edit any Markdown file under `~/.tacit/memory` in your own editor; the service watches for changes and reindexes automatically.
- **Syncing skills after upgrades**: When built-in skills are added or updated, re-run `tacit connect` with the same flags to refresh links.

## Documentation

> Note: Detailed reference docs under `docs/` are currently written in Simplified Chinese.

- [`docs/design.md`](docs/design.md) — System behavior, memory rules, Web UI, and security boundaries
- [`docs/decisions.md`](docs/decisions.md) — Architectural decisions, rationale, and rejected alternatives
- [`docs/configuration.md`](docs/configuration.md) — Environment variables, `config.yaml`, and prompt customization
- [`docs/memory-files.md`](docs/memory-files.md) — Memory repository layout and Markdown file formats
- [`docs/operations.md`](docs/operations.md) — Backup/restore, Git sync, troubleshooting, and release packaging
- [`docs/development.md`](docs/development.md) — Codebase map, testing seams, and local builds

## License

Released under the [Apache License 2.0](LICENSE). Third-party notices bundled in release archives are listed in `THIRD-PARTY-NOTICES.txt`.
