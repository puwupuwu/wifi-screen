import test from 'node:test';
import assert from 'node:assert/strict';
import { discoveryAdapters } from './network.mjs';
import { audioPayload } from './audio-control.mjs';
const interfaces = {
  'Wi-Fi': [{ family:'IPv4', internal:false, address:'192.0.2.9' }],
  'VPN': [{ family:'IPv4', internal:false, address:'198.51.100.3' }],
  'Loopback': [{ family:'IPv4', internal:true, address:'127.0.0.1' }],
};
test('explicit scanning excludes VPN and all other adapters', () => {
  assert.deepEqual(discoveryAdapters(interfaces, '192.0.2.9'), [{name:'Wi-Fi',address:'192.0.2.9'}]);
  assert.equal(discoveryAdapters(interfaces).length, 2);
  assert.throws(() => discoveryAdapters(interfaces, '192.0.2.8'), /no longer available/);
});
test('audio disable removes PCM sound without changing clock frame size; re-enable restores sound', () => {
  const captured = new Uint8Array([1,2,3,4,5,6,7,8]);
  assert.deepEqual([...audioPayload(captured, false)], [0,0,0,0,0,0,0,0]);
  assert.deepEqual([...audioPayload(captured, true)], [1,2,3,4,5,6,7,8]);
  assert.deepEqual([...captured], [1,2,3,4,5,6,7,8]);
});
