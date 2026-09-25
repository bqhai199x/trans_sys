import { StringDecoder } from "node:string_decoder";

/** Stateful UTF-8 and line framing; malformed or excessive output fails closed. */
export class JsonLines {
  private readonly decoder = new StringDecoder("utf8");
  private pending = "";
  constructor(private readonly onValue: (value: any) => void, private readonly maxLine = 8_000_000) {}

  write(chunk: Buffer): void { this.consume(this.decoder.write(chunk)); }
  end(): void {
    this.consume(this.decoder.end());
    if (this.pending.trim()) this.onValue(JSON.parse(this.pending));
    this.pending = "";
  }

  private consume(value: string): void {
    this.pending += value;
    let newline: number;
    while ((newline = this.pending.indexOf("\n")) >= 0) {
      const line = this.pending.slice(0, newline);
      if (line.length > this.maxLine) throw new Error("JSONL line exceeds limit");
      this.pending = this.pending.slice(newline + 1);
      if (line.trim()) this.onValue(JSON.parse(line));
    }
    if (this.pending.length > this.maxLine) throw new Error("JSONL line exceeds limit");
  }
}
