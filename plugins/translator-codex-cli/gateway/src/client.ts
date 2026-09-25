import { randomBytes, randomUUID } from "node:crypto";
import { mkdir, readFile } from "node:fs/promises";
import { join } from "node:path";
import { setTimeout as delay } from "node:timers/promises";
import { atomicJson, Journal, type Entry } from "./journal.js";
import { discover, loginStatus, runCodex, type Runtime } from "./codex.js";
import type { Device, Registration, Task, Result, Model } from "./protocol.js";

export class ApiError extends Error {
  constructor(readonly status: number) { super(`Gateway HTTP ${status}`); }
}

export class Server {
  constructor(readonly device: Device) {
    const url = new URL(device.server_url);
    if (url.protocol !== "https:" || url.username || url.password || url.search || url.hash) throw new Error("Gateway requires an HTTPS server URL");
  }

  async call(path: string, body?: unknown): Promise<any> {
    const response = await fetch(this.device.server_url.replace(/\/$/, "") + path, {
      method: body === undefined ? "GET" : "POST", redirect: "error", signal: AbortSignal.timeout(35_000),
      headers: { Authorization: `Bearer ${this.device.device_token}`, "Content-Type": "application/json" },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
    if (!response.ok) throw new ApiError(response.status);
    return response.status === 204 ? undefined : response.json();
  }
}

export async function bootstrap(directory: string, registration: Registration): Promise<Device> {
  await mkdir(directory, { recursive: true, mode: 0o700 });
  const path = join(directory, "device.json");
  let device: Device;
  try {
    device = JSON.parse(await readFile(path, "utf8")) as Device;
    if (device.client_instance_id !== registration.client_instance_id || device.server_url !== registration.server_url) throw new Error("Existing installation belongs to another client instance");
  } catch (error) {
    if ((error as NodeJS.ErrnoException).code !== "ENOENT") throw error;
    device = { server_url: registration.server_url, client_instance_id: registration.client_instance_id,
      installation_id: randomUUID(), device_token: randomBytes(32).toString("base64url") };
    // Persist identity before enrollment so retry after response loss is idempotent.
    await atomicJson(path, device);
  }
  if (!device.gateway_id) {
    const result = await new Server(device).call("/internal/gateways/enroll", { code: registration.code, installation_id: device.installation_id, device_token: device.device_token });
    if (result.client_instance_id !== device.client_instance_id) throw new Error("Enrollment client mismatch");
    device.gateway_id = result.gateway_id;
    await atomicJson(path, device);
  }
  return device;
}

export class Gateway {
  private active: { id: string; abort: AbortController } | undefined;
  private readiness: "ready" | "login_required" | "error" = "login_required";
  private catalog: Model[] | undefined;
  private catalogAt = 0;
  private heartbeatBusy = false;

  constructor(readonly server: Pick<Server, "call">, readonly journal: Journal, readonly runtime: Runtime,
              readonly runner = runCodex) {}

  async deliver(entry: Entry): Promise<void> {
    if (entry.state === "acknowledged") { await this.journal.acknowledge(entry); return; }
    if (!entry.result) throw new Error("Journal result missing");
    const response = await this.server.call(`/internal/gateways/tasks/${entry.task.id}/result`, { lease_token: entry.task.lease_token, result: entry.result });
    if (!["accepted", "duplicate", "discarded"].includes(response.status)) throw new Error("Unknown result acknowledgement");
    await this.journal.acknowledge(entry);
  }

  async recover(executeReceived = true): Promise<void> {
    for (const entry of await this.journal.entries()) {
      if (entry.state === "acknowledged" || entry.state === "result_ready") { await this.deliver(entry); continue; }
      if (entry.state === "received") { if (executeReceived) await this.execute(entry.task); continue; }
      // PID reuse makes killing or relaunching from a persisted PID unsafe.
      const recovered: Entry = { ...entry, state: "result_ready", result: { raw: "", completion: "uncertain", session: entry.session ?? entry.task.session } };
      await this.journal.save(recovered);
      await this.deliver(recovered);
    }
  }

  async execute(task: Task): Promise<void> {
    if (this.active) throw new Error("Only one Codex turn may run at a time");
    let entry = await this.journal.get(task.id);
    if (!entry && task.state === "started") {
      entry = { task, state: "result_ready", result: { raw: "", completion: "uncertain", session: task.session } };
      await this.journal.save(entry);
    }
    if (entry?.state === "result_ready" || entry?.state === "acknowledged") { await this.deliver(entry); return; }
    if (entry && entry.state !== "received") throw new Error("Unreconciled attempt");
    entry = entry ?? { task, state: "received" };
    await this.journal.save(entry);
    let authorization;
    try { authorization = await this.server.call(`/internal/gateways/tasks/${task.id}/started`, { lease_token: task.lease_token }); }
    catch (error) {
      if (error instanceof ApiError && error.status === 409) { await this.journal.acknowledge(entry); return; }
      throw error;
    }
    if (!authorization.authorized) throw new Error("Start not authorized");
    entry.state = "start_authorized";
    await this.journal.save(entry);
    const abort = new AbortController();
    this.active = { id: task.id, abort };
    try {
      const result = await this.runner(this.runtime, task, abort.signal, async (pid, session) => {
        entry!.state = "running"; entry!.pid = pid;
        if (session) entry!.session = session;
        await this.journal.save(entry!);
      });
      entry.result = result;
      entry.state = "result_ready";
      await this.journal.save(entry);
      await this.deliver(entry);
    } finally { this.active = undefined; }
  }

  async heartbeat(): Promise<void> {
    if (this.heartbeatBusy) return;
    this.heartbeatBusy = true;
    try {
      const response = await this.server.call("/internal/gateways/heartbeat", { readiness: this.readiness, active_task: this.active?.id ?? null,
        ...(this.catalog ? { models: this.catalog } : {}) });
      this.catalog = undefined;
      if (this.active && response.cancel.includes(this.active.id)) this.active.abort.abort();
    } finally { this.heartbeatBusy = false; }
  }

  async run(signal: AbortSignal): Promise<void> {
    const heartbeat = setInterval(() => { void this.heartbeat().catch(() => {}); }, 15_000);
    const cancel = () => this.active?.abort.abort();
    signal.addEventListener("abort", cancel);
    let failures = 0;
    try {
      while (!signal.aborted) {
        try {
          if (!this.active && Date.now() - this.catalogAt > 600_000) {
            if (!await loginStatus(this.runtime)) { this.readiness = "login_required"; }
            else {
              try { this.catalog = await discover(this.runtime); this.catalogAt = Date.now(); this.readiness = "ready"; }
              catch { this.readiness = "error"; }
            }
          }
          await this.heartbeat();
          await this.recover(this.readiness === "ready");
          if (this.readiness !== "ready") { await delay(15_000, undefined, { signal }); continue; }
          const task = await this.server.call("/internal/gateways/tasks/next") as Task | undefined;
          if (task) await this.execute(task);
          failures = 0;
        } catch (error) {
          if (signal.aborted) break;
          // Logs intentionally omit prompts, credentials, HTTP bodies and enrollment codes.
          process.stderr.write(`Gateway reconnect: ${error instanceof ApiError ? error.message : "connection or runtime error"}\n`);
          await delay(Math.min(30_000, 1000 * 2 ** Math.min(failures++, 5)) * (0.5 + Math.random()/2), undefined, { signal }).catch(() => {});
        }
      }
    } finally { clearInterval(heartbeat); signal.removeEventListener("abort", cancel); }
  }
}
