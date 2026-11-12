'use strict';
// Unit-only analyzer fixture. Real native integration is exercised separately.
const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const childProcess = require('node:child_process');

const script = String.raw`
const fs = require('node:fs');
const crypto = require('node:crypto');
const args = process.argv.slice(2);
const batch = JSON.parse(fs.readFileSync(args[args.indexOf('--batch') + 1]));
if (process.env.ATH_BATCH_FIXTURE_LOG) fs.appendFileSync(process.env.ATH_BATCH_FIXTURE_LOG, JSON.stringify({ count: batch.items.length, schemaVersion: batch.schemaVersion, inputBytes:batch.items.reduce((sum,item)=>sum+fs.statSync(item.input).size,0) }) + '\n');
const code = value => ({pass:0,fail:1,inconclusive:2,'infrastructure-error':3})[value];
const entries = [];
const terminalMode = JSON.parse(fs.readFileSync(batch.items[0].manifest)).fixtureTerminalMode || '';
if (batch.schemaVersion === 2 && terminalMode === 'finished-without-index') process.stdout.write(JSON.stringify({schemaVersion:2,event:'finished',outcome:'pass'})+'\n');
if (batch.schemaVersion === 2 && terminalMode === 'null-progress') process.stdout.write('null\n');
(async () => {
  for (let index = 0; index < batch.items.length; index++) {
    const item = batch.items[index];
    if (batch.schemaVersion === 2) process.stdout.write(JSON.stringify({schemaVersion:2,index,event:'started'}) + '\n');
    const manifest = JSON.parse(fs.readFileSync(item.manifest));
    await new Promise(resolve => setTimeout(resolve, manifest.fixtureDelayMs || 0));
    const policy = JSON.parse(fs.readFileSync(item.policy));
    const result = {schemaVersion:1,outcome:'pass',policy,image:manifest.resolution,context:{screenId:manifest.screenId,screenshotId:manifest.screenshotId,locale:manifest.locale,uiScale:manifest.uiScale},coverage:{mode:'manifest',expected:manifest.elements.length,measured:manifest.elements.length,limitations:[]},findings:manifest.elements.flatMap((element,i)=>['text.height','text.contrast','text.clipping'].map(ruleId=>({ruleId,elementId:element.id || 'expected-'+i,bounds:element.bounds,expectedBounds:element.bounds,outcome:'pass',measuredHeightPx:30,contrastRatio:7,measurementConfidence:1,detectionConfidence:1,message:'fixture'})))};
    if (item.annotated) fs.copyFileSync(item.input,item.annotated,fs.constants.COPYFILE_EXCL);
    entries.push(result);
    if (batch.schemaVersion === 2) {
      const bytes=Buffer.from(JSON.stringify(result));
      fs.writeFileSync(item.output,bytes,{flag:'wx'});
      const finished={schemaVersion:2,index,event:'finished',outcome:result.outcome,resultSha256:crypto.createHash('sha256').update(bytes).digest('hex'),resultBytes:bytes.length};
      if(manifest.fixtureResultMode==='tamper') { result.findings[0].contrastRatio=8;fs.writeFileSync(item.output,JSON.stringify(result)); }
      if(manifest.fixtureResultMode==='missing-digest') delete finished.resultSha256;
      process.stdout.write(JSON.stringify(finished) + '\n');
    }
  }
  if (batch.schemaVersion === 2) {
    if (terminalMode !== 'missing-complete') process.stdout.write(JSON.stringify({schemaVersion:2,event:'complete',outcome:terminalMode==='wrong-aggregate'?'fail':'pass',count:entries.length})+'\n');
  } else process.stdout.write(JSON.stringify({schemaVersion:1,outcome:'pass',results:entries}));
  process.exitCode=terminalMode==='wrong-exit'?1:code('pass');
})();
`;

function createScreens(root, count, { invalid = -1, malformed = false, delayMs = 0, regions = 1, encodedBytes = 24, resultMode = '', terminalMode = '' } = {}) {
  const screens = [];
  const input = Buffer.alloc(24);
  Buffer.from('89504e470d0a1a0a', 'hex').copy(input); input.write('IHDR', 12); input.writeUInt32BE(100, 16); input.writeUInt32BE(100, 20);
  const policy = path.join(root, 'policy.json');
  fs.writeFileSync(policy, JSON.stringify({ schemaVersion:1,id:'fixture',version:'1',source:'fixture://batch',minHeightPx:24,minContrastRatio:4.5 }));
  for (let index = 0; index < count; index++) {
    const id = 'screen' + String(index).padStart(4, '0');
    const screenshot = id + '.png', manifest = id + '.json';
    if (index !== invalid) { fs.writeFileSync(path.join(root, screenshot), input); fs.truncateSync(path.join(root, screenshot),encodedBytes); }
    else if (malformed) fs.writeFileSync(path.join(root, screenshot), 'not a PNG');
    fs.writeFileSync(path.join(root, manifest), JSON.stringify({schemaVersion:1,screenId:id,screenshotId:screenshot,locale:'en-US',uiScale:1,resolution:{width:100,height:100},elements:Array.from({length:regions},(_,i)=>({id:'title'+i,bounds:{x:1,y:1,width:40,height:30}})),fixtureDelayMs:Array.isArray(delayMs)?delayMs[index]:delayMs,fixtureResultMode:Array.isArray(resultMode)?resultMode[index]:resultMode,fixtureTerminalMode:terminalMode}));
    screens.push({id,screenshot,manifest});
  }
  const manifest = path.join(root, 'audit-manifest.json');
  fs.writeFileSync(manifest, JSON.stringify({schemaVersion:1,screens}));
  return {screens,manifest,policy};
}

async function withFakeAnalyzer(action) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'ath-batch-review-'));
  const fake = path.join(root, 'analyzer.cjs'), log = path.join(root, 'calls.jsonl');
  fs.writeFileSync(fake, script);
  const originalSpawn = childProcess.spawn, oldLog = process.env.ATH_BATCH_FIXTURE_LOG;
  const modulePath = require.resolve('./visual'), oldModule = require.cache[modulePath];
  childProcess.spawn = (executable, args, options) => originalSpawn(executable, executable === process.execPath && args[0] === '--batch' ? [fake, ...args] : args, options);
  process.env.ATH_BATCH_FIXTURE_LOG = log;
  delete require.cache[modulePath];
  try { return await action({root,log,visual:require('./visual'),reports:require('./visual-reports'),createScreens:(count,options)=>createScreens(root,count,options)}); }
  finally {
    childProcess.spawn = originalSpawn;
    if (oldLog === undefined) delete process.env.ATH_BATCH_FIXTURE_LOG; else process.env.ATH_BATCH_FIXTURE_LOG = oldLog;
    delete require.cache[modulePath]; if (oldModule) require.cache[modulePath] = oldModule;
    fs.rmSync(root,{recursive:true,force:true});
  }
}

module.exports = { withFakeAnalyzer };
