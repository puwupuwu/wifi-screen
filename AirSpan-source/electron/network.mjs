import dgram from 'node:dgram';
import net from 'node:net';
import os from 'node:os';

export function lanAddresses(interfaces = os.networkInterfaces()) {
  return Object.entries(interfaces).flatMap(([name, entries]) => (entries || [])
    .filter(e => !e.internal && e.family === 'IPv4' && !e.address.startsWith('169.254.'))
    .map(e => ({ name, address: e.address })));
}
export function selectLanAddress(interfaces, routedAddress) {
  const list = lanAddresses(interfaces);
  if (list.some(e => e.address === routedAddress)) return routedAddress;
  return list.find(e => /wi-?fi|wireless|wlan/i.test(e.name))?.address
    || list.find(e => !/vpn|zerotier|wireguard|virtual|tailscale|wintun/i.test(e.name) && e.address.startsWith('192.168.'))?.address
    || list.find(e => !/vpn|zerotier|wireguard|virtual|tailscale|wintun/i.test(e.name))?.address
    || list[0]?.address || '127.0.0.1';
}
export function routeAddress(target) {
  return new Promise(resolve => {
    if (!net.isIPv4(target)) return resolve(null);
    const socket = dgram.createSocket('udp4');
    let done = false;
    const finish = value => { if (done) return; done = true; clearTimeout(timer); try { socket.close(); } catch {} resolve(value); };
    const timer = setTimeout(() => finish(null), 1500);
    socket.on('error', () => finish(null));
    socket.connect(8009, target, () => { try { finish(socket.address().address); } catch { finish(null); } });
  });
}
export function mdnsQuery() {
  const header = Buffer.from([0,0,0,0,0,1,0,0,0,0,0,0]);
  return Buffer.concat([header, ...'_airplay._tcp.local'.split('.').map(s => Buffer.concat([Buffer.from([s.length]), Buffer.from(s)])), Buffer.from([0,0,12,128,1])]);
}
export function parseRecords(packet) {
  if (packet.length < 12 || !(packet[2] & 128)) throw new Error('Not a DNS response');
  function name(start) {
    let at=start, end=null, labels=[], steps=0, total=0;
    while (true) {
      if (at>=packet.length || ++steps>128) throw new Error('Invalid DNS name');
      const size=packet[at++];
      if (!size) return {value:labels.join('.'), end:end??at};
      if ((size&192)===192) { if(at>=packet.length)throw new Error('Truncated pointer');end??=at+1;at=((size&63)<<8)|packet[at];continue; }
      if(size>63 || at+size>packet.length || (total+=size+1)>255)throw new Error('Invalid label');
      labels.push(packet.toString('utf8',at,at+size));at+=size;
    }
  }
  let p=12, count=packet.readUInt16BE(6)+packet.readUInt16BE(8)+packet.readUInt16BE(10);
  const questions=packet.readUInt16BE(4);if(questions>128||count>512)throw new Error('Oversized DNS response');
  for(let i=0;i<questions;i++){p=name(p).end+4;if(p>packet.length)throw new Error('Truncated question');}
  const records=[];
  for(let i=0;i<count;i++) {
    const owner=name(p);p=owner.end;if(p+10>packet.length)throw new Error('Truncated record');
    const type=packet.readUInt16BE(p),ttl=packet.readUInt32BE(p+4),len=packet.readUInt16BE(p+8);p+=10;const end=p+len;if(end>packet.length)throw new Error('Truncated data');
    const r={name:owner.value,type,ttl};
    if(type===12){const n=name(p);if(n.end>end)throw new Error('Invalid PTR');r.target=n.value;}
    if(type===33){if(len<7)throw new Error('Invalid SRV');r.port=packet.readUInt16BE(p+4);const n=name(p+6);if(n.end>end)throw new Error('Invalid SRV');r.target=n.value;}
    if(type===16){r.txt={};let at=p;while(at<end){const size=packet[at++];if(at+size>end)throw new Error('Invalid TXT');const entry=packet.toString('utf8',at,at+size),sep=entry.indexOf('=');if(sep>0)r.txt[entry.slice(0,sep)]=entry.slice(sep+1);at+=size;}}
    if(type===1&&len===4)r.address=Array.from(packet.subarray(p,end)).join('.');
    if(ttl)records.push(r);p=end;
  }
  return records;
}
export function servicesFromRecords(records, remote) {
  const names=new Set(records.map(r => r.type===12?r.target:r.name).filter(n => n?.toLowerCase().endsWith('._airplay._tcp.local')));
  return [...names].map(instance => {
    const srv=records.find(r => r.type===33&&r.name===instance);
    const txt=records.find(r => r.type===16&&r.name===instance)?.txt;
    if(!srv||!txt)return null;
    const address=records.find(r => r.type===1&&r.name===srv.target)?.address || remote;
    return {name:instance.slice(0,-'._airplay._tcp.local'.length),host:srv.target,port:srv.port,addresses:[address],txt};
  }).filter(Boolean);
}
// Legacy-unicast mDNS queries avoid relying solely on a single default
// multicast interface (often a VPN). Query each local adapter independently.
export function discoveryAdapters(interfaces, selectedAddress = '') {
  const entries = lanAddresses(interfaces);
  if (!selectedAddress) return entries;
  const selected = entries.filter(entry => entry.address === selectedAddress);
  if (!selected.length) throw new Error('That adapter is no longer available. Refresh the adapter list.');
  return selected;
}

export function startUnicastDiscovery(onService, onError=()=>{}, selectedAddress = '') {
  const sockets=[],timers=[];
  let stopped = false;
  for(const entry of discoveryAdapters(os.networkInterfaces(), selectedAddress)) {
    const socket=dgram.createSocket('udp4');sockets.push(socket);
    const cache=new Map();
    socket.on('error', error => onError(entry.name,error.message));
    socket.on('message',(packet,remote)=>{
      if (stopped) return;
      if(remote.port!==5353)return;
      try {
        const now=Date.now();for(const [key,item] of cache)if(item.expires<now)cache.delete(key);
        for(const r of parseRecords(packet)){if(cache.size>1024)cache.clear();cache.set(remote.address+'|'+r.name+'|'+r.type,{record:r,remote:remote.address,expires:now+Math.min(r.ttl,30)*1000});}
        const records=[...cache.values()].filter(x=>x.remote===remote.address).map(x=>x.record);
        for(const service of servicesFromRecords(records,remote.address))onService(service);
      }catch {} // Ignore malformed packets, never take down the app.
    });
    socket.bind(0,entry.address,()=>{
      if (stopped) { try { socket.close(); } catch {} return; }
      try{socket.setMulticastInterface(entry.address);}catch(error){onError(entry.name,error.message);}
      const query=()=>socket.send(mdnsQuery(),5353,'224.0.0.251',error=>{if(error)onError(entry.name,error.message);});
      query();timers.push(setInterval(query,3000));
    });
  }
  return {stop(){stopped=true;for(const timer of timers)clearInterval(timer);for(const socket of sockets)try{socket.close();}catch{}}};
}
