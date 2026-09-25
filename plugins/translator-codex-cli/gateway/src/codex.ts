import { spawn, type ChildProcess } from "node:child_process";
import { mkdir, readFile, writeFile } from "node:fs/promises";
import { join, resolve } from "node:path";
import { JsonLines } from "./jsonl.js";
import type { Task, Result, Model } from "./protocol.js";

export interface Runtime {
  node: string;
  codex: string;
  directory: string;
  codexHome: string;
  timeoutMs?: number;
}

function launch(runtime: Runtime, args: string[], cwd = runtime.directory): ChildProcess {
  return spawn(runtime.node, [runtime.codex, ...args], {
    shell: false, windowsHide: true, cwd, stdio: ["pipe", "pipe", "pipe"],
    env: { ...process.env, CODEX_HOME: runtime.codexHome },
  });
}

function stop(child: ChildProcess): void {
  if (!child.pid || child.exitCode !== null) return;
  if (process.platform === "win32") {
    const killer = spawn(join(process.env.SystemRoot ?? "C:\\Windows", "System32", "taskkill.exe"), ["/PID", String(child.pid), "/T", "/F"], { shell: false, windowsHide: true, stdio: "ignore" });
    killer.on("error", () => child.kill());
    killer.on("exit", code => { if (code !== 0 && child.exitCode === null) child.kill(); });
  } else child.kill("SIGKILL");
}

export async function runCodex(runtime: Runtime, task: Task, signal: AbortSignal, progress: (pid: number | undefined, session: string | null) => Promise<void>): Promise<Result> {
  if (!/^[a-zA-Z0-9-]{1,100}$/.test(task.id)) throw new Error("Invalid task ID");
  const directory = resolve(runtime.directory, "tasks", task.id);
  await mkdir(directory, { recursive: true, mode: 0o700 });
  const schema = join(directory, "schema.json"), final = join(directory, "final.json");
  await writeFile(schema, JSON.stringify(task.schema), { mode: 0o600 });
  const args = ["-a", "never", "-c", 'sandbox_mode="read-only"', "-c", 'web_search="disabled"',
    ...["shell_tool", "apps", "plugins", "hooks", "multi_agent", "browser_use", "computer_use"].flatMap(flag => ["-c", `features.${flag}=false`]), "exec"];
  if (task.session) args.push("resume", task.session);
  args.push("--ignore-user-config", "--ignore-rules", "--json", "--skip-git-repo-check", "--output-schema", schema, "--output-last-message", final, "--model", task.model);
  if (task.effort) args.push("-c", `model_reasoning_effort=${JSON.stringify(task.effort)}`);
  args.push("-");
  return new Promise((complete) => {
    const child = launch(runtime, args, directory);
    let session: string | null = task.session, completed = false, failed = false, timedOut = false, terminalFailure = false;
    let bytes = 0;
    let writes = Promise.resolve();
    const record = () => { writes = writes.then(() => progress(child.pid, session)); writes.catch(() => { failed = true; stop(child); }); };
    const lines = new JsonLines(value => {
      if (value.type === "thread.started" && typeof value.thread_id === "string") { session = value.thread_id; record(); }
      if (value.type === "turn.completed") completed = true;
      if (value.type === "turn.failed") { failed = true; terminalFailure = true; }
      if (value.type === "error") failed = true;
    });
    const cancel = () => stop(child);
    signal.addEventListener("abort", cancel, { once: true });
    const timeout = setTimeout(() => { timedOut = true; stop(child); }, runtime.timeoutMs ?? 600_000);
    child.on("spawn", () => { record(); if (signal.aborted) cancel(); });
    child.stdout!.on("data", (chunk: Buffer) => {
      bytes += chunk.length;
      try { if (bytes > 32_000_000) throw new Error("CLI output limit"); lines.write(chunk); }
      catch { failed = true; stop(child); }
    });
    // Drain stderr, which can contain diagnostics but is never a final response.
    child.stderr!.on("data", () => {});
    child.stdin!.on("error", () => { failed = true; });
    child.on("error", () => { failed = true; });
    child.on("close", async code => {
      clearTimeout(timeout);
      signal.removeEventListener("abort", cancel);
      try { lines.end(); await writes; } catch { failed = true; }
      let raw = "";
      if (completed && !failed && code === 0) {
        try { raw = await readFile(final, "utf8"); } catch { failed = true; }
      }
      complete({ raw, session,
        completion: signal.aborted ? "cancelled" : timedOut || !completed && !terminalFailure ? "uncertain" : completed && !failed && code === 0 ? "completed" : "failed" });
    });
    child.stdin!.end(task.prompt, "utf8");
  });
}

export async function loginStatus(runtime: Runtime): Promise<boolean> {
  return new Promise(resolveStatus => {
    const child = launch(runtime, ["login", "status"]);
    const timeout = setTimeout(() => stop(child), 30_000);
    child.stdout!.resume(); child.stderr!.resume(); child.stdin!.end();
    child.on("error", () => resolveStatus(false));
    child.on("close", code => { clearTimeout(timeout); resolveStatus(code === 0); });
  });
}

export async function discover(runtime: Runtime): Promise<Model[]> {
  const child = launch(runtime, ["app-server"]);
  let sequence = 0;
  const pending = new Map<number, { resolve: (value: any) => void; reject: (reason: Error) => void }>();
  const fail = (error: Error) => { for (const item of pending.values()) item.reject(error); pending.clear(); stop(child); };
  const lines = new JsonLines(value => {
    const item = pending.get(value.id);
    if (item) { pending.delete(value.id); value.error ? item.reject(new Error("Model discovery rejected")) : item.resolve(value.result); }
  });
  child.stdout!.on("data", chunk => { try { lines.write(chunk); } catch { fail(new Error("Invalid discovery JSONL")); } });
  child.stderr!.resume();
  child.on("error", fail);
  child.on("close", () => fail(new Error("Discovery process exited")));
  const timeout = setTimeout(() => fail(new Error("Discovery timeout")), 30_000);
  const request = (method: string, params: object) => new Promise<any>((resolve, reject) => {
    const id = ++sequence;
    pending.set(id, { resolve, reject });
    child.stdin!.write(JSON.stringify({ id, method, params }) + "\n");
  });
  try {
    await request("initialize", { clientInfo: { name: "translator_gateway", title: "Translator Gateway", version: "0.1.0" } });
    child.stdin!.write(JSON.stringify({ method: "initialized", params: {} }) + "\n");
    const models: Model[] = [];
    let cursor: string | null = null;
    const cursors = new Set<string>();
    do {
      const page = await request("model/list", { cursor, limit: 100 });
      for (const item of page.data) models.push({ id: item.id, efforts: (item.supportedReasoningEfforts ?? []).map((e: any) => e.reasoningEffort),
        session: true, context_tokens: null, output_tokens: null });
      cursor = page.nextCursor ?? null;
      if (cursor) { if (cursors.has(cursor)) throw new Error("Repeated model cursor"); cursors.add(cursor); }
    } while (cursor);
    return models;
  } finally {
    clearTimeout(timeout);
    if (child.exitCode === null && child.signalCode === null) {
      await new Promise<void>(resolveClose => { child.once("close", () => resolveClose()); stop(child); });
    }
  }
}
