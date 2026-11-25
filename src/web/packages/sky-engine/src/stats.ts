// Frame statistics: rolling FPS and 95th percentile frame time (spec §4.2 performance budget).

export class FrameStats {
  private readonly times: number[] = [];
  private last: number | null = null;
  private total = 0;

  constructor(private readonly capacity = 600) {}

  /** Records a frame at `nowMs` (performance.now()). */
  tick(nowMs: number): void {
    if (this.last !== null) {
      this.times.push(nowMs - this.last);
      if (this.times.length > this.capacity) {
        this.times.shift();
      }
    }
    this.last = nowMs;
    this.total++;
  }

  get frames(): number {
    return this.total;
  }

  get fps(): number {
    if (this.times.length === 0) {
      return 0;
    }
    const mean = this.times.reduce((sum, t) => sum + t, 0) / this.times.length;
    return mean > 0 ? 1000 / mean : 0;
  }

  get p95FrameMs(): number {
    if (this.times.length === 0) {
      return 0;
    }
    const sorted = [...this.times].sort((a, b) => a - b);
    return sorted[Math.min(sorted.length - 1, Math.floor(sorted.length * 0.95))] ?? 0;
  }

  reset(): void {
    this.times.length = 0;
    this.last = null;
    this.total = 0;
  }
}

/**
 * Spec §7.4: when the p95 frame time stays over the budget (20 ms) for five whole seconds in a
 * row, the sky should get cheaper. Stalls (> 250 ms: a hidden window, a breakpoint) do not count.
 */
export class SlowFrameWatch {
  private frames: number[] = [];
  private windowStart = -1;
  private slowSeconds = 0;

  constructor(
    private readonly budgetMs = 20,
    private readonly seconds = 5,
  ) {}

  /** Adds a frame of `frameMs` ending at `nowMs`; true when the sky has been slow long enough. */
  add(frameMs: number, nowMs: number): boolean {
    if (frameMs > 250) {
      this.reset();
      return false;
    }
    if (this.windowStart < 0) {
      this.windowStart = nowMs;
    }
    this.frames.push(frameMs);
    if (nowMs - this.windowStart < 1000) {
      return false;
    }
    const sorted = [...this.frames].sort((a, b) => a - b);
    const p95 = sorted[Math.min(sorted.length - 1, Math.floor(sorted.length * 0.95))] ?? 0;
    this.slowSeconds = p95 > this.budgetMs ? this.slowSeconds + 1 : 0;
    this.frames = [];
    this.windowStart = nowMs;
    if (this.slowSeconds >= this.seconds) {
      this.slowSeconds = 0;
      return true;
    }
    return false;
  }

  reset(): void {
    this.frames = [];
    this.windowStart = -1;
    this.slowSeconds = 0;
  }
}
