'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const { EventEmitter } = require('node:events');
const { PassThrough } = require('node:stream');
const childProcess = require('node:child_process');
const { withFakeAnalyzer } = require('./visual-batch-fixture');

async function withUnconfirmedKill(action,{synchronous=false}={}) {
  await withFakeAnalyzer(async ({root,reports,createScreens}) => {
    const analyzer = path.join(root,'analyzer.cjs');
    const modulePath = require.resolve('./visual'), previousModule = require.cache[modulePath];
    const originalSpawn = childProcess.spawn, workers = [];
    let killRequests = 0;
    childProcess.spawn = (executable,args,options) => {
      if (executable !== analyzer || args[0] !== '--batch') return originalSpawn(executable,args,options);
      const worker = new EventEmitter();
      worker.stdout = new PassThrough(); worker.stderr = new PassThrough(); worker.unref = () => {};
      worker.kill = () => {
        killRequests++;
        // No close event follows: the OS has not confirmed worker termination.
        const fail = () => worker.emit('error',Object.assign(new Error('permission denied while terminating'),{code:'EPERM'}));
        if(synchronous) fail(); else setImmediate(fail);
        return false;
      };
      workers.push(worker);
      setImmediate(() => worker.stdout.write(JSON.stringify({schemaVersion:2,index:0,event:'started'})+'\n'));
      return worker;
    };
    delete require.cache[modulePath]; const visual = require('./visual');
    try {
      await action({root,visual,reports,createScreens,analyzer,workers,killRequests:()=>killRequests});
    } finally {
      childProcess.spawn = originalSpawn;
      delete require.cache[modulePath]; require.cache[modulePath] = previousModule;
      for (const worker of workers) { worker.stdout.destroy(); worker.stderr.destroy(); }
    }
  });
}

test('an unconfirmed kill error stops all later audit groups', async () => {
  for(const synchronous of [false,true]) await withUnconfirmedKill(async ({root,reports,createScreens,analyzer,workers,killRequests})=>{
    const fixture = createScreens(12);
    const result = await reports.audit({input:root,manifest:fixture.manifest,policy:fixture.policy,out:path.join(root,'audit'),analyzer,timeoutMs:20});
    assert.deepEqual(result.entries.map(entry=>entry.id),fixture.screens.map(screen=>screen.id));
    assert.ok(result.entries.every(entry=>entry.outcome==='infrastructure-error'));
    assert.equal(workers.length,1,'a worker whose termination is unconfirmed must not overlap with a retry or later group');
    assert.equal(killRequests(),1);
    assert.ok(result.entries.every(entry=>/termination unconfirmed/i.test(entry.message)));
  },{synchronous});
});

test('direct batch calls also stop before launching a later bounded group', async () => {
  for(const synchronous of [false,true]) await withUnconfirmedKill(async ({root,visual,createScreens,analyzer,workers,killRequests})=>{
    const fixture=createScreens(12);
    const results=await visual.analyzeBatch(fixture.screens.map(screen=>({input:path.join(root,screen.screenshot),manifest:path.join(root,screen.manifest),policy:fixture.policy,out:path.join(root,screen.id),analyzer,timeoutMs:20})),path.join(root,'batch.json'));
    assert.equal(results.length,12);assert.ok(results.every(result=>result.result.outcome==='infrastructure-error'));
    assert.equal(workers.length,1,'an unconfirmed worker must stop later internal groups');
    assert.equal(killRequests(),1);
    assert.equal(results.terminationUnconfirmed,true);
    assert.equal(Object.keys(results).includes('terminationUnconfirmed'),false,'fatal lifecycle metadata does not change the public JSON schema');
  },{synchronous});
});
