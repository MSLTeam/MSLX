export interface ConsoleCursor { streamId: string; sequence: number }
export interface ConsoleReplay { streamId: string; sequence: number; gap: boolean; frames: { sequence: number; text: string }[] }

// 历史回放相关
export class ConsoleStream {
  cursor: ConsoleCursor | null = null;
  private pending = new Map<number, string>();
  constructor(private write: (text: string) => void) {}
  receive(text: string, cursor: ConsoleCursor) {
    if (!this.cursor || this.cursor.streamId !== cursor.streamId) return false;
    if (cursor.sequence <= this.cursor.sequence) return true;
    this.pending.set(cursor.sequence, text);
    while (this.pending.has(this.cursor.sequence + 1)) {
      const next = this.cursor.sequence + 1;
      this.write(this.pending.get(next)!);
      this.pending.delete(next);
      this.cursor = { streamId: cursor.streamId, sequence: next };
    }
    return true;
  }
  replay(snapshot: ConsoleReplay | null) {
    if (!snapshot) return;
    if (!this.cursor || this.cursor.streamId !== snapshot.streamId || snapshot.gap) {
      this.pending.clear();
      this.cursor = { streamId: snapshot.streamId, sequence: snapshot.frames.length ? snapshot.frames[0].sequence - 1 : snapshot.sequence };
    }
    for (const frame of snapshot.frames) this.receive(frame.text, { streamId: snapshot.streamId, sequence: frame.sequence });
  }
  reset() { this.cursor = null; this.pending.clear(); }
}
