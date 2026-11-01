import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';
import test from 'node:test';

// Run the production packet writer, rather than reimplementing it in tests.
const source = readFileSync(new URL('./main.mjs', import.meta.url), 'utf8');
const start = source.indexOf('function sendAirPlayAudioSync(');
const end = source.indexOf('\nfunction sendAirPlayAudioPacket(', start);
const syncSource = source.slice(start, end);
function syncPacket({ head, protocol = 'PTP', first = true }) {
  const packets = [];
  const wall = 0x0001fd1685aea593n;
  const timeline = 0xf84288888f4f0008n;
  const write = vm.runInNewContext(`(${syncSource})`, {
    Buffer, Date, console, airPlayDiagnostic() {},
    getAirPlayMediaTimestamp: () => ({ timestamp: wall }),
    airPlayPtpNanoseconds: value => value,
  });
  const audio = {
    timingProtocol: protocol, remoteControlPort: 12345,
    remoteAddress: '192.0.2.1', latencySamples: 11025,
    controlSocket: { send: packet => packets.push(Buffer.from(packet)) },
    syncPackets: first ? 0 : 1,
  };
  assert.equal(write({ mediaClock: { timelineID: timeline } }, audio, head, true), true);
  assert.equal(packets.length, 1);
  return packets[0];
}

test('PTP announce matches the captured iOS AirPlay 2 wire vector', () => {
  // Captured protocol data documented in OwnTone's rtp_common.c.
  const expected = Buffer.from('90d70006cbbbfe400001fd1685aea593cbbd2bb7f84288888f4f0008', 'hex');
  assert.deepEqual(syncPacket({ head: 0xcbbbfe40 }), expected);
});

test('PTP receiver has a positive buffer window and 250ms playback lead across wraparound', () => {
  for (const head of [0, 1, 123456789, 0xfffffff0]) {
    const packet = syncPacket({ head, first: false });
    const frame1 = packet.readUInt32BE(4), frame2 = packet.readUInt32BE(16);
    // Shairport Sync derives the receive window and playback anchor this way.
    assert.equal(((frame2 - frame1) >>> 0) + 11025, 88200);
    const samplePlayingAtAnchor = (frame1 - 11025) >>> 0;
    assert.equal((head - samplePlayingAtAnchor) >>> 0, 11025);
    assert.equal(packet[0], 0x80);
  }
});

test('legacy NTP sync retains its independent timestamp ordering', () => {
  const packet = syncPacket({ head: 100000, protocol: 'NTP' });
  assert.equal(packet.length, 20);
  assert.equal(packet[1], 0xd4);
  assert.equal(packet.readUInt32BE(4), 100000 - 11025);
  assert.equal(packet.readUInt32BE(16), 100000);
});
