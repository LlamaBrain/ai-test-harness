'use strict';
const { test } = require('node:test');
const assert = require('node:assert/strict');
const { resolveProfile, applyProfileScope } = require('./visual-profiles');
test('profiles scale rendered pixels by screenshot height', () => {
  assert.equal(resolveProfile('xag101-console', 1080).minHeightPx, 26);
  assert.equal(resolveProfile('xag101-pc', 2160).minHeightPx, 36);
  assert.equal(resolveProfile('steam-adjustable', 1080).minHeightPx, 38);
  assert.equal(resolveProfile('steam-adjustable', 2160).minHeightPx, 76);
  assert.equal(resolveProfile('steam-adjustable', 1440).minHeightPx, 38 * 1440 / 1080);
  assert.throws(() => resolveProfile('../invalid', 1080));
});
test('one large screenshot cannot establish adjustable text capability', () => {
  const result = { outcome: 'pass', findings: [], coverage: { limitations: [] } };
  applyProfileScope(result, resolveProfile('steam-adjustable', 1080));
  assert.equal(result.outcome, 'inconclusive');
  assert.equal(result.findings[0].ruleId, 'text.adjustability');
  const failed = { outcome: 'fail', findings: [], coverage: { limitations: [] } };
  applyProfileScope(failed, resolveProfile('steam-adjustable', 1080));
  assert.equal(failed.outcome, 'fail');
});
