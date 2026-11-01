import test from 'node:test';
import assert from 'node:assert/strict';
import { lanAddresses, selectLanAddress, mdnsQuery, parseRecords, servicesFromRecords } from './network.mjs';
const adapters = {
  'Ethernet VPN':[{address:'198.51.100.181',family:'IPv4',internal:false}],
  'ZeroTier':[{address:'169.254.57.42',family:'IPv4',internal:false}],
  'Wi-Fi':[{address:'192.0.2.9',family:'IPv4',internal:false}],
  'Loopback':[{address:'127.0.0.1',family:'IPv4',internal:true}],
};
test('route to TV takes priority; exclude link-local and loopback',()=>{
  assert.equal(selectLanAddress(adapters,'192.0.2.9'),'192.0.2.9');
  assert.equal(selectLanAddress(adapters,null),'192.0.2.9');
  assert.deepEqual(lanAddresses(adapters).map(x=>x.address),['198.51.100.181','192.0.2.9']);
});
test('queries request unicast PTR replies',()=>{
  assert.deepEqual([...mdnsQuery().subarray(-5)],[0,0,12,128,1]);
});
const dnsName = value => Buffer.concat([...value.split('.').map(s=>Buffer.concat([Buffer.from([Buffer.byteLength(s)]),Buffer.from(s)])),Buffer.from([0])]);
function record(name,type,data){const h=Buffer.alloc(10);h.writeUInt16BE(type);h.writeUInt16BE(1,2);h.writeUInt32BE(120,4);h.writeUInt16BE(data.length,8);return Buffer.concat([dnsName(name),h,data]);}
test('TCL reply yields its IP, service port, feature flags and identity',()=>{
  const instance='TV._airplay._tcp.local',srv=Buffer.alloc(6);srv.writeUInt16BE(7000,4);
  const txt=['deviceid=TEST-TV','model=75C845','features=0x7F8AD0,0x38BCF46'];
  const records=[record('_airplay._tcp.local',12,dnsName(instance)),record(instance,33,Buffer.concat([srv,dnsName('localhost.local')])),record(instance,16,Buffer.concat(txt.map(s=>Buffer.concat([Buffer.from([s.length]),Buffer.from(s)])))),record('localhost.local',1,Buffer.from([192,0,2,10]))];
  const header=Buffer.alloc(12);header[2]=128;header.writeUInt16BE(records.length,6);
  const services=servicesFromRecords(parseRecords(Buffer.concat([header,...records])),'192.0.2.10');
  assert.equal(services.length,1);assert.equal(services[0].port,7000);assert.equal(services[0].txt.model,'75C845');assert.deepEqual(services[0].addresses,['192.0.2.10']);
});
test('truncated records and pointer cycles are rejected',()=>{
  assert.throws(()=>parseRecords(Buffer.alloc(3)));
  const loop=Buffer.alloc(14);loop[2]=128;loop.writeUInt16BE(1,6);loop[12]=192;loop[13]=12;
  assert.throws(()=>parseRecords(loop));
});
