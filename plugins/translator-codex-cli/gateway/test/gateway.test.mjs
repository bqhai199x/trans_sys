import assert from 'node:assert/strict';
import { test } from 'node:test';
import { mkdtemp, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { JsonLines } from '../dist/jsonl.js';
import { Journal } from '../dist/journal.js';
import { Gateway } from '../dist/client.js';
import { runCodex, discover } from '../dist/codex.js';

const task = {id:'attempt-1',lease_token:'lease',state:'queued',prompt:'translate',schema:{type:'object'},model:'fake',effort:null,session:null};
const result = {raw:'{"texts":["done"]}',completion:'completed',session:'session'};

test('JSONL preserves split UTF-8 and multiple line boundaries', () => {
  const values=[];
  const reader=new JsonLines(v=>values.push(v));
  for(const byte of Buffer.from('{"text":"đỏ 😀"}\n{"n":2}')) reader.write(Buffer.from([byte]));
  reader.end();
  assert.deepEqual(values,[{text:'đỏ 😀'},{n:2}]);
  assert.throws(()=>new JsonLines(()=>{}).write(Buffer.from('{invalid}\n')));
});

test('lost ACK replays journal result without starting CLI again', async () => {
  const directory=await mkdtemp(join(tmpdir(),'gateway-test-'));
  try {
    let executions=0, deliveries=0;
    const server={async call(path) {
      if(path.endsWith('/started'))return {authorized:true};
      if(++deliveries===1)throw new Error('lost ACK');
      return {status:'duplicate'};
    }};
    const runner=async()=>{executions++;return result;};
    const gateway=new Gateway(server,new Journal(directory),{},runner);
    await assert.rejects(gateway.execute(task));
    assert.equal((await gateway.journal.get(task.id)).state,'result_ready');
    await new Gateway(server,new Journal(directory),{},runner).recover();
    assert.equal(executions,1);
    assert.deepEqual(await gateway.journal.entries(),[]);
  } finally { await rm(directory,{recursive:true,force:true}); }
});

test('restart with authorized turn reports uncertainty and never kills stale PID',async()=>{
  const directory=await mkdtemp(join(tmpdir(),'gateway-test-'));
  try {
    const journal=new Journal(directory);
    await journal.save({task,state:'running',pid:process.pid,session:'saved-session'});
    let recovered;
    const server={async call(path,body){recovered=body.result;return {status:'accepted'};}};
    await new Gateway(server,journal,{},async()=>{throw new Error('must not run');}).recover();
    assert.equal(recovered.completion,'uncertain');
    assert.equal(recovered.session,'saved-session');
  } finally {await rm(directory,{recursive:true,force:true});}
});

test('server started task with missing local journal is never replayed',async()=>{
  const directory=await mkdtemp(join(tmpdir(),'gateway-test-'));
  try {
    let recovered;
    const server={async call(path,body){recovered=body.result;return {status:'accepted'};}};
    await new Gateway(server,new Journal(directory),{},async()=>{throw new Error('must not run');}).execute({...task,state:'started'});
    assert.equal(recovered.completion,'uncertain');
  } finally {await rm(directory,{recursive:true,force:true});}
});

test('CLI completion requires terminal event and uses final response file',async()=>{
  const directory=await mkdtemp(join(tmpdir(),'gateway-test-'));
  try {
    const runtime={node:process.execPath,codex:resolve('test/fake-cli.mjs'),directory,codexHome:directory,timeoutMs:5000};
    const progress=[];
    const output=await runCodex(runtime,task,new AbortController().signal,async(pid,session)=>progress.push(session));
    assert.equal(output.completion,'completed');
    assert.deepEqual(JSON.parse(output.raw),{texts:['xe đỏ 😀']});
    assert.equal(output.session,'session-123');
    assert.ok(progress.includes('session-123'));
    const missing=await runCodex(runtime,{...task,id:'attempt-2',prompt:'no-completion'},new AbortController().signal,async()=>{});
    assert.equal(missing.completion,'uncertain');
    assert.equal(missing.raw,'');
    const timeout=await runCodex({...runtime,timeoutMs:200},{...task,id:'attempt-3',prompt:'hang'},new AbortController().signal,async()=>{});
    assert.equal(timeout.completion,'uncertain');
    const abort=new AbortController();
    setTimeout(()=>abort.abort(),200);
    const cancelled=await runCodex(runtime,{...task,id:'attempt-4',prompt:'hang'},abort.signal,async()=>{});
    assert.equal(cancelled.completion,'cancelled');
  } finally {await rm(directory,{recursive:true,force:true});}
});

test('model discovery initializes and reads every page with model-specific efforts',async()=>{
  const directory=await mkdtemp(join(tmpdir(),'gateway-test-'));
  try {
    const models=await discover({node:process.execPath,codex:resolve('test/fake-discovery.mjs'),directory,codexHome:directory});
    assert.deepEqual(models.map(m=>[m.id,m.efforts]),[['first-model',['low']],['second-model',['high']]]);
    assert.ok(models.every(m=>m.context_tokens===null));
  } finally {await rm(directory,{recursive:true,force:true});}
});
