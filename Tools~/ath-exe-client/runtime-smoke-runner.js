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
  const continuationMatches = launch.continuation == null
    ? armed.continuation == null
    : stableJson(armed.continuation) === stableJson(launch.continuation);
  if (!identityMatches || !continuationMatches) return invalid('provenance_mismatch', 'Player armed record does not match the launch envelope and watched process.');

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
  let previousTime = launchTime;
  for (let index = 0; index < evidence.transcript.length; index++) {
    const record = evidence.transcript[index];
    if (!record || record.sequence !== index)
      return invalid('sequence_invalid', 'Transcript sequence must be contiguous and start at zero.');
    if (record.correlationId !== launch.correlationId || record.workflowId !== launch.workflowId || record.scriptHash !== launch.scriptHash ||
        !sameOptionalIdentity(record.scenario, launch.scenario) || !sameOptionalIdentity(record.seed, launch.seed))
      return invalid('identity_mismatch', 'Transcript records must use the launch correlation, workflow, script hash, scenario, and seed.');
    const timestamp = Date.parse(record.timestampUtc);
    const beforeProcess = index === 0 && record.type === 'armed';
    if (!Number.isFinite(timestamp) || timestamp < launchTime || timestamp < previousTime ||
        (!beforeProcess && timestamp < processTime) || (beforeProcess && timestamp > processTime) || timestamp > exitTime)
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
    'runId', 'correlationId', 'workflowId', 'canonicalScriptHash', 'target', 'mode'];
  for (const key of required) {
    if (options[key] == null || options[key] === '')
      throw Object.assign(new Error(`${key} is required`), { code: 'launch_input_invalid' });
  }
  if (!safeId(options.runId) || !safeId(options.correlationId) || !HEX_256.test(options.canonicalScriptHash) || options.scriptHash != null ||
      !Number.isInteger(options.timeoutSeconds) || options.timeoutSeconds < 1 || options.timeoutSeconds > 86400 ||
      (options.seed != null && !isValidSeed(options.seed)) ||
      !['development', 'smoke_harness'].includes(options.target) || options.mode !== 'fsd')
    throw Object.assign(new Error('Launch identity, canonical workflow hash, target, mode, or deadline is invalid.'), { code: 'launch_input_invalid' });
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

  const continuation = options.continuation || null;
  if (continuation && (!safeId(continuation.id) || !safeId(continuation.stageId) ||
      !Number.isInteger(continuation.stageIndex) || !Number.isInteger(continuation.stageCount) ||
      continuation.stageCount < 2 || continuation.stageIndex < 0 || continuation.stageIndex >= continuation.stageCount ||
      continuation.profileAction !== (continuation.stageIndex === 0 ? 'create' : 'load') || !safeId(continuation.profileId)))
    throw Object.assign(new Error('Continuation launch context is malformed.'), { code: 'continuation_invalid' });
  const nonceBytes = (options.randomBytes || crypto.randomBytes)(32);
  if (!Buffer.isBuffer(nonceBytes) || nonceBytes.length !== 32)
    throw Object.assign(new Error('Nonce provider must return exactly 256 bits.'), { code: 'launch_input_invalid' });

  if (options.runId === '.' || options.runId === '..')
    throw Object.assign(new Error('Run ID cannot be a path segment.'), { code: 'launch_input_invalid' });
  const runDirectory = path.resolve(outputRoot, options.runId);
  if (!isWithin(runDirectory, outputRoot) || pathEquals(runDirectory, outputRoot) || !pathEquals(path.dirname(runDirectory), outputRoot))
    throw Object.assign(new Error('Run ID must resolve to one direct child of the output root.'), { code: 'artifact_root_escape' });
  const logPath = path.join(runDirectory, 'logs', 'player.log');
  assertWindowsPlayerArtifactPaths(runDirectory, logPath);
  let created = false;
  try { fs.mkdirSync(runDirectory); created = true; }
  catch (error) {
    if (error && error.code === 'EEXIST')
      throw Object.assign(new Error('Run directory already exists; existing artifacts are never cleared.'), { code: 'run_directory_collision' });
    throw error;
  }
  if (!created) throw Object.assign(new Error('Run directory could not be claimed.'), { code: 'run_directory_collision' });
  for (const folder of ['scratch', 'logs', 'captures']) fs.mkdirSync(path.join(runDirectory, folder));

  const buildEvidenceHash = hashBuildEvidence(crypto, buildEvidence);
  const startedUtc = (options.now ? options.now() : new Date()).toISOString();
  const buildEvidenceSnapshotPath = path.join(runDirectory, 'build-evidence.json');
  writeAtomicNew(fs, buildEvidenceSnapshotPath, stableJson(buildEvidence) + '\n');
  const envelope = {
    schemaVersion: 1,
    state: 'awaiting_player',
    runId: options.runId,
    correlationId: options.correlationId,
    workflowId: options.workflowId,
    scriptHash: options.canonicalScriptHash,
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
    continuation,
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
  const armedPrelude = {
    schemaVersion: 1,
    sequence: 0,
    type: 'armed',
    timestampUtc: startedUtc,
    workflowId: envelope.workflowId,
    scriptHash: envelope.scriptHash,
    correlationId: envelope.correlationId,
    scenario: envelope.scenario,
    seed: envelope.seed,
    status: 'armed',
  };
  writeAtomicNew(fs, path.join(runDirectory, 'transcript.jsonl'), stableJson(armedPrelude) + '\n');

  const args = ['-runtimeSmoke', options.workflowId,
    '-runtimeSmokeCorrelation', options.correlationId,
    '-runtimeSmokeArtifactRoot', runDirectory,
    '-runtimeSmokeNonce', envelope.nonce,
    '-runtimeSmokeBuildToken', envelope.buildToken,
    ...(continuation ? ['-runtimeSmokeContinuation', continuation.id,
      '-runtimeSmokeContinuationStage', continuation.stageId,
      '-runtimeSmokeContinuationIndex', String(continuation.stageIndex),
      '-runtimeSmokeContinuationCount', String(continuation.stageCount),
      '-runtimeSmokeContinuationAction', continuation.profileAction,
      '-runtimeSmokeContinuationProfile', continuation.profileId,
      ...(continuation.profileAction === 'load' ? ['-autoload', continuation.profileId] : [])] : []),
    '-logFile', logPath];
  return { envelope, args, runDirectory, logPath };
}

/** Launch one player and accept it only after clean exit plus fully bound evidence. */
async function launchRuntimeSmokeProcess(options = {}) {
  const fs = options.fs || fsDefault;
  const prepared = createRuntimeSmokeLaunch(options);
  const spawnProcess = options.spawnProcess || require('node:child_process').spawn;
  const stdoutPath = path.join(prepared.runDirectory, 'logs', 'player.stdout.txt');
  const stderrPath = path.join(prepared.runDirectory, 'logs', 'player.stderr.txt');
  let child;
  let stdoutTail = '';
  let stderrTail = '';
  let processExitRecordPath = null;
  let exitEvidenceWritten = false;
  let exitEvidenceRecord = null;
  const captureTail = (current, chunk) => (current + chunk.toString('utf8')).slice(-8192);
  const flushCapturedOutput = () => {
    try { writeAtomicNew(fs, stdoutPath, stdoutTail); } catch (_) { /* primary process failure remains authoritative */ }
    try { writeAtomicNew(fs, stderrPath, stderrTail); } catch (_) { /* primary process failure remains authoritative */ }
  };
  const writeExitEvidence = (state, completion = exitRecord, processInfo = null, failureCode = null, failureDiagnostic = null) => {
    if (exitEvidenceWritten) return exitEvidenceRecord;
    flushCapturedOutput();
    const exitCode = completion ? completion.code : null;
    const signal = completion ? completion.signal : null;
    const exitCodeUnavailableReason = !completion ? 'exit_event_not_observed' : exitCode === null ? 'process_terminated_by_signal' : null;
    const record = { schemaVersion: 1, state, runId: prepared.envelope.runId,
      correlationId: prepared.envelope.correlationId,
      processId: processInfo && processInfo.processId || (child && child.pid) || null,
      processStartUtc: processInfo && processInfo.processStartUtc || null,
      exitedUtc: completion && completion.exitedUtc || null,
      exitCode, exitCodeUnavailableReason, signal,
      stdoutPath, stdoutBytes: Buffer.byteLength(stdoutTail), stderrPath, stderrBytes: Buffer.byteLength(stderrTail),
      stderrTail: stderrTail.slice(-2048),
      nativeLaunchError: spawnError ? cleanText(spawnError.message) :
        (failureCode === 'player_spawn_failed' || failureCode === 'player_process_error' ? cleanText(failureDiagnostic) : null),
      diagnostic: null };
    processExitRecordPath = path.join(prepared.runDirectory, 'runtime-smoke-process-exit.json');
    try { writeAtomicNew(fs, processExitRecordPath, stableJson(record) + '\n'); exitEvidenceWritten = true; exitEvidenceRecord = record; }
    catch (_) { /* do not replace the launch outcome with a secondary evidence-write failure */ }
    return record;
  };
  const fail = (code, message, processInfo = null) => {
    const exitEvidence = writeExitEvidence('failed', exitRecord, processInfo, code, message);
    let diagnostic = cleanText(message);
    if (exitEvidence && (exitEvidence.exitCode !== null || exitEvidence.signal)) {
      diagnostic = cleanText(`${diagnostic} (exitCode=${exitEvidence.exitCode === null ? 'null' : exitEvidence.exitCode}, signal=${exitEvidence.signal || 'none'})`);
    }
    if (stderrTail.trim()) diagnostic = cleanText(`${diagnostic}; stderr: ${stderrTail.trim().slice(-2048)}`);
    const watch = { schemaVersion: 1, state: 'failed', failureCode: code, diagnostic,
      runId: prepared.envelope.runId, correlationId: prepared.envelope.correlationId,
      processId: processInfo && processInfo.processId,
      terminationConfirmed: processInfo && processInfo.terminationConfirmed,
      processState: processInfo && processInfo.terminationConfirmed === false ? 'termination_unconfirmed' : null,
      exitedUtc: exitEvidence && exitEvidence.exitedUtc,
      exitCode: exitEvidence ? exitEvidence.exitCode : null,
      exitCodeUnavailableReason: exitEvidence ? exitEvidence.exitCodeUnavailableReason : 'exit_event_not_observed',
      signal: exitEvidence && exitEvidence.signal,
      stdoutPath, stderrPath, processExitRecordPath,
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
      stdio: ['ignore', 'pipe', 'pipe'],
      windowsHide: false,
    });
    if (child.stdout && typeof child.stdout.on === 'function') child.stdout.on('data', (chunk) => { stdoutTail = captureTail(stdoutTail, chunk); });
    if (child.stderr && typeof child.stderr.on === 'function') child.stderr.on('data', (chunk) => { stderrTail = captureTail(stderrTail, chunk); });
    spawned = new Promise((resolve) => {
      child.once('spawn', () => resolve(true));
      child.once('error', (error) => { spawnError = error; resolve(false); });
    });
    exited = new Promise((resolve) => {
      child.once('exit', (code, signal) => {
        exitRecord = { code, signal, exitedUtc: new Date().toISOString() };
      });
      child.once('close', (code, signal) => {
        if (!exitRecord) exitRecord = { code, signal, exitedUtc: new Date().toISOString() };
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

    const exitEvidence = writeExitEvidence('exited', completed, processInfo);
    processInfo.exitCode = completed.code;
    processInfo.exitCodeUnavailableReason = exitEvidence && exitEvidence.exitCodeUnavailableReason;
    processInfo.signal = completed.signal;
    processInfo.exitedUtc = completed.exitedUtc;

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
    return { ...watch, ...validation, runDirectory: prepared.runDirectory, evidence };
  } catch (error) {
    const terminationConfirmed = child && child.pid && !exitRecord
      ? await stopOwnedChild(child, exited, options.killGraceMs || 5000) : (exitRecord ? true : null);
    const code = error.code || (child ? 'runner_error' : 'player_spawn_failed');
    return fail(code, error.message, child && child.pid ? { processId: child.pid, terminationConfirmed } : null);
  }
}

/** Copy and attest an exact built-player tree for a reusable suite gate. */
function prepareRuntimeSmokeArtifactCopy(options = {}) {
  const fs = options.fs || fsDefault;
  const crypto = options.crypto || cryptoDefault;
  const hostRoot = fs.realpathSync(options.hostRoot);
  const sourceArtifactRoot = fs.realpathSync(options.sourceArtifactRoot);
  const sourceExecutablePath = fs.realpathSync(options.sourceExecutablePath);
  const authoredRoot = fs.realpathSync(options.authoredRoot);
  const buildEvidencePath = fs.realpathSync(options.buildEvidencePath);
  const artifactCopyDirectory = path.resolve(options.artifactCopyDirectory || '');
  const copyId = options.copyId;
  if (!safeId(copyId) || !fs.statSync(hostRoot).isDirectory() || !fs.statSync(sourceArtifactRoot).isDirectory() ||
      !isWithin(sourceArtifactRoot, hostRoot) || !isWithin(sourceExecutablePath, sourceArtifactRoot) ||
      !isWithin(buildEvidencePath, hostRoot) || overlaps(sourceArtifactRoot, authoredRoot) ||
      overlaps(buildEvidencePath, sourceArtifactRoot) || !isWithin(artifactCopyDirectory, hostRoot) ||
      pathEquals(artifactCopyDirectory, hostRoot) || overlaps(artifactCopyDirectory, sourceArtifactRoot) ||
      overlaps(artifactCopyDirectory, authoredRoot) || overlaps(artifactCopyDirectory, buildEvidencePath))
    throw Object.assign(new Error('Artifact source, build evidence, authoring inputs, and copy directory must be contained and disjoint.'), { code: 'artifact_root_escape' });
  assertNoSymlinkPath(fs, sourceArtifactRoot, hostRoot);
  assertNoSymlinkPath(fs, sourceExecutablePath, hostRoot);
  assertNoSymlinkPath(fs, buildEvidencePath, hostRoot);
  assertNoSymlinkPath(fs, artifactCopyDirectory, hostRoot);
  if (!fs.statSync(sourceExecutablePath).isFile())
    throw Object.assign(new Error('The selected source executable must be a file inside the artifact tree.'), { code: 'executable_invalid' });
  if (fs.existsSync(artifactCopyDirectory))
    throw Object.assign(new Error('Artifact-copy destination already exists; existing build evidence is never cleared.'), { code: 'artifact_copy_collision' });

  const sourceTree = hashArtifactTree(fs, crypto, sourceArtifactRoot);
  const sourceExecutableSha256 = hashFile(fs, crypto, sourceExecutablePath);
  const sourceEvidenceBytes = fs.readFileSync(buildEvidencePath);
  let sourceBuildEvidence;
  try { sourceBuildEvidence = JSON.parse(sourceEvidenceBytes.toString('utf8')); }
  catch (error) { throw Object.assign(new Error(`Build evidence is not valid JSON: ${error.message}`), { code: 'build_provenance_mismatch' }); }
  if (!sourceBuildEvidence || sourceBuildEvidence.schemaVersion !== 1 || !pathEquals(sourceBuildEvidence.artifactPath, sourceExecutablePath) ||
      sourceBuildEvidence.artifactSha256 !== sourceExecutableSha256 || !sourceBuildEvidence.revision || !sourceBuildEvidence.version ||
      !HEX_256.test(sourceBuildEvidence.profileHash || '') || !HEX_256.test(sourceBuildEvidence.contentHash || '') ||
      !sourceBuildEvidence.backend || !sourceBuildEvidence.stripping || !['development', 'smoke_harness'].includes(sourceBuildEvidence.lane))
    throw Object.assign(new Error('Build evidence does not bind the exact source executable and complete build provenance.'), { code: 'build_provenance_mismatch' });
  if (options.target && sourceBuildEvidence.lane !== options.target)
    throw Object.assign(new Error('Build evidence lane does not match the suite target.'), { code: 'build_provenance_mismatch' });

  const copyParent = path.dirname(artifactCopyDirectory);
  if (!fs.existsSync(copyParent) || !fs.statSync(copyParent).isDirectory())
    throw Object.assign(new Error('Artifact-copy parent must be an existing directory.'), { code: 'artifact_root_missing' });
  fs.mkdirSync(artifactCopyDirectory);
  const artifactRoot = path.join(artifactCopyDirectory, 'payload');
  fs.cpSync(sourceArtifactRoot, artifactRoot, { recursive: true, errorOnExist: true, force: false, dereference: false });
  const relativeExecutable = path.relative(sourceArtifactRoot, sourceExecutablePath);
  const executablePath = path.resolve(artifactRoot, relativeExecutable);
  assertNoSymlinkPath(fs, executablePath, artifactRoot);
  const copiedTree = hashArtifactTree(fs, crypto, artifactRoot);
  const copyExecutableSha256 = hashFile(fs, crypto, executablePath);
  if (sourceTree.sha256 !== copiedTree.sha256 || sourceExecutableSha256 !== copyExecutableSha256)
    throw Object.assign(new Error('Fresh artifact copy differs from the exact source build tree.'), { code: 'artifact_copy_mismatch' });

  const copiedUtc = (options.now ? options.now() : new Date()).toISOString();
  const attestation = {
    schemaVersion: 1,
    copyId,
    copiedUtc,
    sourceArtifactRoot,
    sourceExecutablePath,
    sourceTreeSha256: sourceTree.sha256,
    sourceFileCount: sourceTree.fileCount,
    sourceBytes: sourceTree.bytes,
    sourceExecutableSha256,
    sourceExecutableTimestampUtc: fs.statSync(sourceExecutablePath).mtime.toISOString(),
    buildRevision: sourceBuildEvidence.revision,
    buildVersion: sourceBuildEvidence.version,
    profileHash: sourceBuildEvidence.profileHash,
    contentHash: sourceBuildEvidence.contentHash,
    backend: sourceBuildEvidence.backend,
    stripping: sourceBuildEvidence.stripping,
    lane: sourceBuildEvidence.lane,
    sourceBuildEvidencePath: buildEvidencePath,
    sourceBuildEvidenceSha256: crypto.createHash('sha256').update(sourceEvidenceBytes).digest('hex'),
    copiedArtifactRoot: artifactRoot,
    copiedExecutablePath: executablePath,
    copyTreeSha256: copiedTree.sha256,
    copyFileCount: copiedTree.fileCount,
    copyBytes: copiedTree.bytes,
    copyExecutableSha256,
  };
  const attestationPath = path.join(artifactCopyDirectory, 'runtime-smoke-copy-attestation.json');
  const copiedBuildEvidencePath = path.join(artifactCopyDirectory, 'build-evidence.json');
  const copiedBuildEvidence = { ...sourceBuildEvidence, artifactPath: executablePath };
  writeAtomicNew(fs, copiedBuildEvidencePath, stableJson(copiedBuildEvidence) + '\n');
  attestation.copiedBuildEvidenceSha256 = hashFile(fs, crypto, copiedBuildEvidencePath);
  writeAtomicNew(fs, attestationPath, stableJson(attestation) + '\n');
  return {
    copyId,
    sourceArtifactRoot,
    sourceExecutablePath,
    sourceBuildEvidencePath: buildEvidencePath,
    artifactRoot,
    executablePath,
    buildEvidencePath: copiedBuildEvidencePath,
    attestationPath,
    attestationSha256: hashFile(fs, crypto, attestationPath),
    attestation,
  };
}

/** Execute a host-compiled suite against one fresh, attested copy of a built artifact. */
async function runRuntimeSmokeSuite(spec, dependencies = {}) {
  const fs = dependencies.fs || fsDefault;
  const crypto = dependencies.crypto || cryptoDefault;
  const now = dependencies.now || (() => new Date());
  const launch = dependencies.launch || launchRuntimeSmokeProcess;
  const normalized = validateRuntimeSmokeSuiteSpec(spec, fs);
  const startedUtc = now().toISOString();
  const startedMs = Date.parse(startedUtc);
  const deadlineMs = startedMs + normalized.maximumSuiteSeconds * 1000;
  try { fs.mkdirSync(normalized.outputDirectory); }
  catch (error) {
    if (error && error.code === 'EEXIST') throw Object.assign(new Error('Build-gate output directory already exists; evidence is never overwritten.'), { code: 'gate_output_collision' });
    throw error;
  }
  const runsRoot = path.join(normalized.outputDirectory, 'runs');
  fs.mkdirSync(runsRoot);
  const entries = [];
  let artifactCopy = null;
  let failureCode = null;
  let diagnostic = null;
  try {
    artifactCopy = prepareRuntimeSmokeArtifactCopy({
      hostRoot: normalized.hostRoot,
      authoredRoot: normalized.authoredRoot,
      sourceArtifactRoot: normalized.sourceArtifactRoot,
      sourceExecutablePath: normalized.sourceExecutablePath,
      buildEvidencePath: normalized.buildEvidencePath,
      target: normalized.target,
      artifactCopyDirectory: path.join(normalized.outputDirectory, 'artifact-copy'),
      copyId: normalized.copyId,
      fs,
      crypto,
      now,
    });
  } catch (error) {
    failureCode = error.code || 'artifact_copy_failed';
    diagnostic = cleanText(error.message);
  }

  for (const entry of normalized.entries) {
    if (failureCode) {
      entries.push(skippedSuiteEntry(entry, failureCode, diagnostic));
      continue;
    }
    const beforeRun = verifyRuntimeSmokeArtifactCopy(artifactCopy, fs, crypto);
    if (!beforeRun.accepted) {
      failureCode = 'artifact_copy_changed';
      diagnostic = beforeRun.diagnostic;
      entries.push(skippedSuiteEntry(entry, failureCode, diagnostic));
      continue;
    }
    const remainingSeconds = Math.floor((deadlineMs - Date.parse(now().toISOString())) / 1000);
    if (remainingSeconds < 1) {
      entries.push(skippedSuiteEntry(entry, 'suite_deadline_exceeded', 'Maximum suite deadline elapsed before this workflow started.'));
      continue;
    }
    const profileBytes = entry.continuation ? (dependencies.randomBytes || crypto.randomBytes)(16) : null;
    if (profileBytes && (!Buffer.isBuffer(profileBytes) || profileBytes.length !== 16)) {
      entries.push(skippedSuiteEntry(entry, 'continuation_invalid', 'Continuation profile provider must return exactly 128 random bits.'));
      continue;
    }
    const continuationProfileId = profileBytes ? `athrs_${profileBytes.toString('hex')}` : null;
    const stages = entry.continuation ? entry.continuation.stages : [entry];
    const stageResults = [];
    for (let stageIndex = 0; stageIndex < stages.length; stageIndex++) {
      const stage = stages[stageIndex];
      if (stageIndex > 0 && !stageResults[stageIndex - 1].accepted) break;
      const stageArtifactCheck = verifyRuntimeSmokeArtifactCopy(artifactCopy, fs, crypto);
      if (!stageArtifactCheck.accepted) {
        stageResults.push({ stageId: stage.stageId || null, workflowId: stage.workflowId,
          canonicalScriptHash: stage.canonicalScriptHash, runId: stage.runId, correlationId: stage.correlationId,
          profileAction: entry.continuation ? stage.profileAction : null,
          profileIdSha256: continuationProfileId ? crypto.createHash('sha256').update(continuationProfileId).digest('hex') : null,
          accepted: false, state: 'failed', failureCode: 'artifact_copy_changed',
          diagnostic: stageArtifactCheck.diagnostic });
        break;
      }
      const stageRemainingSeconds = Math.floor((deadlineMs - Date.parse(now().toISOString())) / 1000);
      if (stageRemainingSeconds < 1) {
        stageResults.push({ stageId: stage.stageId || null, workflowId: stage.workflowId,
          canonicalScriptHash: stage.canonicalScriptHash, runId: stage.runId, correlationId: stage.correlationId,
          profileAction: entry.continuation ? stage.profileAction : null,
          profileIdSha256: continuationProfileId ? crypto.createHash('sha256').update(continuationProfileId).digest('hex') : null,
          accepted: false, state: 'skipped', failureCode: 'suite_deadline_exceeded',
          diagnostic: 'Maximum suite deadline elapsed before this process stage started.' });
        break;
      }
      const runStartedUtc = now().toISOString();
      const runOptions = {
        hostRoot: normalized.hostRoot,
        outputRoot: runsRoot,
        authoredRoot: normalized.authoredRoot,
        executablePath: artifactCopy.executablePath,
        buildEvidencePath: artifactCopy.buildEvidencePath,
        runId: stage.runId,
        correlationId: stage.correlationId,
        workflowId: stage.workflowId,
        canonicalScriptHash: stage.canonicalScriptHash,
        proposition: entry.proposition,
        requiredCapabilities: entry.requiredCapabilities.slice(),
        parityContract: entry.parityContract,
        target: normalized.target,
        mode: 'fsd',
        suiteId: normalized.suiteId,
        scenario: entry.scenario,
        seed: entry.seed,
        timeoutSeconds: Math.min(stage.timeoutSeconds, stageRemainingSeconds),
        requiredStepIds: stage.requiredStepIds.slice(),
        continuation: entry.continuation ? {
          id: entry.continuation.continuationId,
          stageId: stage.stageId,
          stageIndex,
          stageCount: stages.length,
          profileAction: stage.profileAction,
          profileId: continuationProfileId,
        } : null,
      };
      let execution;
      try { execution = await launch(runOptions); }
      catch (error) { execution = { accepted: false, state: 'failed', failureCode: error.code || 'runner_error', diagnostic: cleanText(error.message) }; }
      const runEndedUtc = now().toISOString();
      const runDirectory = execution && execution.runDirectory || path.join(runsRoot, stage.runId);
      stageResults.push({
        stageId: stage.stageId || null,
        workflowId: stage.workflowId,
        canonicalScriptHash: stage.canonicalScriptHash,
        correlationId: stage.correlationId,
        runId: stage.runId,
        profileAction: entry.continuation ? stage.profileAction : null,
        profileIdSha256: continuationProfileId ? crypto.createHash('sha256').update(continuationProfileId).digest('hex') : null,
        accepted: !!(execution && execution.accepted === true),
        state: execution && execution.state || 'failed',
        failureCode: execution && (execution.failureCode || (execution.accepted ? null : execution.code)) || null,
        diagnostic: execution && (execution.diagnostic || execution.message) || null,
        startedUtc: runStartedUtc,
        endedUtc: runEndedUtc,
        runDirectory,
        launchPath: path.join(runDirectory, 'runtime-smoke-launch.json'),
        resultPath: path.join(runDirectory, 'result.json'),
        transcriptPath: path.join(runDirectory, 'transcript.jsonl'),
        playerLogPath: path.join(runDirectory, 'logs', 'player.log'),
        runtimeSmokeEvidence: collectRuntimeSmokeRunEvidence(execution && execution.evidence, runDirectory, fs, crypto),
      });
      if (!stageResults[stageIndex].accepted) break;
    }
    const firstFailure = stageResults.find((stage) => !stage.accepted);
    while (stageResults.length < stages.length) {
      const stage = stages[stageResults.length];
      stageResults.push({ stageId: stage.stageId, workflowId: stage.workflowId, canonicalScriptHash: stage.canonicalScriptHash,
        runId: stage.runId, correlationId: stage.correlationId, profileAction: stage.profileAction,
        profileIdSha256: continuationProfileId ? crypto.createHash('sha256').update(continuationProfileId).digest('hex') : null,
        accepted: false, state: 'skipped', failureCode: 'continuation_stage_not_run',
        diagnostic: `Stage was not run because ${firstFailure && firstFailure.stageId || 'a prior stage'} did not pass.`,
        runDirectory: null, launchPath: null, resultPath: null, transcriptPath: null, playerLogPath: null });
    }
    const finalStage = stageResults[stageResults.length - 1];
    const firstStage = stages[0];
    const firstDirectory = path.join(runsRoot, firstStage.runId);
    entries.push({
      entryId: entry.entryId,
      required: entry.required,
      workflowId: firstStage.workflowId,
      canonicalScriptHash: firstStage.canonicalScriptHash,
      proposition: entry.proposition,
      requiredCapabilities: entry.requiredCapabilities.slice(),
      parityContract: entry.parityContract,
      correlationId: firstStage.correlationId,
      scenario: entry.scenario == null ? null : entry.scenario,
      seed: entry.seed == null ? null : String(entry.seed),
      target: normalized.target,
      mode: 'fsd',
      timeoutSeconds: finalStage ? Math.min(entry.timeoutSeconds, Math.max(1, remainingSeconds)) : entry.timeoutSeconds,
      accepted: stageResults.length === stages.length && stageResults.every((stage) => stage.accepted),
      state: stageResults.length === stages.length && stageResults.every((stage) => stage.accepted) ? 'passed' : 'failed',
      continuationId: entry.continuation ? entry.continuation.continuationId : null,
      stages: stageResults,
        failureCode: firstFailure && firstFailure.failureCode || null,
      diagnostic: firstFailure && firstFailure.diagnostic || null,
      startedUtc: stageResults[0] && stageResults[0].startedUtc || now().toISOString(),
      endedUtc: finalStage && finalStage.endedUtc || now().toISOString(),
      runDirectory: firstDirectory,
      launchPath: path.join(firstDirectory, 'runtime-smoke-launch.json'),
      resultPath: path.join(firstDirectory, 'result.json'),
      transcriptPath: path.join(firstDirectory, 'transcript.jsonl'),
      playerLogPath: path.join(firstDirectory, 'logs', 'player.log'),
    });
  }

  if (artifactCopy) {
    const afterRun = verifyRuntimeSmokeArtifactCopy(artifactCopy, fs, crypto);
    if (!afterRun.accepted) {
      failureCode = 'artifact_copy_changed';
      diagnostic = afterRun.diagnostic;
    }
  }
  const endedUtc = now().toISOString();
  const gate = createRuntimeSmokeBuildGate({
    suiteId: normalized.suiteId,
    target: normalized.target,
    startedUtc,
    endedUtc,
    maximumSuiteSeconds: normalized.maximumSuiteSeconds,
    artifactCopy,
    entries,
    failureCode,
    diagnostic,
  });
  const junit = serializeRuntimeSmokeBuildGateJUnit(gate);
  const gatePath = path.join(normalized.outputDirectory, 'runtime-smoke-gate.json');
  const junitPath = path.join(normalized.outputDirectory, 'junit.xml');
  writeAtomicNew(fs, gatePath, stableJson(gate) + '\n');
  writeAtomicNew(fs, junitPath, junit + '\n');
  return { ...gate, copyAttestation: artifactCopy && artifactCopy.attestation, junit, gatePath, junitPath };
}

function createRuntimeSmokeBuildGate({ suiteId, target, startedUtc, endedUtc, maximumSuiteSeconds, artifactCopy, entries, failureCode, diagnostic }) {
  const outcomes = entries || [];
  const requiredFailures = outcomes.filter((entry) => entry.required && !entry.accepted);
  const accepted = !failureCode && requiredFailures.length === 0;
  return {
    schemaVersion: 1,
    suiteId,
    target,
    state: accepted ? 'passed' : 'failed',
    accepted,
    failureCode: failureCode || (requiredFailures.length ? (requiredFailures[0].failureCode || 'required_workflow_failed') : null),
    diagnostic: diagnostic || (requiredFailures.length ? requiredFailures[0].diagnostic : null),
    startedUtc,
    endedUtc,
    maximumSuiteSeconds,
    artifactCopy: artifactCopy ? {
      copyId: artifactCopy.copyId,
      copiedUtc: artifactCopy.attestation.copiedUtc,
      sourceArtifactRoot: artifactCopy.sourceArtifactRoot,
      sourceExecutablePath: artifactCopy.sourceExecutablePath,
      sourceTreeSha256: artifactCopy.attestation.sourceTreeSha256,
      sourceFileCount: artifactCopy.attestation.sourceFileCount,
      sourceBytes: artifactCopy.attestation.sourceBytes,
      sourceExecutableSha256: artifactCopy.attestation.sourceExecutableSha256,
      sourceExecutableTimestampUtc: artifactCopy.attestation.sourceExecutableTimestampUtc,
      revision: artifactCopy.attestation.buildRevision,
      version: artifactCopy.attestation.buildVersion,
      profileHash: artifactCopy.attestation.profileHash,
      contentHash: artifactCopy.attestation.contentHash,
      backend: artifactCopy.attestation.backend,
      stripping: artifactCopy.attestation.stripping,
      lane: artifactCopy.attestation.lane,
      sourceBuildEvidencePath: artifactCopy.attestation.sourceBuildEvidencePath,
      sourceBuildEvidenceSha256: artifactCopy.attestation.sourceBuildEvidenceSha256,
      copiedBuildEvidencePath: artifactCopy.buildEvidencePath,
      copiedBuildEvidenceSha256: artifactCopy.attestation.copiedBuildEvidenceSha256,
      copiedArtifactRoot: artifactCopy.artifactRoot,
      copiedExecutablePath: artifactCopy.executablePath,
      copyTreeSha256: artifactCopy.attestation.copyTreeSha256,
      copyFileCount: artifactCopy.attestation.copyFileCount,
      copyBytes: artifactCopy.attestation.copyBytes,
      copyExecutableSha256: artifactCopy.attestation.copyExecutableSha256,
      attestationPath: artifactCopy.attestationPath,
      attestationSha256: artifactCopy.attestationSha256,
    } : null,
    entries: outcomes,
  };
}

function serializeRuntimeSmokeBuildGateJUnit(gate) {
  if (!gate || !Array.isArray(gate.entries)) throw Object.assign(new Error('A build-gate result with entries is required.'), { code: 'gate_result_invalid' });
  const requiredNonpasses = gate.entries.some((entry) => entry.required && !entry.accepted);
  const suiteFailureCase = gate.failureCode && !requiredNonpasses
    ? `<testcase classname="RuntimeSmokeBuildGate" name="${xmlEscape(gate.suiteId)}"><error type="${xmlEscape(gate.failureCode)}" message="${xmlEscape(gate.diagnostic || gate.failureCode)}"/></testcase>` : '';
  const cases = gate.entries.map((entry) => {
    const elapsed = Math.max(0, (Date.parse(entry.endedUtc) - Date.parse(entry.startedUtc)) / 1000 || 0).toFixed(3);
    const properties = [
      ['entryId', entry.entryId], ['required', String(!!entry.required)], ['state', entry.state],
      ['failureCode', entry.failureCode || ''], ['correlationId', entry.correlationId || ''],
      ['scriptHash', entry.canonicalScriptHash || ''], ['target', entry.target || ''], ['mode', entry.mode || ''],
      ['scenario', entry.scenario || ''], ['seed', entry.seed || ''], ['proposition', entry.proposition || ''],
      ['requiredCapabilities', (entry.requiredCapabilities || []).join(',')], ['parityContract', entry.parityContract || ''],
      ['runDirectory', entry.runDirectory || ''],
    ].map(([name, value]) => `<property name="${xmlEscape(name)}" value="${xmlEscape(value)}"/>`).join('');
    const body = `<properties>${properties}</properties>`;
    if (entry.accepted) return `<testcase classname="${xmlEscape(gate.suiteId)}" name="${xmlEscape(entry.workflowId)}" time="${elapsed}">${body}</testcase>`;
    if (!entry.required) return `<testcase classname="${xmlEscape(gate.suiteId)}" name="${xmlEscape(entry.workflowId)}" time="${elapsed}">${body}<skipped message="optional workflow did not pass"/></testcase>`;
    const code = entry.failureCode || gate.failureCode || 'required_workflow_failed';
    const text = xmlEscape(entry.diagnostic || code);
    const failure = /^assertion_|^predicate_/.test(code)
      ? `<failure type="${xmlEscape(code)}" message="${text}"/>`
      : `<error type="${xmlEscape(code)}" message="${text}"/>`;
    return `<testcase classname="${xmlEscape(gate.suiteId)}" name="${xmlEscape(entry.workflowId)}" time="${elapsed}">${body}${failure}</testcase>`;
  }).join('') + suiteFailureCase;
  const totalTests = gate.entries.length + (suiteFailureCase ? 1 : 0);
  const failures = gate.entries.filter((entry) => entry.required && !entry.accepted && /^assertion_|^predicate_/.test(entry.failureCode || gate.failureCode || '')).length;
  const errors = gate.entries.filter((entry) => entry.required && !entry.accepted && !/^assertion_|^predicate_/.test(entry.failureCode || gate.failureCode || '')).length;
  const skipped = gate.entries.filter((entry) => !entry.required && !entry.accepted).length;
  return `<testsuites tests="${totalTests}" failures="${failures}" errors="${errors + (suiteFailureCase ? 1 : 0)}" skipped="${skipped}"><testsuite name="${xmlEscape(gate.suiteId)}" tests="${totalTests}" failures="${failures}" errors="${errors + (suiteFailureCase ? 1 : 0)}" skipped="${skipped}">${cases}</testsuite></testsuites>`;
}

function validateRuntimeSmokeSuiteSpec(spec, fs) {
  if (!spec || spec.schemaVersion !== 1 || !isDocumentId(spec.suiteId) ||
      !['development', 'smoke_harness'].includes(spec.target) || !Number.isInteger(spec.maximumSuiteSeconds) ||
      spec.maximumSuiteSeconds < 1 || spec.maximumSuiteSeconds > 86400 || !Array.isArray(spec.entries) || spec.entries.length === 0)
    throw Object.assign(new Error('RuntimeSmoke suite spec requires schemaVersion 1, a stable suite ID, supported target, deadline, and entries.'), { code: 'suite_spec_invalid' });
  const hostRoot = fs.realpathSync(spec.hostRoot);
  const authoredRoot = fs.realpathSync(spec.authoredRoot);
  const sourceArtifactRoot = fs.realpathSync(spec.sourceArtifactRoot);
  const sourceExecutablePath = fs.realpathSync(spec.sourceExecutablePath);
  const buildEvidencePath = fs.realpathSync(spec.buildEvidencePath);
  const outputDirectory = path.resolve(spec.outputDirectory || '');
  if (!isWithin(authoredRoot, hostRoot) || !isWithin(sourceArtifactRoot, hostRoot) || !isWithin(sourceExecutablePath, sourceArtifactRoot) ||
      !isWithin(buildEvidencePath, hostRoot) || !isWithin(outputDirectory, hostRoot) || pathEquals(outputDirectory, hostRoot) ||
      overlaps(outputDirectory, authoredRoot) || overlaps(outputDirectory, sourceArtifactRoot) || overlaps(outputDirectory, buildEvidencePath) ||
      overlaps(outputDirectory, sourceExecutablePath))
    throw Object.assign(new Error('Suite inputs and output must remain contained in the selected host and disjoint from authored/build inputs.'), { code: 'artifact_root_escape' });
  assertNoSymlinkPath(fs, authoredRoot, hostRoot);
  assertNoSymlinkPath(fs, sourceArtifactRoot, hostRoot);
  assertNoSymlinkPath(fs, sourceExecutablePath, hostRoot);
  assertNoSymlinkPath(fs, buildEvidencePath, hostRoot);
  assertNoSymlinkPath(fs, outputDirectory, hostRoot);
  const ids = new Set(); const scripts = new Set(); const runs = new Set(); const correlations = new Set();
  const entries = spec.entries.map((entry) => {
    if (!entry || !safeId(entry.entryId) || ids.has(entry.entryId) || !isDocumentId(entry.workflowId) || scripts.has(entry.workflowId) ||
        typeof entry.required !== 'boolean' || entry.target !== spec.target || entry.mode !== 'fsd' ||
        !HEX_256.test(entry.canonicalScriptHash || '') || !safeId(entry.runId) || runs.has(entry.runId) ||
        !safeId(entry.correlationId) || correlations.has(entry.correlationId) || !Number.isInteger(entry.timeoutSeconds) ||
      entry.timeoutSeconds < 1 || entry.timeoutSeconds > 86400 || !Array.isArray(entry.requiredStepIds) || entry.requiredStepIds.length === 0 ||
        new Set(entry.requiredStepIds).size !== entry.requiredStepIds.length || entry.requiredStepIds.some((id) => !safeId(id)) ||
        (entry.seed != null && !isValidSeed(entry.seed)) ||
        (entry.requiredCapabilities != null && (!Array.isArray(entry.requiredCapabilities) ||
          new Set(entry.requiredCapabilities).size !== entry.requiredCapabilities.length || entry.requiredCapabilities.some((capability) => !safeId(capability)))) ||
        (entry.proposition != null && (typeof entry.proposition !== 'string' || entry.proposition.length > 512)) ||
        (entry.parityContract != null && !safeId(entry.parityContract)))
      throw Object.assign(new Error('Suite entries must be unique FSD workflows with matching target, canonical hash, correlation, deadline, and required steps.'), { code: 'suite_entry_invalid' });
    ids.add(entry.entryId); scripts.add(entry.workflowId); runs.add(entry.runId); correlations.add(entry.correlationId);
    let continuation = null;
    if (entry.continuation != null) {
      const value = entry.continuation;
      if (!value || !safeId(value.continuationId) || !Array.isArray(value.stages) || value.stages.length < 2 || value.stages.length > 8)
        throw Object.assign(new Error('Continuation requires a stable ID and two to eight ordered process stages.'), { code: 'continuation_invalid' });
      const stageIds = new Set();
      continuation = { continuationId: value.continuationId, stages: value.stages.map((stage, index) => {
        const firstStage = index === 0;
        if (!stage || !safeId(stage.stageId) || stageIds.has(stage.stageId) || !isDocumentId(stage.workflowId) ||
            !HEX_256.test(stage.canonicalScriptHash || '') || !safeId(stage.runId) ||
            (firstStage ? (stage.workflowId !== entry.workflowId || stage.canonicalScriptHash !== entry.canonicalScriptHash || stage.runId !== entry.runId) :
              (scripts.has(stage.workflowId) || runs.has(stage.runId))) ||
            !safeId(stage.correlationId) || (firstStage ? stage.correlationId !== entry.correlationId : correlations.has(stage.correlationId)) ||
            !Number.isInteger(stage.timeoutSeconds || entry.timeoutSeconds) ||
            (stage.timeoutSeconds != null && (stage.timeoutSeconds < 1 || stage.timeoutSeconds > 86400)) ||
            !Array.isArray(stage.requiredStepIds) || stage.requiredStepIds.length === 0 ||
            new Set(stage.requiredStepIds).size !== stage.requiredStepIds.length || stage.requiredStepIds.some((id) => !safeId(id)) ||
            (firstStage && JSON.stringify(stage.requiredStepIds) !== JSON.stringify(entry.requiredStepIds)) ||
            stage.profileAction !== (index === 0 ? 'create' : 'load'))
          throw Object.assign(new Error('Continuation stages must be ordered; stage zero must match the logical entry, and later stages need fresh identities, required steps, and load actions.'), { code: 'continuation_invalid' });
        stageIds.add(stage.stageId);
        if (!firstStage) { scripts.add(stage.workflowId); runs.add(stage.runId); correlations.add(stage.correlationId); }
        return { ...stage, timeoutSeconds: stage.timeoutSeconds || entry.timeoutSeconds, requiredStepIds: stage.requiredStepIds.slice() };
      }) };
    }
    return { ...entry, requiredCapabilities: entry.requiredCapabilities ? entry.requiredCapabilities.slice() : [],
      proposition: entry.proposition || null, parityContract: entry.parityContract || null,
      requiredStepIds: entry.requiredStepIds.slice(), continuation };
  });
  for (const entry of entries) {
    const runIds = entry.continuation ? entry.continuation.stages.map((stage) => stage.runId) : [entry.runId];
    for (const runId of runIds) {
      const runDirectory = path.join(outputDirectory, 'runs', runId);
      assertWindowsPlayerArtifactPaths(runDirectory, path.join(runDirectory, 'logs', 'player.log'));
    }
  }
  return { ...spec, hostRoot, authoredRoot, sourceArtifactRoot, sourceExecutablePath, buildEvidencePath, outputDirectory,
    copyId: safeId(spec.copyId) ? spec.copyId : 'artifact-copy', entries };
}

function skippedSuiteEntry(entry, code, diagnostic) {
  const stages = entry.continuation ? entry.continuation.stages.map((stage) => ({
    stageId: stage.stageId, workflowId: stage.workflowId, canonicalScriptHash: stage.canonicalScriptHash,
    correlationId: stage.correlationId, runId: stage.runId, profileAction: stage.profileAction,
    profileIdSha256: null, accepted: false, state: 'skipped', failureCode: code, diagnostic,
    runDirectory: null, launchPath: null, resultPath: null, transcriptPath: null, playerLogPath: null,
  })) : [];
  return { entryId: entry.entryId, required: entry.required, workflowId: entry.workflowId,
    proposition: entry.proposition || null, parityContract: entry.parityContract || null,
    canonicalScriptHash: entry.canonicalScriptHash,
    requiredCapabilities: entry.requiredCapabilities || [], correlationId: entry.correlationId,
    scenario: entry.scenario == null ? null : entry.scenario, seed: entry.seed == null ? null : String(entry.seed),
    target: entry.target, mode: 'fsd', timeoutSeconds: entry.timeoutSeconds, accepted: false, state: 'skipped',
    failureCode: code, diagnostic, runDirectory: null, launchPath: null, resultPath: null, transcriptPath: null, playerLogPath: null,
    continuationId: entry.continuation ? entry.continuation.continuationId : null, stages };
}

function verifyRuntimeSmokeArtifactCopy(copy, fs, crypto) {
  try {
    const sourceTree = hashArtifactTree(fs, crypto, copy.sourceArtifactRoot);
    const copiedTree = hashArtifactTree(fs, crypto, copy.artifactRoot);
    const sourceExe = hashFile(fs, crypto, copy.sourceExecutablePath);
    const copiedExe = hashFile(fs, crypto, copy.executablePath);
    const sourceEvidenceSha256 = hashFile(fs, crypto, copy.sourceBuildEvidencePath);
    const copyEvidenceSha256 = hashFile(fs, crypto, copy.buildEvidencePath);
    const attestationSha256 = hashFile(fs, crypto, copy.attestationPath);
    const sourceEvidence = JSON.parse(fs.readFileSync(copy.sourceBuildEvidencePath, 'utf8'));
    const expectedCopyEvidence = { ...sourceEvidence, artifactPath: copy.executablePath };
    const valid = sourceEvidenceSha256 === copy.attestation.sourceBuildEvidenceSha256 &&
      copyEvidenceSha256 === copy.attestation.copiedBuildEvidenceSha256 && attestationSha256 === copy.attestationSha256 &&
      stableJson(JSON.parse(fs.readFileSync(copy.buildEvidencePath, 'utf8'))) === stableJson(expectedCopyEvidence) &&
      sourceTree.sha256 === copy.attestation.sourceTreeSha256 && copiedTree.sha256 === copy.attestation.copyTreeSha256 &&
      sourceTree.sha256 === copiedTree.sha256 && sourceExe === copy.attestation.sourceExecutableSha256 &&
      copiedExe === copy.attestation.copyExecutableSha256 && sourceExe === copiedExe;
    return { accepted: valid, diagnostic: valid ? null : 'Source or copied artifact tree/executable changed after fresh-copy attestation.' };
  } catch (error) { return { accepted: false, diagnostic: cleanText(error.message) }; }
}

function hashArtifactTree(fs, crypto, root) {
  const entries = [];
  function visit(directory, relative) {
    for (const dirent of fs.readdirSync(directory, { withFileTypes: true })) {
      const absolute = path.join(directory, dirent.name);
      const rel = relative ? `${relative}/${dirent.name}` : dirent.name;
      const stat = fs.lstatSync(absolute);
      if (stat.isSymbolicLink()) throw Object.assign(new Error(`Artifact tree contains a symbolic link: ${rel}`), { code: 'artifact_root_escape' });
      if (stat.isDirectory()) { entries.push({ type: 'directory', relative: rel }); visit(absolute, rel); }
      else if (stat.isFile()) entries.push({ type: 'file', absolute, relative: rel, size: stat.size });
      else throw Object.assign(new Error(`Artifact tree contains an unsupported filesystem entry: ${rel}`), { code: 'artifact_tree_invalid' });
    }
  }
  visit(root, '');
  entries.sort((left, right) => left.relative < right.relative ? -1 : left.relative > right.relative ? 1 : 0);
  const hash = crypto.createHash('sha256');
  let bytes = 0;
  let fileCount = 0;
  for (const entry of entries) {
    hash.update(entry.type === 'directory' ? 'D' : 'F'); hash.update('\0');
    hash.update(entry.relative.replace(/\\/g, '/')); hash.update('\0');
    if (entry.type === 'file') {
      const content = fs.readFileSync(entry.absolute);
      hash.update(String(content.length)); hash.update('\0'); hash.update(content); hash.update('\0');
      bytes += content.length;
      fileCount++;
    }
  }
  return { sha256: hash.digest('hex'), fileCount, entryCount: entries.length, bytes };
}

function isDocumentId(value) {
  return typeof value === 'string' && value.length <= 160 && value.split('/').length >= 2 &&
    value.split('/').every((part) => /^[A-Za-z0-9._-]+$/.test(part) && part !== '.' && part !== '..');
}
function collectRuntimeSmokeRunEvidence(evidence, runDirectory, fs, crypto) {
  const describe = (name, relativePath) => {
    const filePath = path.join(runDirectory, relativePath);
    try {
      const stat = fs.statSync(filePath);
      if (!stat.isFile()) return null;
      return { path: filePath, length: stat.size, lastWriteUtc: stat.mtime.toISOString(), sha256: hashFile(fs, crypto, filePath) };
    } catch (_) { return null; }
  };
  const parsed = (relativePath) => {
    try { return JSON.parse(fs.readFileSync(path.join(runDirectory, relativePath), 'utf8')); }
    catch (_) { return null; }
  };
  let transcript = evidence && Array.isArray(evidence.transcript) ? evidence.transcript : null;
  if (!transcript) {
    try {
      const text = fs.readFileSync(path.join(runDirectory, 'transcript.jsonl'), 'utf8');
      transcript = text.split(/\r?\n/).filter(Boolean).map((line) => JSON.parse(line));
    } catch (_) { transcript = null; }
  }
  return {
    result: evidence && evidence.result || parsed('result.json'),
    processExit: evidence && evidence.processExit || parsed('runtime-smoke-process-exit.json'),
    terminal: evidence && Array.isArray(evidence.transcript) ? evidence.transcript[evidence.transcript.length - 1] || null :
      transcript && transcript[transcript.length - 1] || null,
    transcript,
    files: {
      launch: describe('launch', 'runtime-smoke-launch.json'),
      process: describe('process', 'runtime-smoke-process.json'),
      processExit: describe('processExit', 'runtime-smoke-process-exit.json'),
      watch: describe('watch', 'runtime-smoke-watch.json'),
      result: describe('result', 'result.json'),
      transcript: describe('transcript', 'transcript.jsonl'),
      playerLog: describe('playerLog', path.join('logs', 'player.log')),
      playerStdout: describe('playerStdout', path.join('logs', 'player.stdout.txt')),
      playerStderr: describe('playerStderr', path.join('logs', 'player.stderr.txt')),
      failureContext: describe('failureContext', 'failure-context.json'),
    },
  };
}
function xmlEscape(value) {
  return String(value == null ? '' : value).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;').replace(/'/g, '&apos;');
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
function assertWindowsPlayerArtifactPaths(runDirectory, logPath) {
  if (process.platform !== 'win32') return;
  const paths = [
    ['-logFile', logPath],
    ['run directory', runDirectory],
    ['launch envelope', path.join(runDirectory, 'runtime-smoke-launch.json')],
    ['player claim', path.join(runDirectory, 'runtime-smoke-claim.json')],
    ['armed claim', path.join(runDirectory, 'runtime-smoke-armed.json')],
    ['transcript', path.join(runDirectory, 'transcript.jsonl')],
    ['result', path.join(runDirectory, 'result.json')],
    ['build evidence snapshot', path.join(runDirectory, 'build-evidence.json')],
    ['captured stdout', path.join(runDirectory, 'logs', 'player.stdout.txt')],
    ['captured stderr', path.join(runDirectory, 'logs', 'player.stderr.txt')],
  ];
  const overlong = paths.find(([, filePath]) => filePath.length >= 260);
  if (overlong) {
    const [label, filePath] = overlong;
    throw Object.assign(new Error(`RuntimeSmoke ${label} path is ${filePath.length} characters; Windows player paths must be shorter than 260. Choose a shorter output directory or run ID.`),
      { code: 'player_artifact_path_too_long' });
  }
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

module.exports = { validateRuntimeSmokeEvidence, prepareRuntimeSmokeRun, createRuntimeSmokeLaunch, launchRuntimeSmokeProcess, observeProcessIdentity, createBuildToken, prepareRuntimeSmokeArtifactCopy, runRuntimeSmokeSuite, createRuntimeSmokeBuildGate, serializeRuntimeSmokeBuildGateJUnit };
