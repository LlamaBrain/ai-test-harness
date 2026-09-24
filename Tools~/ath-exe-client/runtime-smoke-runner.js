'use strict';

const fsDefault = require('node:fs');
const path = require('node:path');
const cryptoDefault = require('node:crypto');

const HEX_256 = /^[0-9a-f]{64}$/;

/** Validate the durable evidence produced by one externally launched RuntimeSmoke. */
function validateRuntimeSmokeEvidence(evidence, trustedLaunch) {
  const invalid = (code, message) => ({ accepted: false, code, message });
  if (!evidence || !evidence.launch || !evidence.armed || !Array.isArray(evidence.transcript) || !evidence.process)
    return invalid('evidence_invalid', 'Launch, armed record, transcript, and process observation are required.');

  const { launch, armed, process: observed } = evidence;
  if (!trustedLaunch) return invalid('trust_anchor_missing', 'The runner-created launch envelope must be retained as the evidence trust anchor.');
  if (stableJson(launch) !== stableJson(trustedLaunch) ||
      launch.buildToken !== createBuildToken(trustedLaunch))
    return invalid('launch_tampered', 'Persisted launch envelope differs from the runner-owned envelope or its build token.');
  if (launch.schemaVersion !== 1 || !launch.runId || !launch.correlationId || !launch.workflowId ||
      !HEX_256.test(launch.scriptHash || '') || !HEX_256.test(launch.nonce || '') || !HEX_256.test(launch.buildToken || '') ||
      !Number.isInteger(launch.timeoutSeconds) || launch.timeoutSeconds < 1 || launch.timeoutSeconds > 86400 ||
      !Array.isArray(launch.requiredStepIds) || launch.requiredStepIds.length === 0 ||
      new Set(launch.requiredStepIds).size !== launch.requiredStepIds.length ||
      launch.requiredStepIds.some((id) => !safeId(id)))
    return invalid('launch_invalid', 'Launch envelope is missing valid identity, hash, nonce, token, or deadline fields.');

  const identityMatches = armed.state === 'armed' && armed.runId === launch.runId &&
    armed.correlationId === launch.correlationId && armed.scriptHash === launch.scriptHash &&
    armed.nonce === launch.nonce && armed.buildToken === launch.buildToken &&
    armed.processId === observed.processId && armed.processStartUtc === observed.processStartUtc &&
    armed.executableSha256 === launch.executableSha256 && armed.buildRevision === launch.buildRevision &&
    armed.profileHash === launch.profileHash && armed.contentHash === launch.contentHash &&
    sameOptionalIdentity(armed.scenario, launch.scenario) && sameOptionalIdentity(armed.seed, launch.seed) &&
    observed.executableSha256 === launch.executableSha256 && pathEquals(observed.executablePath, launch.executablePath);
  if (!identityMatches) return invalid('provenance_mismatch', 'Player armed record does not match the launch envelope and watched process.');

  const launchTime = Date.parse(launch.startedUtc);
  const processTime = Date.parse(observed.processStartUtc);
  const exitTime = Date.parse(observed.exitedUtc);
  if (![launchTime, processTime, exitTime].every(Number.isFinite) || processTime < launchTime || exitTime < processTime)
    return invalid('timestamp_invalid', 'Launch and process timestamps are invalid or out of order.');
  if (exitTime >= launchTime + launch.timeoutSeconds * 1000)
    return invalid('watchdog_timeout', 'Launched process reached or exceeded its bounded deadline.');
  if (observed.exitCode !== 0) return invalid('process_exit_failed', 'RuntimeSmoke player process did not exit cleanly.');

  const log = evidence.log;
  const logTime = log && Date.parse(log.lastWriteUtc);
  // Windows filesystems may round write times to a 2-second boundary; the run directory is
  // newly claimed and empty, so permit that small post-exit rounding window for the fresh log.
  if (!log || !pathEquals(log.path, launch.logPath) || !Number.isFinite(log.length) || log.length <= 0 ||
      !Number.isFinite(logTime) || logTime < processTime || logTime > exitTime + 2000)
    return invalid('log_invalid', 'Player log is empty, stale, outside the process lifetime, or not the envelope-owned log.');

  let terminalCount = 0;
  let previousTime = processTime;
  for (let index = 0; index < evidence.transcript.length; index++) {
    const record = evidence.transcript[index];
    if (!record || record.sequence !== index)
      return invalid('sequence_invalid', 'Transcript sequence must be contiguous and start at zero.');
    if (record.correlationId !== launch.correlationId || record.workflowId !== launch.workflowId || record.scriptHash !== launch.scriptHash ||
        !sameOptionalIdentity(record.scenario, launch.scenario) || !sameOptionalIdentity(record.seed, launch.seed))
      return invalid('identity_mismatch', 'Transcript records must use the launch correlation, workflow, script hash, scenario, and seed.');
    const timestamp = Date.parse(record.timestampUtc);
    if (!Number.isFinite(timestamp) || timestamp < launchTime || timestamp < previousTime || timestamp > exitTime)
      return invalid('timestamp_invalid', 'Transcript event falls outside its process lifetime or is not monotonic.');
    previousTime = timestamp;
    const allowedStatuses = {
      armed: ['armed'], run_started: ['running'], step_started: ['running'], step_completed: ['passed'],
      review_ready: ['review_ready'], checkpoint_resumed: ['resumed'],
      run_terminal: ['passed', 'failed', 'cancelled'],
    };
    if (!allowedStatuses[record.type] || !allowedStatuses[record.type].includes(String(record.status).toLowerCase()))
      return invalid('failure_record_present', 'Transcript contains an unknown, failed, or non-success event that cannot contribute to PASS.');
    if (record.type === 'run_terminal') terminalCount++;
  }

  if (terminalCount === 0) return invalid('terminal_missing', 'Transcript has no terminal RuntimeSmoke record.');
  if (terminalCount !== 1) return invalid('terminal_duplicate', 'Transcript must contain exactly one terminal RuntimeSmoke record.');
  const terminal = evidence.transcript[evidence.transcript.length - 1];
  if (terminal.type !== 'run_terminal' || terminal.status !== 'passed')
    return invalid('terminal_not_pass', 'The final transcript record must be the single passed terminal result.');
  if (evidence.transcript[0].type !== 'armed' || evidence.transcript[0].status !== 'armed' ||
      evidence.transcript[1]?.type !== 'run_started')
    return invalid('steps_missing', 'Transcript must begin with armed followed by run_started.');
  const completedSteps = evidence.transcript.slice(2, -1)
    .filter((record) => record.type === 'step_completed' && record.status === 'passed')
    .map((record) => record.stepId);
  if (completedSteps.length !== launch.requiredStepIds.length ||
      completedSteps.some((id, index) => id !== launch.requiredStepIds[index]))
    return invalid('steps_missing', 'Transcript does not contain every required step result in declared order.');

  const result = evidence.result;
  if (!result || result.correlationId !== launch.correlationId || result.workflowId !== launch.workflowId ||
      result.scriptHash !== launch.scriptHash || !sameOptionalIdentity(result.scenario, launch.scenario) ||
      !sameOptionalIdentity(result.seed, launch.seed) || result.terminalState !== 'passed' || result.failureCode)
    return invalid('result_mismatch', 'Structured result identity or terminal state disagrees with the launch and transcript.');
  const resultStart = Date.parse(result.startedUtc);
  const resultEnd = Date.parse(result.endedUtc);
  if (![resultStart, resultEnd].every(Number.isFinite) || resultStart < launchTime || resultStart < processTime ||
      resultStart > resultEnd || resultEnd > Date.parse(terminal.timestampUtc) ||
      Date.parse(terminal.timestampUtc) - resultEnd > 10000 || resultEnd > exitTime)
    return invalid('result_mismatch', 'Structured result timestamps do not bind to the terminal transcript and process lifetime.');

  return { accepted: true, code: 'pass', terminal };
}

/** Refuse reuse of any artifact directory; evidence is never cleared to make a run fit. */
async function prepareRuntimeSmokeRun({ artifactRoot, fs = fsDefault } = {}) {
  if (typeof artifactRoot !== 'string' || artifactRoot.length === 0)
    throw Object.assign(new Error('artifactRoot is required'), { code: 'artifact_root_invalid' });
  let stat;
  try { stat = fs.lstatSync(artifactRoot); }
  catch (error) {
    throw Object.assign(new Error('artifact root must exist before launch'), { code: 'artifact_root_missing', cause: error });
  }
  if (stat.isSymbolicLink())
    throw Object.assign(new Error('artifact root cannot be a symbolic link'), { code: 'artifact_root_escape' });
  if (!stat.isDirectory())
    throw Object.assign(new Error('artifact root must be a directory'), { code: 'artifact_root_invalid' });
  if (fs.readdirSync(artifactRoot).length !== 0)
    throw Object.assign(new Error('artifact root must be empty; existing evidence is never deleted'), { code: 'artifact_root_not_empty' });
  return artifactRoot;
}

/** Create the durable, single-run launch envelope consumed by the player-side claim protocol. */
function createRuntimeSmokeLaunch(options = {}) {
  const fs = options.fs || fsDefault;
  const crypto = options.crypto || cryptoDefault;
  const required = ['hostRoot', 'outputRoot', 'authoredRoot', 'executablePath', 'buildEvidencePath',
    'runId', 'correlationId', 'workflowId', 'scriptHash', 'target', 'mode'];
  for (const key of required) {
    if (options[key] == null || options[key] === '')
      throw Object.assign(new Error(`${key} is required`), { code: 'launch_input_invalid' });
  }
  if (!safeId(options.runId) || !safeId(options.correlationId) || !HEX_256.test(options.scriptHash) ||
      !Number.isInteger(options.timeoutSeconds) || options.timeoutSeconds < 1 || options.timeoutSeconds > 86400 ||
      (options.seed != null && !isValidSeed(options.seed)) ||
      !['development', 'smoke_harness'].includes(options.target) || options.mode !== 'fsd')
    throw Object.assign(new Error('Launch identity, hash, target, mode, or deadline is invalid.'), { code: 'launch_input_invalid' });
  const requiredStepIds = options.requiredStepIds;
  if (!Array.isArray(requiredStepIds) || requiredStepIds.length === 0 ||
      new Set(requiredStepIds).size !== requiredStepIds.length || requiredStepIds.some((id) => !safeId(id)))
    throw Object.assign(new Error('A unique ordered required-step list is required.'), { code: 'launch_input_invalid' });

  const hostRoot = fs.realpathSync(options.hostRoot);
  const outputRoot = path.resolve(options.outputRoot);
  const authoredRoot = fs.realpathSync(options.authoredRoot);
  const requestedExecutablePath = path.resolve(options.executablePath);
  const requestedBuildEvidencePath = path.resolve(options.buildEvidencePath);
  if (!isWithin(requestedExecutablePath, hostRoot) || !isWithin(requestedBuildEvidencePath, hostRoot) ||
      overlaps(requestedBuildEvidencePath, authoredRoot))
    throw Object.assign(new Error('Executable and build evidence must remain inside the selected host and outside authored inputs.'), { code: 'build_provenance_mismatch' });
  assertNoSymlinkPath(fs, requestedExecutablePath, hostRoot);
  assertNoSymlinkPath(fs, requestedBuildEvidencePath, hostRoot);
  const executablePath = fs.realpathSync(options.executablePath);
  const buildEvidencePath = fs.realpathSync(options.buildEvidencePath);
  assertNoSymlinkPath(fs, outputRoot, hostRoot);
  if (!isWithin(outputRoot, hostRoot) || pathEquals(outputRoot, hostRoot) || overlaps(outputRoot, authoredRoot))
    throw Object.assign(new Error('Output root must remain inside the host and outside authored inputs.'), { code: 'artifact_root_escape' });
  if (!isWithin(executablePath, hostRoot) || !fs.statSync(executablePath).isFile())
    throw Object.assign(new Error('Executable must be a file inside the selected host.'), { code: 'executable_invalid' });

  const buildEvidence = JSON.parse(fs.readFileSync(buildEvidencePath, 'utf8'));
  const executableSha256 = hashFile(fs, crypto, executablePath);
  if (!buildEvidence || buildEvidence.schemaVersion !== 1 || !pathEquals(buildEvidence.artifactPath, executablePath) ||
      buildEvidence.artifactSha256 !== executableSha256 || !buildEvidence.revision || !buildEvidence.version ||
      !HEX_256.test(buildEvidence.profileHash || '') || !HEX_256.test(buildEvidence.contentHash || '') ||
      !buildEvidence.backend || !buildEvidence.stripping || buildEvidence.lane !== options.target)
    throw Object.assign(new Error('Build evidence does not prove the exact executable, revision, profile, content, backend, stripping, and lane.'), { code: 'build_provenance_mismatch' });

  if (options.runId === '.' || options.runId === '..')
    throw Object.assign(new Error('Run ID cannot be a path segment.'), { code: 'launch_input_invalid' });
  const runDirectory = path.resolve(outputRoot, options.runId);
  if (!isWithin(runDirectory, outputRoot) || pathEquals(runDirectory, outputRoot) || !pathEquals(path.dirname(runDirectory), outputRoot))
    throw Object.assign(new Error('Run ID must resolve to one direct child of the output root.'), { code: 'artifact_root_escape' });
  let created = false;
  try { fs.mkdirSync(runDirectory); created = true; }
  catch (error) {
    if (error && error.code === 'EEXIST')
      throw Object.assign(new Error('Run directory already exists; existing artifacts are never cleared.'), { code: 'run_directory_collision' });
    throw error;
  }
  if (!created) throw Object.assign(new Error('Run directory could not be claimed.'), { code: 'run_directory_collision' });
  for (const folder of ['scratch', 'logs', 'captures']) fs.mkdirSync(path.join(runDirectory, folder));

  const nonceBytes = (options.randomBytes || crypto.randomBytes)(32);
  if (!Buffer.isBuffer(nonceBytes) || nonceBytes.length !== 32)
    throw Object.assign(new Error('Nonce provider must return exactly 256 bits.'), { code: 'launch_input_invalid' });
  const buildEvidenceHash = hashBuildEvidence(crypto, buildEvidence);
  const startedUtc = (options.now ? options.now() : new Date()).toISOString();
  const logPath = path.join(runDirectory, 'logs', 'player.log');
  const buildEvidenceSnapshotPath = path.join(runDirectory, 'build-evidence.json');
  writeAtomicNew(fs, buildEvidenceSnapshotPath, stableJson(buildEvidence) + '\n');
  const envelope = {
    schemaVersion: 1,
    state: 'awaiting_player',
    runId: options.runId,
    correlationId: options.correlationId,
    workflowId: options.workflowId,
    scriptHash: options.scriptHash,
    hostRoot,
    outputRoot,
    authoredRoot,
    runDirectory,
    artifactRoot: runDirectory,
    executablePath,
    executableSha256,
    logPath,
    buildEvidenceSnapshotPath,
    processId: null,
    processStartUtc: null,
    startedUtc,
    timeoutSeconds: options.timeoutSeconds,
    target: options.target,
    mode: options.mode,
    suiteId: options.suiteId || null,
    scenario: options.scenario || null,
    seed: options.seed == null ? null : String(options.seed),
    requiredStepIds: requiredStepIds.slice(),
    buildRevision: buildEvidence.revision,
    buildVersion: buildEvidence.version,
    profileHash: buildEvidence.profileHash,
    contentHash: buildEvidence.contentHash,
    backend: buildEvidence.backend,
    stripping: buildEvidence.stripping,
    buildEvidenceHash,
    nonce: nonceBytes.toString('hex'),
    buildToken: null,
  };
  envelope.buildToken = createBuildToken(envelope, crypto);
  writeAtomicNew(fs, path.join(runDirectory, 'runtime-smoke-launch.json'), stableJson(envelope) + '\n');

  const args = ['-runtimeSmoke', options.workflowId,
    '-runtimeSmokeCorrelation', options.correlationId,
    '-runtimeSmokeArtifactRoot', runDirectory,
    '-runtimeSmokeNonce', envelope.nonce,
    '-runtimeSmokeBuildToken', envelope.buildToken,
    '-logFile', logPath];
  return { envelope, args, runDirectory, logPath };
}

/** Launch one player and accept it only after clean exit plus fully bound evidence. */
async function launchRuntimeSmokeProcess(options = {}) {
  const fs = options.fs || fsDefault;
  const prepared = createRuntimeSmokeLaunch(options);
  const spawnProcess = options.spawnProcess || require('node:child_process').spawn;
  let child;
  const fail = (code, message, processInfo = null) => {
    const watch = { schemaVersion: 1, state: 'failed', failureCode: code, diagnostic: cleanText(message),
      runId: prepared.envelope.runId, correlationId: prepared.envelope.correlationId,
      processId: processInfo && processInfo.processId,
      terminationConfirmed: processInfo && processInfo.terminationConfirmed,
      processState: processInfo && processInfo.terminationConfirmed === false ? 'termination_unconfirmed' : null,
      observedUtc: new Date().toISOString() };
    try { writeAtomicNew(fs, path.join(prepared.runDirectory, 'runtime-smoke-watch.json'), stableJson(watch) + '\n'); }
    catch (_) { /* retain original evidence; a conflicting watch record is itself a failure */ }
    return { ...watch, accepted: false, runDirectory: prepared.runDirectory };
  };

  let spawnError = null;
  let exitRecord = null;
  let spawned;
  let exited;

  try {
    child = spawnProcess(prepared.envelope.executablePath, prepared.args, {
      cwd: prepared.envelope.hostRoot,
      detached: false,
      stdio: 'ignore',
      windowsHide: false,
    });
    spawned = new Promise((resolve) => {
      child.once('spawn', () => resolve(true));
      child.once('error', (error) => { spawnError = error; resolve(false); });
    });
    exited = new Promise((resolve) => {
      child.once('exit', (code, signal) => {
        exitRecord = { code, signal, exitedUtc: new Date().toISOString() };
        resolve(exitRecord);
      });
      child.once('error', (error) => { spawnError = error; resolve(null); });
    });
    const spawnTimeoutMs = Math.min(30000, prepared.envelope.timeoutSeconds * 1000);
    const spawnOk = await raceTimeout(spawned, spawnTimeoutMs, false);
    if (!spawnOk || spawnError) {
      const terminationConfirmed = child.pid ? await stopOwnedChild(child, exited, options.killGraceMs || 5000) : null;
      return fail('player_spawn_failed', spawnError ? spawnError.message : 'Player process did not spawn before the launch deadline.',
        child.pid ? { processId: child.pid, terminationConfirmed } : null);
    }
    if (!Number.isInteger(child.pid) || child.pid <= 0)
      return fail('process_identity_invalid', 'Spawned player did not expose a valid process ID.');

    const launchDeadline = Date.parse(prepared.envelope.startedUtc) + prepared.envelope.timeoutSeconds * 1000;
    const identityTimeoutMs = Math.max(1, launchDeadline - Date.now());
    let image;
    try {
      image = options.observeProcessIdentity
        ? options.observeProcessIdentity(child.pid, identityTimeoutMs)
        : observeProcessIdentity(child.pid, identityTimeoutMs);
    } catch (error) {
      const terminationConfirmed = await stopOwnedChild(child, exited, options.killGraceMs || 5000);
      const code = Date.now() >= launchDeadline ? 'watchdog_timeout' : 'process_identity_unavailable';
      return fail(code, `Could not observe the spawned process image: ${error.message}`, { processId: child.pid, terminationConfirmed });
    }
    const imagePath = image && image.executablePath;
    const imageHash = imagePath && fs.existsSync(imagePath) ? hashFile(fs, options.crypto || cryptoDefault, imagePath) : null;
    if (!image || !pathEquals(imagePath, prepared.envelope.executablePath) || imageHash !== prepared.envelope.executableSha256 ||
        !Number.isFinite(Date.parse(image.processStartUtc))) {
      const terminationConfirmed = await stopOwnedChild(child, exited, options.killGraceMs || 5000);
      return fail('launched_image_mismatch', 'Observed process image, start time, or bytes do not match the exact launch artifact.', { processId: child.pid, terminationConfirmed });
    }

    const processInfo = {
      schemaVersion: 1,
      state: 'watching',
      runId: prepared.envelope.runId,
      correlationId: prepared.envelope.correlationId,
      workflowId: prepared.envelope.workflowId,
      scriptHash: prepared.envelope.scriptHash,
      nonce: prepared.envelope.nonce,
      buildToken: prepared.envelope.buildToken,
      processId: child.pid,
      processStartUtc: new Date(image.processStartUtc).toISOString(),
      executablePath: imagePath,
      executableSha256: imageHash,
    };
    writeAtomicNew(fs, path.join(prepared.runDirectory, 'runtime-smoke-process.json'), stableJson(processInfo) + '\n');

    const remainingMs = Math.max(0, launchDeadline - Date.now());
    const completed = await raceTimeout(exited, remainingMs, null);
    if (!completed) {
      processInfo.terminationConfirmed = await stopOwnedChild(child, exited, options.killGraceMs || 5000);
      return fail('watchdog_timeout', 'Player exceeded its bounded RuntimeSmoke deadline.', processInfo);
    }
    if (spawnError) return fail('player_process_error', spawnError.message, processInfo);

    let evidence;
    try {
      const launchPath = path.join(prepared.runDirectory, 'runtime-smoke-launch.json');
      const armedPath = path.join(prepared.runDirectory, 'runtime-smoke-armed.json');
      const transcriptPath = path.join(prepared.runDirectory, 'transcript.jsonl');
      const resultPath = path.join(prepared.runDirectory, 'result.json');
      const persistedLaunch = JSON.parse(fs.readFileSync(launchPath, 'utf8'));
      const armed = JSON.parse(fs.readFileSync(armedPath, 'utf8'));
      const result = JSON.parse(fs.readFileSync(resultPath, 'utf8'));
      const transcriptText = fs.readFileSync(transcriptPath, 'utf8');
      if (!transcriptText.endsWith('\n')) throw Object.assign(new Error('Transcript ends with an incomplete JSONL record.'), { code: 'transcript_truncated' });
      const transcript = transcriptText.split(/\r?\n/).filter(Boolean).map((line) => JSON.parse(line));
      const logStat = fs.statSync(prepared.logPath);
      evidence = {
        launch: persistedLaunch,
        armed,
        transcript,
        result,
        process: { processId: child.pid, processStartUtc: processInfo.processStartUtc,
          exitedUtc: completed.exitedUtc, exitCode: completed.code,
          executablePath: prepared.envelope.executablePath, executableSha256: prepared.envelope.executableSha256 },
        log: { path: prepared.logPath, length: logStat.size, lastWriteUtc: logStat.mtime.toISOString() },
      };
    } catch (error) {
      return fail(error.code || 'evidence_invalid', `RuntimeSmoke evidence could not be read: ${error.message}`, processInfo);
    }

    const validation = validateRuntimeSmokeEvidence(evidence, prepared.envelope);
    const watch = { schemaVersion: 1, state: validation.accepted ? 'passed' : 'failed',
      failureCode: validation.accepted ? null : validation.code, diagnostic: validation.message || null,
      runId: prepared.envelope.runId, correlationId: prepared.envelope.correlationId,
      processId: child.pid, processStartUtc: processInfo.processStartUtc, exitedUtc: completed.exitedUtc,
      exitCode: completed.code, terminalSequence: validation.accepted ? validation.terminal.sequence : null,
      observedUtc: new Date().toISOString() };
    try { writeAtomicNew(fs, path.join(prepared.runDirectory, 'runtime-smoke-watch.json'), stableJson(watch) + '\n'); }
    catch (error) { return fail('watch_record_write_failed', error.message, processInfo); }
    return { ...watch, ...validation, runDirectory: prepared.runDirectory };
  } catch (error) {
    const terminationConfirmed = child && child.pid && !exitRecord
      ? await stopOwnedChild(child, exited, options.killGraceMs || 5000) : (exitRecord ? true : null);
    const code = error.code || (child ? 'runner_error' : 'player_spawn_failed');
    return fail(code, error.message, child && child.pid ? { processId: child.pid, terminationConfirmed } : null);
  }
}

function raceTimeout(promise, milliseconds, fallback) {
  let timer;
  return Promise.race([promise, new Promise((resolve) => { timer = setTimeout(() => resolve(fallback), milliseconds); })])
    .finally(() => clearTimeout(timer));
}
async function stopOwnedChild(child, exited, graceMs) {
  if (!child || !child.pid) return true;
  try { child.kill(); } catch (_) { /* rely on the bounded exit observation below */ }
  if (!exited) return false;
  return (await raceTimeout(exited, graceMs, null)) !== null;
}
function cleanText(value) { return String(value || '').replace(/[\r\n]/g, ' ').slice(0, 512); }

function observeProcessIdentity(processId, timeoutMs = 10000) {
  if (process.platform !== 'win32')
    throw Object.assign(new Error('OS process image observation is currently supported only on Windows.'), { code: 'process_identity_unavailable' });
  const script = "$p=Get-Process -Id ([int]$env:ATH_RUNTIME_SMOKE_PROCESS_ID) -ErrorAction Stop; [pscustomobject]@{executablePath=$p.Path;processStartUtc=$p.StartTime.ToUniversalTime().ToString('o')} | ConvertTo-Json -Compress";
  const output = require('node:child_process').execFileSync('powershell.exe',
    ['-NoProfile', '-NonInteractive', '-Command', script], { encoding: 'utf8', windowsHide: true,
      timeout: Math.max(1, timeoutMs), killSignal: 'SIGTERM',
      stdio: ['ignore', 'pipe', 'pipe'], env: { ...process.env, ATH_RUNTIME_SMOKE_PROCESS_ID: String(processId) } });
  const identity = JSON.parse(output);
  if (!identity || typeof identity.executablePath !== 'string' || typeof identity.processStartUtc !== 'string')
    throw Object.assign(new Error('Operating system returned incomplete process identity.'), { code: 'process_identity_unavailable' });
  return identity;
}

function safeId(value) {
  return typeof value === 'string' && value.length > 0 && value.length <= 160 && value !== '.' && value !== '..' &&
    /^[A-Za-z0-9._-]+$/.test(value);
}
function hashFile(fs, crypto, filePath) {
  const hash = crypto.createHash('sha256');
  hash.update(fs.readFileSync(filePath));
  return hash.digest('hex');
}
function stableJson(value) {
  if (Array.isArray(value)) return `[${value.map(stableJson).join(',')}]`;
  if (value && typeof value === 'object') {
    return `{${Object.keys(value).filter((key) => value[key] !== undefined).sort()
      .map((key) => `${JSON.stringify(key)}:${stableJson(value[key])}`).join(',')}}`;
  }
  return JSON.stringify(value);
}
function createBuildToken(envelope, crypto = cryptoDefault) {
  const payload = { ...envelope, buildToken: null };
  return crypto.createHash('sha256').update(stableJson(payload), 'utf8').digest('hex');
}
function sameOptionalIdentity(actual, expected) {
  return (actual == null || actual === '') && (expected == null || expected === '') ? true : actual === expected;
}
function hashBuildEvidence(crypto, evidence) {
  const projection = {
    schemaVersion: evidence.schemaVersion,
    artifactPath: evidence.artifactPath,
    artifactSha256: evidence.artifactSha256,
    revision: evidence.revision,
    version: evidence.version,
    profileHash: evidence.profileHash,
    contentHash: evidence.contentHash,
    backend: evidence.backend,
    stripping: evidence.stripping,
    lane: evidence.lane,
  };
  return crypto.createHash('sha256').update(stableJson(projection), 'utf8').digest('hex');
}
function isValidSeed(seed) {
  if (Number.isSafeInteger(seed)) return seed >= 0;
  return typeof seed === 'string' && seed.length > 0 && seed.length <= 128 && /^[\p{L}\p{N}._-]+$/u.test(seed);
}

function normalizedPath(value) { return path.resolve(value).replace(/[\\/]+$/, '') || path.parse(path.resolve(value)).root; }
function pathEquals(left, right) {
  if (typeof left !== 'string' || typeof right !== 'string') return false;
  const comparison = process.platform === 'win32' ? (s) => s.toLowerCase() : (s) => s;
  return comparison(normalizedPath(left)) === comparison(normalizedPath(right));
}
function isWithin(candidate, root) {
  const c = normalizedPath(candidate);
  const r = normalizedPath(root);
  const relative = path.relative(r, c);
  return relative === '' || (!relative.startsWith(`..${path.sep}`) && relative !== '..' && !path.isAbsolute(relative));
}
function overlaps(left, right) { return isWithin(left, right) || isWithin(right, left); }
function assertNoSymlinkPath(fs, target, root) {
  const relative = path.relative(root, target);
  if (relative.startsWith('..') || path.isAbsolute(relative))
    throw Object.assign(new Error('Path escapes selected root.'), { code: 'artifact_root_escape' });
  let current = root;
  for (const part of relative.split(path.sep).filter(Boolean)) {
    current = path.join(current, part);
    if (fs.existsSync(current) && fs.lstatSync(current).isSymbolicLink())
      throw Object.assign(new Error('Symbolic links are not allowed in RuntimeSmoke artifact paths.'), { code: 'artifact_root_escape' });
  }
}
function writeAtomic(fs, destination, content) {
  const temp = `${destination}.tmp-${process.pid}-${Date.now()}`;
  const descriptor = fs.openSync(temp, 'wx');
  try {
    fs.writeFileSync(descriptor, content, 'utf8');
    fs.fsyncSync(descriptor);
  } finally { fs.closeSync(descriptor); }
  fs.renameSync(temp, destination);
}

/** Publish a file without replacing an existing path, including under concurrent writers. */
function writeAtomicNew(fs, destination, content) {
  const temp = `${destination}.tmp-${process.pid}-${Date.now()}-${cryptoDefault.randomBytes(6).toString('hex')}`;
  let ownsTemp = false;
  try {
    const descriptor = fs.openSync(temp, 'wx');
    ownsTemp = true;
    try {
      fs.writeFileSync(descriptor, content, 'utf8');
      fs.fsyncSync(descriptor);
    } finally { fs.closeSync(descriptor); }
    // Hard-link publication is atomic and fails with EEXIST instead of overwriting evidence.
    fs.linkSync(temp, destination);
  } finally {
    if (ownsTemp) {
      try { fs.unlinkSync(temp); } catch (_) { /* only this invocation's unique temp is eligible */ }
    }
  }
}

module.exports = { validateRuntimeSmokeEvidence, prepareRuntimeSmokeRun, createRuntimeSmokeLaunch, launchRuntimeSmokeProcess, observeProcessIdentity, createBuildToken };
