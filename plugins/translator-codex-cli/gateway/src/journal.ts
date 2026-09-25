import { open, readFile, rename, mkdir, readdir, unlink } from "node:fs/promises";
import { join } from "node:path";
import { randomUUID } from "node:crypto";
import type { Task, Result } from "./protocol.js";

export async function atomicJson(path: string, value: unknown): Promise<void> {
  const temporary = `${path}.${randomUUID()}.tmp`;
  const file = await open(temporary, "wx", 0o600);
  try {
    await file.writeFile(JSON.stringify(value), "utf8");
    await file.sync();
  } finally { await file.close(); }
  await rename(temporary, path);
}

export interface Entry {
  task: Task;
  state: "received" | "start_authorized" | "running" | "result_ready" | "acknowledged";
  pid?: number;
  session?: string;
  result?: Result;
}

export class Journal {
  constructor(readonly directory: string) {}

  private path(id: string): string {
    if (!/^[a-zA-Z0-9-]{1,100}$/.test(id)) throw new Error("Invalid task ID");
    return join(this.directory, `${id}.json`);
  }

  async save(entry: Entry): Promise<void> {
    await mkdir(this.directory, { recursive: true, mode: 0o700 });
    await atomicJson(this.path(entry.task.id), entry);
  }

  async get(id: string): Promise<Entry | undefined> {
    try { return JSON.parse(await readFile(this.path(id), "utf8")) as Entry; }
    catch (error) { if ((error as NodeJS.ErrnoException).code === "ENOENT") return undefined; throw error; }
  }

  async entries(): Promise<Entry[]> {
    await mkdir(this.directory, { recursive: true, mode: 0o700 });
    const names = (await readdir(this.directory)).filter(name => name.endsWith(".json"));
    return Promise.all(names.map(async name => JSON.parse(await readFile(join(this.directory, name), "utf8")) as Entry));
  }

  async acknowledge(entry: Entry): Promise<void> {
    await this.save({ ...entry, state: "acknowledged" });
    await unlink(this.path(entry.task.id));
  }
}
