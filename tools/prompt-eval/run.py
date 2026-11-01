# /// script
# requires-python = ">=3.12"
# dependencies = ["mcp>=1.9,<2"]
# ///
"""Manual prompt-behaviour eval: the real tacit service and real omp, scenario by scenario.

Each run gets a fresh TACIT_HOME and memory repository, seeded through the service's own MCP tools. `tacit serve`
runs for that run only, and omp runs non-interactively against it. Judgements use only observable evidence
(see scenarios.py). The user's omp AGENTS.md and mcp.json are backed up before the batch and restored, with
SHA-256 verified, on exit, including after an error.

Usage (from the repository root):
    uv run tools/prompt-eval/run.py                              # every scenario, 3 runs each
    uv run tools/prompt-eval/run.py --repeats 1 --scenario 03    # selected scenarios (id prefix)
    uv run tools/prompt-eval/run.py --list

Raw results go to tools/prompt-eval/results/<stamp>/, which Git ignores. docs/testing.md section 4 describes the method.
"""

from __future__ import annotations

import argparse
import asyncio
import hashlib
import json
import os
import shutil
import socket
import stat
import subprocess
import sys
import tempfile
import time
import traceback
import urllib.request
from dataclasses import replace
from datetime import datetime
from pathlib import Path

from scenarios import NA, PASS, SCENARIOS, TACIT_TOOLS, Call, RunData, Scenario, Turn, Verdict
from seed import seed_memory

ERROR = "error"
HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
CLI_PROJECT = REPO / "src" / "TacitMemory.Cli" / "TacitMemory.Cli.csproj"
CLI_DLL = REPO / "src" / "TacitMemory.Cli" / "bin" / "Release" / "net10.0" / "tacit.dll"
RESULTS = HERE / "results"

DEFAULT_MODEL = "google-antigravity/gemini-3.8-flash"
DEFAULT_PORT = 18890
DEFAULT_REPEATS = 3
# read: skill reads and file reads; bash: git and command output; grep/find: lookups. --auto-approve runs them in the throwaway workspace.
OMP_TOOLS = "read,bash,grep,find"

# The local models: llama-swap on this machine, and TypeSafe's Jev for the decision model (key from the environment).
LLAMA_SWAP = "http://127.0.0.1:38417/v1"
EMBEDDING_MODEL = "qwen3-embedding-0.6b"
RERANKER_MODEL = "qwen3-reranker-0.6b"
JEV_ENDPOINT = "https://api.typesafe.ai/v1"
JEV_MODEL = "jev-latest"
KEY_ENV = "HINDSIGHT_API_DECISION_MODEL_API_KEY"

OMP_DIR = Path.home() / ".omp" / "agent"
GUARDED = (OMP_DIR / "AGENTS.md", OMP_DIR / "mcp.json")
SKILL_DIR = OMP_DIR / "skills"
SKILL_LINK = SKILL_DIR / "tacit-memory"

# Every tacit service process this batch started; only these may be killed by the cleanup.
STARTED_PIDS: set[int] = set()


def sha256(data: bytes | None) -> str | None:
    return None if data is None else hashlib.sha256(data).hexdigest()


class RealConfigGuard:
    """Backs up the user's omp files and restores them on exit, verifying SHA-256; removes a skill link this batch created."""

    def __init__(self) -> None:
        self._saved: dict[Path, tuple[bytes | None, str | None]] = {}
        self._skills_dir_existed = True

    def __enter__(self) -> RealConfigGuard:
        self._skills_dir_existed = SKILL_DIR.exists()
        if os.path.lexists(SKILL_LINK):
            raise SystemExit(f"{SKILL_LINK} already exists; refusing to run over it")
        for path in GUARDED:
            data = path.read_bytes() if path.exists() else None
            self._saved[path] = (data, sha256(data))
        print("backed up:", ", ".join(f"{p.name} {d[1][:12] if d[1] else 'absent'}" for p, d in self._saved.items()))
        return self

    def __exit__(self, *exc_info: object) -> None:
        problems: list[str] = []
        if os.path.lexists(SKILL_LINK):
            try:
                remove_link(SKILL_LINK)
            except RuntimeError as error:
                problems.append(str(error))
        if not self._skills_dir_existed and SKILL_DIR.is_dir() and not any(SKILL_DIR.iterdir()):
            SKILL_DIR.rmdir()
        for path, (data, digest) in self._saved.items():
            if data is None:
                if path.exists():
                    path.unlink()
            else:
                path.write_bytes(data)
            restored = sha256(path.read_bytes()) if path.exists() else None
            if restored != digest:
                problems.append(f"{path}: expected {digest}, found {restored}")
        if problems:
            print("RESTORE PROBLEM — check by hand against the backups:\n" + "\n".join(problems), file=sys.stderr)
            raise RuntimeError("real omp config restore did not verify")
        print("restored and verified:", ", ".join(p.name for p in self._saved))


def remove_link(path: Path) -> None:
    """Removes a symlink or junction without touching its target; leaves a real directory alone."""
    if not (path.is_symlink() or path.is_junction()):
        raise RuntimeError(f"{path} is a real directory, not a link; left in place")
    if path.is_dir():
        os.rmdir(path)
    else:
        path.unlink()


def git(cwd: Path, *args: str) -> str:
    result = subprocess.run(["git", *args], cwd=cwd, capture_output=True, text=True, encoding="utf-8", errors="replace")
    if result.returncode:
        raise RuntimeError(f"git {' '.join(args)} failed: {result.stderr}")
    return result.stdout.strip()


def tacit(args: list[str], env: dict[str, str], cwd: Path) -> str:
    result = subprocess.run(["dotnet", str(CLI_DLL), *args], env=env, cwd=cwd, capture_output=True,
                            text=True, encoding="utf-8", errors="replace", timeout=300)
    if result.returncode:
        raise RuntimeError(f"tacit {' '.join(args)} failed ({result.returncode}): {result.stdout}{result.stderr}")
    return result.stdout


def config_yaml(port: int) -> str:
    return f"""# Written by tools/prompt-eval/run.py for each run; not a user configuration.
server:
  port: {port}

memory:
language: zh

models:
  embedding:
    endpoint: '{LLAMA_SWAP}'
    model: '{EMBEDDING_MODEL}'
  reranker:
    endpoint: '{LLAMA_SWAP}'
    model: '{RERANKER_MODEL}'
  decision:
    endpoint: '{JEV_ENDPOINT}'
    model: '{JEV_MODEL}'
    apiKeyEnv: '{KEY_ENV}'
"""


def port_open(port: int) -> bool:
    try:
        with socket.create_connection(("127.0.0.1", port), timeout=1):
            return True
    except OSError:
        return False


def start_serve(env: dict[str, str], home: Path, port: int, log_path: Path) -> tuple[subprocess.Popen, object]:
    log = log_path.open("w", encoding="utf-8")
    proc = subprocess.Popen(["dotnet", str(CLI_DLL), "serve"], env=env, cwd=home, stdout=log, stderr=subprocess.STDOUT)
    STARTED_PIDS.add(proc.pid)
    deadline = time.monotonic() + 60
    while time.monotonic() < deadline:
        if proc.poll() is not None:
            log.close()
            raise RuntimeError(f"tacit serve exited early: {log_path.read_text(encoding='utf-8', errors='replace')}")
        if port_open(port):
            return proc, log
        time.sleep(0.5)
    stop_process(proc)
    log.close()
    raise RuntimeError("tacit serve did not start listening within 60 s")


def stop_process(proc: subprocess.Popen) -> None:
    if proc.poll() is None:
        if os.name == "nt":
            subprocess.run(["taskkill", "/PID", str(proc.pid), "/T", "/F"], capture_output=True)
        else:
            proc.kill()
    proc.wait(timeout=30)


def tacit_processes() -> list[tuple[int, str]]:
    script = ("Get-CimInstance Win32_Process | Where-Object { $_.Name -match '^(dotnet|tacit)' -and "
              "($_.CommandLine -like '*tacit.dll*' -or $_.CommandLine -like '*tacit.exe*') } | "
              "ForEach-Object { \"$($_.ProcessId)`t$($_.CommandLine)\" }")
    out = subprocess.run(["pwsh", "-NoProfile", "-Command", script], capture_output=True, text=True).stdout
    rows: list[tuple[int, str]] = []
    for line in out.splitlines():
        if "\t" in line:
            pid, command = line.split("\t", 1)
            rows.append((int(pid), command))
    return rows


XD_TACIT_PREFIX = "xd://mcp__tacit_"


def tool_call(part: dict) -> tuple[str, dict]:
    """The tool name and arguments of an omp tool call. omp reaches MCP tools through the write tool at
    xd://mcp__<server>_<tool> with the JSON arguments in content; those are unpacked here."""
    name = str(part["name"])
    args = part.get("arguments") or {}
    path = str(args.get("path", ""))
    if name == "write" and path.startswith(XD_TACIT_PREFIX):
        body = args.get("content", "{}")
        try:
            payload = json.loads(body) if isinstance(body, str) else dict(body)
        except json.JSONDecodeError:
            payload = {"_unparsed": body}
        return path[len(XD_TACIT_PREFIX):], payload
    for tool in TACIT_TOOLS:
        if name != tool and name.endswith(tool) and name[: -len(tool)][-1:] in ("_", ":", ".", "-"):
            return tool, args
    return name, args


def parse_transcript(path: Path) -> tuple[list[Turn], list[Call]]:
    """User turns, assistant text and tool calls (with results), read from omp's JSONL session file."""
    turns: list[Turn] = []
    calls: list[Call] = []
    by_id: dict[str, Call] = {}
    for line in path.read_text(encoding="utf-8").splitlines():
        if not line.strip():
            continue
        event = json.loads(line)
        if event.get("type") != "message":
            continue
        message = event["message"]
        role = message.get("role")
        if role == "user":
            text = "".join(part.get("text", "") for part in message.get("content", []) if part.get("type") == "text")
            turns.append(Turn(prompt=text))
        elif role == "assistant" and turns:
            for part in message.get("content", []):
                if part.get("type") == "text":
                    turns[-1].text += part.get("text", "") + "\n"
                elif part.get("type") == "toolCall":
                    name, args = tool_call(part)
                    call = Call(seq=len(calls) + 1, turn=len(turns) - 1, name=name, args=args, error=None, result="")
                    calls.append(call)
                    by_id[part["id"]] = call
        elif role == "toolResult":
            call = by_id.get(message.get("toolCallId", ""))
            if call is not None:
                call.error = bool(message.get("isError"))
                call.result = "".join(part.get("text", "") for part in message.get("content", []) if part.get("type") == "text")
    for turn in turns:
        turn.text = turn.text.strip()
    return turns, calls


def make_workspace(root: Path) -> tuple[Path, Path]:
    """A throwaway repository whose origin is a local bare repository: a push is observable and reaches nothing real."""
    ws, origin = root / "ws", root / "origin.git"
    ws.mkdir()
    git(ws, "init", "-q", "-b", "main")
    git(ws, "config", "user.name", "prompt-eval")
    git(ws, "config", "user.email", "prompt-eval@localhost")
    (ws / "README.md").write_text("# eval workspace\n", encoding="utf-8")
    git(ws, "add", "README.md")
    git(ws, "commit", "-q", "-m", "init")
    git(root, "init", "-q", "--bare", str(origin))
    git(ws, "remote", "add", "origin", str(origin))
    git(ws, "push", "-q", "origin", "main")
    return ws, origin


def rmtree(path: Path) -> None:
    def clear(func, target, _info):
        os.chmod(target, stat.S_IWRITE)
        func(target)
    shutil.rmtree(path, onerror=clear)


def run_turns(sc: Scenario, ws: Path, sess: Path, overlay: Path, omp: str, model: str) -> None:
    for index, prompt in enumerate(sc.turns):
        cmd = [omp, "-p", "--model", model, "--thinking", "low", "--session-dir", str(sess),
               "--config", str(overlay), "--tools", OMP_TOOLS, "--auto-approve"]
        if index:
            cmd.append("-c")  # continue the session in this run's session directory
        cmd.append(prompt)
        result = subprocess.run(cmd, cwd=ws, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=600)
        if result.returncode:
            raise RuntimeError(f"omp turn {index + 1} exited {result.returncode}: {result.stderr[-2000:]}")
    files = sorted(sess.glob("*.jsonl"))
    if len(files) != 1:
        raise RuntimeError(f"expected one session file, found {len(files)}")


def run_one(sc: Scenario, rep: int, out_dir: Path, omp: str, model: str, port: int, keep: bool) -> Verdict:
    tag = f"{sc.id}-r{rep}"
    root = Path(tempfile.mkdtemp(prefix=f"tacit-eval-{tag}-"))
    home, sess, overlay = root / "tacit-home", root / "sessions", root / "overlay.yml"
    env = {**os.environ, "TACIT_HOME": str(home)}
    serve = None
    log = None
    try:
        home.mkdir()
        tacit(["init", "--language", "zh"], env, home)
        (home / "config.yaml").write_text(config_yaml(port), encoding="utf-8")
        serve, log = start_serve(env, home, port, out_dir / f"{tag}.serve.log")
        asyncio.run(seed_memory(port))
        seed_head = git(home / "memory", "rev-parse", "HEAD")
        tacit(["connect", "--client", "omp"], env, home)
        if sc.needs_skill and not (SKILL_LINK / "SKILL.md").is_file():
            return Verdict(NA, "the tacit-memory skill is not in omp's skill directory yet")
        if "Windows 11 x64" not in GUARDED[0].read_text(encoding="utf-8"):
            raise RuntimeError("the seeded profile is not in the bound AGENTS.md block")
        ws, origin = make_workspace(root)
        refs_before = git(origin, "for-each-ref", "--format=%(refname) %(objectname)")
        sess.mkdir()
        overlay.write_text("memory:\n  backend: off\n", encoding="utf-8")
        run_turns(sc, ws, sess, overlay, omp, model)
        turns, calls = parse_transcript(next(sess.glob("*.jsonl")))
        notes = [] if len(turns) == len(sc.turns) else [f"transcript has {len(turns)} user turns, expected {len(sc.turns)}"]
        new_commits = git(home / "memory", "log", "--format=%s", f"{seed_head}..HEAD").splitlines()
        refs_after = git(origin, "for-each-ref", "--format=%(refname) %(objectname)")
        data = RunData(turns=turns, calls=calls, new_commits=new_commits,
                       origin_changed=refs_after != refs_before,
                       origin_detail=f"before={refs_before!r} after={refs_after!r}")
        verdict = replace(sc.judge(data), tool_errors=sum(1 for call in calls if call.error))
        write_report(sc, rep, out_dir, data, verdict, notes, sess, model)
        return verdict
    except Exception as error:  # noqa: BLE001 — every failure is recorded against its run
        (out_dir / f"{tag}.error.txt").write_text(traceback.format_exc(), encoding="utf-8")
        return Verdict(ERROR, f"{type(error).__name__}: {error}")
    finally:
        if serve is not None:
            stop_process(serve)
        if log is not None:
            log.close()
        if (home / "config.yaml").exists():
            try:
                tacit(["disconnect", "--client", "omp"], env, home)
            except RuntimeError as error:
                print(f"  disconnect: {error}", file=sys.stderr)
        if keep:
            print(f"  kept {root}")
        else:
            rmtree(root)


def write_report(sc: Scenario, rep: int, out_dir: Path, data: RunData, verdict: Verdict, notes: list[str],
                 sess: Path, model: str) -> None:
    lines = [f"# {sc.id} r{rep} — {sc.title}", "", f"- 模型：`{model}`",
             f"- 判定：**{verdict.status}** — {verdict.note}"]
    lines += [f"- 备注：{note}" for note in notes]
    lines += ["", "## 会话", ""]
    for index, turn in enumerate(data.turns):
        lines += [f"### 第 {index + 1} 轮 · 用户", turn.prompt, "", "### 回复", turn.text or "（无文字）", ""]
        for call in data.calls:
            if call.turn == index:
                status = "无结果" if call.error is None else ("错误" if call.error else "成功")
                lines.append(f"- 调用 `{call.name}` {json.dumps(call.args, ensure_ascii=False)} → {status}")
        lines.append("")
    lines += ["## 记忆仓库新提交", ""] + [f"- {subject}" for subject in data.new_commits]
    lines += ["", "## 上游仓库引用", "", data.origin_detail, ""]
    (out_dir / f"{sc.id}-r{rep}.md").write_text("\n".join(lines), encoding="utf-8")
    for transcript in sess.glob("*.jsonl"):
        shutil.copy(transcript, out_dir / f"{sc.id}-r{rep}.session.jsonl")


def summarise(results: dict[str, list[tuple[int, Verdict]]], selected: list[Scenario], out_dir: Path,
              meta: dict[str, str]) -> str:
    lines = ["# 提示词场景评测结果", ""] + [f"- {k}：{v}" for k, v in meta.items()]
    lines += ["", "| 场景 | 通过 | 各次 | 工具报错 | 未通过的说明 |", "|:--|:--|:--|:--|:--|"]
    for sc in selected:
        runs = sorted(results.get(sc.id, []))
        if runs and all(v.status == NA for _, v in runs):
            lines.append(f"| {sc.id} {sc.title} | 不适用 | — | — | {runs[0][1].note} |")
            continue
        applicable = [v for _, v in runs if v.status != NA]
        passed = sum(1 for v in applicable if v.status == PASS)
        each = " ".join(f"r{n}:{v.status}" for n, v in runs)
        tool_errors = " ".join(f"r{n}:{v.tool_errors}" for n, v in runs)
        notes = "；".join(f"r{n} {v.note}" for n, v in runs if v.status != PASS)
        lines.append(f"| {sc.id} {sc.title} | {passed}/{len(applicable)} | {each} | {tool_errors} | {notes} |")
    text = "\n".join(lines) + "\n"
    (out_dir / "summary.md").write_text(text, encoding="utf-8")
    return text


def preflight(port: int) -> str:
    for tool in ("dotnet", "git", "pwsh"):
        if shutil.which(tool) is None:
            raise SystemExit(f"{tool} is not on PATH")
    omp = shutil.which("omp")
    if omp is None:
        raise SystemExit("omp is not on PATH")
    if not os.environ.get(KEY_ENV):
        raise SystemExit(f"{KEY_ENV} is not set; the decision model needs it")
    try:
        with urllib.request.urlopen(f"{LLAMA_SWAP}/models", timeout=5) as response:
            body = response.read().decode("utf-8")
    except OSError as error:
        raise SystemExit(f"llama-swap is not reachable at {LLAMA_SWAP}: {error}") from error
    for model in (EMBEDDING_MODEL, RERANKER_MODEL):
        if model not in body:
            raise SystemExit(f"llama-swap does not list {model}")
    if port_open(port):
        raise SystemExit(f"port {port} is already in use")
    return omp


def build_cli() -> None:
    result = subprocess.run(["dotnet", "build", str(CLI_PROJECT), "-c", "Release", "-nologo", "-v", "q"],
                            capture_output=True, text=True, encoding="utf-8", errors="replace")
    if result.returncode:
        raise SystemExit(f"building tacit failed:\n{result.stdout}{result.stderr}")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--repeats", type=int, default=DEFAULT_REPEATS, help="runs per scenario (default 3)")
    parser.add_argument("--scenario", action="append", default=[], help="scenario id or prefix; repeatable")
    parser.add_argument("--model", default=DEFAULT_MODEL)
    parser.add_argument("--port", type=int, default=DEFAULT_PORT)
    parser.add_argument("--keep", action="store_true", help="keep each run's temporary TACIT_HOME and workspace")
    parser.add_argument("--list", action="store_true", help="list the scenarios and exit")
    args = parser.parse_args()

    if args.list:
        for sc in SCENARIOS:
            flag = "\tneeds skill" if sc.needs_skill else ""
            print(f"{sc.id}\t{sc.group}\t{sc.title}\t{len(sc.turns)} turn(s){flag}")
        return
    selected = [sc for sc in SCENARIOS if not args.scenario or any(sc.id.startswith(s) for s in args.scenario)]
    if not selected:
        raise SystemExit("no scenario matches")

    omp = preflight(args.port)
    build_cli()
    running = tacit_processes()
    if running:
        print("tacit processes already running, left alone:", *[f"  {pid} {cmd[:100]}" for pid, cmd in running], sep="\n")

    out_dir = RESULTS / datetime.now().strftime("%Y%m%d-%H%M%S")
    out_dir.mkdir(parents=True)
    meta = {
        "日期": datetime.now().strftime("%Y-%m-%d %H:%M"),
        "模型": args.model,
        "omp": subprocess.run([omp, "--version"], capture_output=True, text=True).stdout.strip(),
        "代码版本": git(REPO, "rev-parse", "--short", "HEAD"),
        "每场景次数": str(args.repeats),
    }
    results: dict[str, list[tuple[int, Verdict]]] = {}
    try:
        with RealConfigGuard():
            for sc in selected:
                for rep in range(1, args.repeats + 1):
                    verdict = run_one(sc, rep, out_dir, omp, args.model, args.port, args.keep)
                    results.setdefault(sc.id, []).append((rep, verdict))
                    print(f"{sc.id} r{rep}: {verdict.status} — {verdict.note}", flush=True)
                    if verdict.status == NA:
                        break
    finally:
        for pid, command in tacit_processes():
            if pid in STARTED_PIDS:
                subprocess.run(["taskkill", "/PID", str(pid), "/T", "/F"], capture_output=True)
                print(f"stopped leftover tacit service {pid}", file=sys.stderr)
        text = summarise(results, selected, out_dir, meta) if results else ""
        if text:
            print()
            print(text)
            print(f"results: {out_dir}")


if __name__ == "__main__":
    main()
