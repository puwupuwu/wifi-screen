import assert from "node:assert/strict";
import { EventEmitter } from "node:events";
import test from "node:test";
import { audioPayload, effectiveAudioEnabled } from "./audio-control.mjs";
import { initialPcMuteState, createMuteReportParser, startPcMuteMonitor } from "./pc-mute-monitor.mjs";

function fixture(options = {}) {
  const calls = [], states = [], timers = new Map();
  const setTimer = (fn, ms) => { const timer = { unref() {} }; timers.set(timer, { fn, ms }); return timer; };
  const clearTimer = (timer) => timers.delete(timer);
  const spawn = (file, args, settings) => {
    const child = new EventEmitter(); child.stdout = new EventEmitter(); child.kills = 0;
    child.kill = () => { child.kills++; child.emit("exit", 0); };
    calls.push({ file, args, settings, child });
    return child;
  };
  const monitor = startPcMuteMonitor({ executable: "monitor.exe", parentPid: 123,
    platform: "win32", spawn, setTimer, clearTimer,
    onChange: (state) => states.push(state), ...options });
  return { calls, states, monitor, timers,
    report(value, child = calls.at(-1).child) { child.stdout.emit("data", Buffer.from(JSON.stringify(value) + "\n")); },
    tick(ms) { for (const [timer, value] of [...timers]) if (value.ms === ms) { timers.delete(timer); value.fn(); } },
  };
}

test("manual audio preference and PC mute both gate sound without changing RTP frame length", () => {
  const pcm = new Uint8Array([1, 2, 3, 4, 5, 6, 7, 8]);
  for (const enabled of [false, true]) for (const muted of [false, true]) {
    const effective = effectiveAudioEnabled(enabled,
      { muteSyncRequired: true, muteSyncAvailable: true, pcMuted: muted });
    const output = audioPayload(pcm, effective);
    assert.equal(output.byteLength, pcm.byteLength);
    assert.deepEqual([...output], enabled && !muted ? [...pcm] : Array(8).fill(0));
  }
  assert.equal(effectiveAudioEnabled(true, initialPcMuteState("win32")), false);
  assert.equal(effectiveAudioEnabled(true, initialPcMuteState("linux")), true);
  assert.equal(effectiveAudioEnabled(false, initialPcMuteState("linux")), false);
});

test("helper launches hidden with parent lifetime tracking and reports mute/unmute changes", () => {
  const f = fixture();
  assert.deepEqual(f.calls[0].args, ["--parent-pid", "123"]);
  assert.equal(f.calls[0].settings.windowsHide, true);
  assert.deepEqual(f.calls[0].settings.stdio, ["ignore", "pipe", "ignore"]);
  f.report({ muted: true, available: true });
  assert.equal(f.monitor.getState().pcMuted, true);
  assert.equal(effectiveAudioEnabled(true, f.monitor.getState()), false);
  // A newly selected default output can have a different mute state.
  f.report({ muted: false, available: true });
  assert.equal(effectiveAudioEnabled(true, f.monitor.getState()), true);
  assert.equal(f.timers.size, 0);
  f.monitor.stop();
  assert.equal(f.calls[0].child.kills, 1);
});

test("unavailable endpoint retains last known mute and silences TV until recovery", () => {
  const f = fixture();
  f.report({ muted: true, available: true });
  f.report({ muted: false, available: false });
  assert.equal(f.monitor.getState().pcMuted, true);
  assert.equal(f.monitor.getState().muteSyncAvailable, false);
  assert.equal(effectiveAudioEnabled(true, f.monitor.getState()), false);
  f.report({ muted: false, available: true });
  assert.equal(effectiveAudioEnabled(true, f.monitor.getState()), true);
  f.monitor.stop();
});

test("parser accepts fragmented JSONL and bounds malformed helper output", () => {
  const reports = []; let errors = 0;
  const parse = createMuteReportParser((value) => reports.push(value), () => errors++);
  parse(Buffer.from('{"muted":tr'));
  parse(Buffer.from('ue,"available":true}\r\n'));
  assert.deepEqual(reports, [{ muted: true, available: true }]);
  parse(Buffer.from("x".repeat(4097)));
  assert.equal(errors, 1);
  parse(Buffer.from("invalid\n".repeat(8)));
  assert.equal(errors, 2);
});

test("process errors retry a bounded number of times and stop cancels remaining work", () => {
  const f = fixture();
  f.report({ muted: false, available: true });
  for (let index = 0; index < 4; index++) {
    f.calls.at(-1).child.emit("error", new Error("unavailable"));
    assert.equal(effectiveAudioEnabled(true, f.monitor.getState()), false);
    f.tick(1000);
  }
  assert.equal(f.calls.length, 4);
  assert.equal(f.monitor.getState().pcMuted, false);
  assert.equal(f.timers.size, 0);
  f.monitor.stop();
  f.tick(1000);
  assert.equal(f.calls.length, 4);
});

test("startup with no endpoint report stays silent and times out the helper", () => {
  const f = fixture();
  assert.equal(effectiveAudioEnabled(true, f.monitor.getState()), false);
  f.tick(5000);
  assert.equal(f.calls[0].child.kills, 1);
  f.monitor.stop();
  f.tick(1000);
  assert.equal(f.calls.length, 1);
  assert.equal(f.timers.size, 0);
});

test("non-Windows does not launch a helper or block the manual audio switch", () => {
  const f = fixture({ platform: "darwin" });
  assert.equal(f.calls.length, 0);
  assert.equal(effectiveAudioEnabled(true, f.monitor.getState()), true);
  f.monitor.stop();
});
