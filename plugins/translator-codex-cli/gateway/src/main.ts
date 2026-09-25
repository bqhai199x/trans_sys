import { copyFile, mkdir, readFile } from "node:fs/promises";
import { homedir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { bootstrap, Gateway, Server } from "./client.js";
import { Journal } from "./journal.js";
import type { Registration } from "./protocol.js";

const bundle = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const directory = process.env.TRANSLATOR_GATEWAY_DATA ?? join(process.env.LOCALAPPDATA ?? homedir(), "TransSysGateway");
await mkdir(directory, { recursive: true, mode: 0o700 });
// OS exclusive lock is held by a listening named pipe; it disappears on process exit.
const { createServer } = await import("node:net");
const { createHash } = await import("node:crypto");
const lockName = createHash("sha256").update(directory).digest("hex").slice(0, 24);
const lock = createServer();
await new Promise<void>((resolveLock, reject) => {
  lock.once("error", reject);
  lock.listen(process.platform === "win32" ? `\\\\.\\pipe\\trans-sys-${lockName}` : join(directory, "gateway.sock"), resolveLock);
});
const codexHome = join(directory, "codex-home");
await mkdir(codexHome, { recursive: true, mode: 0o700 });
// Copy only credentials, never user config, MCP servers, project hooks or instructions.
try { await copyFile(join(homedir(), ".codex", "auth.json"), join(codexHome, "auth.json"), 1); }
catch (error) { if (!["ENOENT", "EEXIST"].includes((error as NodeJS.ErrnoException).code ?? "")) throw error; }
const registration = JSON.parse(await readFile(process.env.TRANSLATOR_GATEWAY_REGISTRATION ?? join(bundle, "registration.json"), "utf8")) as Registration;
const runtime = { node: process.env.TRANSLATOR_GATEWAY_NODE ?? join(bundle, "node", "node.exe"), codex: process.env.TRANSLATOR_GATEWAY_CODEX ?? join(bundle, "node_modules", "@openai", "codex", "bin", "codex.js"), directory, codexHome };
const abort = new AbortController();
process.on("SIGINT", () => abort.abort());
process.on("SIGTERM", () => abort.abort());
try {
  let device;
  while (!abort.signal.aborted && !device) {
    try { device = await bootstrap(directory, registration); }
    catch { process.stderr.write("Enrollment retry pending; check connection or request a new installer if enrollment expired.\n"); await new Promise(r => setTimeout(r, 15_000)); }
  }
  if (device) await new Gateway(new Server(device), new Journal(join(directory, "journal")), runtime).run(abort.signal);
} finally { lock.close(); }
