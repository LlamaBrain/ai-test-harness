'use strict';
const fs = require('node:fs');
const path = require('node:path');
function resolveProfile(name, height) {
  if (!['xag101-console', 'xag101-pc', 'steam-adjustable'].includes(name)) throw new Error('Unknown visual policy profile: ' + name);
  if (!Number.isInteger(height) || height < 1 || height > 32768) throw new Error('Invalid screenshot height');
  const profile = JSON.parse(fs.readFileSync(path.join(__dirname, 'profiles', name + '.json'), 'utf8'));
  return { ...profile, minHeightPx: profile.minHeightPx * height / profile.referenceHeight, analyzedHeight: height };
}
function applyProfileScope(result, policy) {
  if (!policy || policy.scope !== 'adjustable-size-capability') return result;
  const message = 'A screenshot cannot establish that users can adjust text size; separate runtime capability evidence is required';
  result.findings = result.findings.filter(finding => finding.ruleId !== 'text.adjustability');
  result.findings.push({ ruleId: 'text.adjustability', elementId: '', outcome: 'inconclusive', bounds: null, measuredHeightPx: null, contrastRatio: null, detectionConfidence: null, measurementConfidence: null, message });
  if (!result.coverage.limitations.includes(message)) result.coverage.limitations.push(message);
  if (result.outcome === 'pass') result.outcome = 'inconclusive';
  return result;
}
module.exports = { resolveProfile, applyProfileScope };
