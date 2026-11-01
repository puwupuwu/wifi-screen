import { spawn as spawnProcess } from "node:child_process";

export function initialPcMuteState(platform = process.platform) {
  return {
    pcMuted: platform === "win32" ? null : false,
    muteSyncRequired: platform === "win32",
    muteSyncAvailable: false,
    muteSyncMessage: platform === "win32" ? "Checking PC mute state; TV audio is muted until ready." : "",
  };
}

export function createMuteReportParser(onReport, onInvalid) {
  let pending = "";
  let invalidLines = 0;
  return (chunk) => {
    if (chunk.length > 65536) { pending = ""; onInvalid(); return; }
    pending += chunk.toString("utf8");
    let newline;
    while ((newline = pending.indexOf("\n")) >= 0) {
      const line = pending.slice(0, newline).replace(/\r$/, "");
      pending = pending.slice(newline + 1);
      if (line.length > 4096) { pending = ""; onInvalid(); return; }
      try {
        const report = JSON.parse(line);
        if (typeof report.available !== "boolean" || typeof report.muted !== "boolean") throw new Error();
        invalidLines = 0;
        onReport({ available: report.available, muted: report.muted });
      } catch {
        if (++invalidLines >= 8) { pending = ""; onInvalid(); return; }
      }
    }
    if (pending.length > 4096) { pending = ""; onInvalid(); }
  };
}

export function startPcMuteMonitor({ executable, onChange, platform = process.platform,
  parentPid = process.pid, spawn = spawnProcess, maxRestarts = 3,
  restartDelayMs = 1000, startupTimeoutMs = 5000,
  setTimer = setTimeout, clearTimer = clearTimeout }) {
  let state = initialPcMuteState(platform);
  let child = null, restartTimer = null, startupTimer = null;
  let stopped = false, restarts = 0;
  const publish = (update) => {
    const next = { ...state, ...update };
    if (JSON.stringify(next) === JSON.stringify(state)) return;
    state = next;
    onChange({ ...state });
  };
  const unavailable = () => publish({ muteSyncAvailable: false,
    muteSyncMessage: "PC mute sync is unavailable; TV audio is muted until it recovers." });
  const fail = (current) => {
    if (stopped || child !== current) return;
    clearTimer(startupTimer); startupTimer = null;
    child = null;
    try { current?.kill(); } catch {}
    unavailable();
    if (restarts < maxRestarts) {
      restarts++;
      restartTimer = setTimer(() => { restartTimer = null; launch(); }, restartDelayMs);
      restartTimer?.unref?.();
    }
  };
  const launch = () => {
    if (stopped) return;
    let current;
    try {
      current = spawn(executable, ["--parent-pid", String(parentPid)], {
        windowsHide: true, stdio: ["ignore", "pipe", "ignore"],
      });
    } catch {
      // Use a sentinel so synchronous spawn failures share bounded retry logic.
      current = { kill() {} }; child = current; fail(current); return;
    }
    child = current;
    const parse = createMuteReportParser((report) => {
      if (stopped || child !== current) return;
      clearTimer(startupTimer); startupTimer = null;
      if (report.available) {
        publish({ pcMuted: report.muted, muteSyncAvailable: true,
          muteSyncMessage: report.muted ? "PC muted · TV audio muted" : "TV audio follows PC mute." });
      } else {
        // An unavailable endpoint is not an unmute. Retain the last known
        // state and fail closed until a real endpoint can be read again.
        unavailable();
      }
    }, () => fail(current));
    current.stdout?.on("data", parse);
    current.on("error", () => fail(current));
    current.on("exit", () => fail(current));
    startupTimer = setTimer(() => fail(current), startupTimeoutMs);
    startupTimer?.unref?.();
  };
  onChange({ ...state });
  if (state.muteSyncRequired) launch();
  return {
    getState: () => ({ ...state }),
    stop() {
      stopped = true;
      clearTimer(restartTimer); clearTimer(startupTimer);
      restartTimer = startupTimer = null;
      const current = child; child = null;
      try { current?.kill(); } catch {}
    },
  };
}
