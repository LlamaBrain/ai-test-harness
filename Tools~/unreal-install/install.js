#!/usr/bin/env node
'use strict';

// Development plugin installation only. No engine launch, copying, project
// descriptor mutation, replacement, unlinking or deletion is performed.
const fs = require('node:fs');
const path = require('node:path');
const DEFAULT_SOURCE = path.resolve(__dirname, '../../Unreal~/Ath');

function readJson(file, description) {
  try { return JSON.parse(fs.readFileSync(file, 'utf8').replace(/^\uFEFF/, '')); }
  catch (error) { throw new Error(`Cannot read ${description} JSON at ${file}: ${error.message}`); }
}

function statOrNull(file) {
  try { return fs.lstatSync(file); }
  catch (error) { if (error.code === 'ENOENT') return null; throw error; }
}

function resolveProject(input) {
  if (typeof input !== 'string' || !input.trim()) throw new Error('--project must select a .uproject file or project directory');
  const selected = path.resolve(input);
  const info = fs.statSync(selected);
  let projectFile = selected;
  if (info.isDirectory()) {
    const projects = fs.readdirSync(selected, { withFileTypes: true })
      .filter(entry => entry.isFile() && entry.name.toLowerCase().endsWith('.uproject'));
    if (projects.length !== 1) throw new Error('Project directory must contain exactly one .uproject; select the file explicitly if ambiguous');
    projectFile = path.join(selected, projects[0].name);
  }
  if (!projectFile.toLowerCase().endsWith('.uproject')) throw new Error('--project must select a .uproject file');
  const descriptor = readJson(projectFile, 'project');
  if (!descriptor || descriptor.FileVersion !== 3) throw new Error('Unrecognized Unreal project descriptor FileVersion');
  return { projectFile: fs.realpathSync(projectFile), root: fs.realpathSync(path.dirname(projectFile)) };
}

function installPlugin({ project, source = DEFAULT_SOURCE, dryRun = false, verifyOnly = false } = {}) {
  if (dryRun && verifyOnly) throw new Error('--dry-run and --verify-only are mutually exclusive');
  if (typeof source !== 'string' || !source.trim()) throw new Error('Plugin source is required');
  const pluginSource = fs.realpathSync(path.resolve(source));
  if (!fs.statSync(pluginSource).isDirectory()) throw new Error('Plugin source must be a directory');
  const descriptor = readJson(path.join(pluginSource, 'Ath.uplugin'), 'plugin');
  if (descriptor?.FileVersion !== 3 || !Array.isArray(descriptor.Modules) ||
    !descriptor.Modules.some(module => module.Name === 'AthRuntime'))
    throw new Error('Plugin source must contain the ATH AthRuntime descriptor');
  const selected = resolveProject(project);
  const plugins = path.join(selected.root, 'Plugins');
  const parent = statOrNull(plugins);
  // Refuse a redirected parent, including foreign directory junctions. The
  // selected consuming project must own the destination we are about to create.
  if (parent && (!parent.isDirectory() || parent.isSymbolicLink()))
    throw new Error('Project Plugins must be an ordinary directory, not a link or file');
  const destination = path.join(plugins, 'Ath');
  if (pluginSource === destination) throw new Error('Plugin source cannot equal the installation destination');
  const existing = statOrNull(destination);
  const result = { source: pluginSource, projectFile: selected.projectFile, destination,
    method: process.platform === 'win32' ? 'junction' : 'symlink' };
  if (existing) {
    let matches = false;
    if (existing.isSymbolicLink()) {
      try { matches = fs.realpathSync(destination) === pluginSource; }
      catch { /* A dangling link is a conflict; never remove it. */ }
    }
    if (!matches) throw new Error(`Existing plugin conflict at ${destination}; refusing to overwrite or remove it`);
    return { ...result, status: 'verified' };
  }
  if (verifyOnly) throw new Error(`ATH plugin is not installed: ${destination}`);
  if (dryRun) return { ...result, status: 'planned' };
  fs.mkdirSync(plugins, { recursive: true });
  // symlinkSync refuses an existing destination. There is no delete/replace
  // fallback, so a concurrent install cannot cause another plugin to be lost.
  fs.symlinkSync(pluginSource, destination, process.platform === 'win32' ? 'junction' : 'dir');
  if (fs.realpathSync(destination) !== pluginSource) throw new Error('Created plugin link did not resolve to the selected source');
  return { ...result, status: 'installed' };
}

function parseOptions(argv) {
  const options = { source: DEFAULT_SOURCE };
  for (let index = 0; index < argv.length; index++) {
    const flag = argv[index];
    if (flag === '--dry-run') { options.dryRun = true; continue; }
    if (flag === '--verify-only') { options.verifyOnly = true; continue; }
    const key = flag === '--project' ? 'project' : flag === '--source' ? 'source' : null;
    if (!key) throw new Error(`Unknown option: ${flag}`);
    if (!argv[index + 1] || argv[index + 1].startsWith('--')) throw new Error(`${flag} requires a value`);
    options[key] = argv[++index];
  }
  if (!options.project) throw new Error('--project is required');
  if (options.dryRun && options.verifyOnly) throw new Error('--dry-run and --verify-only are mutually exclusive');
  return options;
}

function main(argv = process.argv.slice(2)) {
  const result = installPlugin(parseOptions(argv));
  process.stdout.write(JSON.stringify(result, null, 2) + '\n');
  return 0;
}

if (require.main === module) {
  try { process.exitCode = main(); }
  catch (error) { process.stderr.write(`ath-unreal-install: ${error.message}\n`); process.exitCode = 1; }
}

module.exports = { installPlugin, parseOptions, main };
