// Read-only native integration checks; never toggles Windows or TV sound.
const {spawn,execFileSync}=require('node:child_process');
const path=require('node:path'),fs=require('node:fs'),assert=require('node:assert/strict');
const exe=path.join(__dirname,'audio-endpoint-monitor.exe');
const results=[];
function assertState(line){const s=JSON.parse(line);assert.deepEqual(Object.keys(s).sort(),['available','muted']);assert.equal(typeof s.available,'boolean');assert.equal(typeof s.muted,'boolean');return s;}
function delay(ms){return new Promise(resolve=>setTimeout(resolve,ms));}
function firstLine(child,deadline=5000){return new Promise((resolve,reject)=>{let text='';const timer=setTimeout(()=>reject(Error('No startup JSON within deadline')),deadline);child.stdout.on('data',b=>{text+=b;if(text.includes('\n')){clearTimeout(timer);resolve(text.split(/\r?\n/)[0]);}});child.once('error',e=>{clearTimeout(timer);reject(e);});});}
function alive(pid){try{process.kill(pid,0);return true;}catch(e){if(e.code==='ESRCH')return false;throw e;}}
async function main(){
  const started=Date.now();
  const once=execFileSync(exe,['--once'],{windowsHide:true,timeout:5000,encoding:'utf8'}).trim();
  const initial=assertState(once);assert.equal(initial.available,true,'Expected the active Windows render endpoint to be readable');
  results.push('PASS: actual default endpoint startup in '+(Date.now()-started)+'ms: '+once);

  const monitor=spawn(exe,['--parent-pid',String(process.pid)],{windowsHide:true,stdio:['ignore','pipe','pipe']});
  let output='';monitor.stdout.on('data',b=>output+=b);
  try{
    assertState(await firstLine(monitor));
    await delay(2200);
    const lines=output.trim().split(/\r?\n/);lines.forEach(assertState);
    const states=lines.map(JSON.parse);
    for(let i=1;i<states.length;i++)assert.notDeepEqual(states[i],states[i-1],'Unchanged states must not be repeated');
    assert.equal(monitor.exitCode,null,'Monitor should stay alive while parent exists');
    const ps=process.env.WINDIR+'\\System32\\WindowsPowerShell\\v1.0\\powershell.exe';
    const footprint=execFileSync(ps,['-NoProfile','-Command',`Get-Process -Id ${monitor.pid} | Select-Object WorkingSet64,CPU | ConvertTo-Json -Compress`],{windowsHide:true,encoding:'utf8'}).trim();
    results.push('PASS: live monitoring, JSONL state changes only; '+lines.length+' states in 2.2s; footprint '+footprint);
  }finally{monitor.kill();}

  let helperPid;
  const fixture=spawn(process.execPath,['-e',`const {spawn}=require('node:child_process');const c=spawn(${JSON.stringify(exe)},['--parent-pid',String(process.pid)],{windowsHide:true,stdio:['ignore','pipe','ignore']});console.log(c.pid);c.stdout.once('data',()=>setTimeout(()=>process.exit(0),300));setTimeout(()=>process.exit(3),5000);`],{windowsHide:true,stdio:['ignore','pipe','pipe']});
  try{
    helperPid=Number(await firstLine(fixture));assert.ok(helperPid>0);assert.ok(alive(helperPid));
    const status=await new Promise((resolve,reject)=>{const timer=setTimeout(()=>reject(Error('Fixture parent did not exit')),6000);fixture.once('exit',code=>{clearTimeout(timer);resolve(code);});});
    assert.equal(status,0,'Fixture must receive initial state before exiting');
    const deadline=Date.now()+2000;while(alive(helperPid)&&Date.now()<deadline)await delay(50);
    assert.equal(alive(helperPid),false,'Helper must exit after parent without a state change');
    results.push('PASS: helper exits after parent closes, without requiring a Windows mute change');
  }finally{if(fixture.exitCode===null)fixture.kill();if(helperPid&&alive(helperPid))process.kill(helperPid);}
  fs.writeFileSync(path.join(__dirname,'VALIDATION.txt'),results.join('\r\n')+'\r\n');
  console.log(results.join('\n'));
}
main().catch(e=>{console.error(e);process.exitCode=1;});
