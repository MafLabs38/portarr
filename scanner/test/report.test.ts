import assert from 'node:assert/strict';
import test from 'node:test';

import { clean, hasResults, maxFindings, maxTitleLength, safeUrl, summarise } from '../src/report.ts';
import { report, vulnerability } from './fakes.ts';

test('every severity is counted, and the ones that have a fix', () => {
  const summary = summarise(
    report(
      vulnerability({ VulnerabilityID: 'a', Severity: 'CRITICAL' }),
      vulnerability({ VulnerabilityID: 'b', Severity: 'HIGH', FixedVersion: '' }),
      vulnerability({ VulnerabilityID: 'c', Severity: 'MEDIUM' }),
      vulnerability({ VulnerabilityID: 'd', Severity: 'LOW', FixedVersion: undefined }),
      vulnerability({ VulnerabilityID: 'e', Severity: 'SURPRISING' }),
    ),
  );

  assert.deepEqual(summary.counts, { critical: 1, high: 1, medium: 1, low: 1, unknown: 1 });
  assert.deepEqual(summary.fixable, { critical: 1, high: 0, medium: 1, low: 0, unknown: 1 });
});

test('only HIGH and CRITICAL are described, critical first, the fixable ones before the others', () => {
  const summary = summarise(
    report(
      vulnerability({ VulnerabilityID: 'CVE-3', Severity: 'HIGH', FixedVersion: '' }),
      vulnerability({ VulnerabilityID: 'CVE-2', Severity: 'HIGH' }),
      vulnerability({ VulnerabilityID: 'CVE-1', Severity: 'CRITICAL', FixedVersion: '' }),
      vulnerability({ VulnerabilityID: 'CVE-4', Severity: 'MEDIUM' }),
    ),
  );

  assert.deepEqual(
    summary.findings.map((finding) => finding.id),
    ['CVE-1', 'CVE-2', 'CVE-3'],
  );
  assert.equal(summary.findings[1].fixedVersion, '3.0.2');
  assert.equal(summary.findings[2].fixedVersion, undefined);
});

test('the same flaw in the same package is one flaw, even reported by two files', () => {
  const twice = { Results: [{ Vulnerabilities: [vulnerability()] }, { Vulnerabilities: [vulnerability()] }] };

  assert.equal(summarise(twice).counts.high, 1);
  assert.equal(summarise(twice).findings.length, 1);
});

test(`at most ${maxFindings} findings are described, the counts stay complete`, () => {
  const many = Array.from({ length: 80 }, (_, index) =>
    vulnerability({ VulnerabilityID: `CVE-${index}`, PkgName: `p${index}` }),
  );

  const summary = summarise(report(...many));

  assert.equal(summary.findings.length, maxFindings);
  assert.equal(summary.counts.high, 80);
});

test('what a database wrote is cleaned: control and bidirectional characters, length, links', () => {
  const summary = summarise(
    report(
      vulnerability({
        Title: `Ignore\u0000 the\nrules\u202E and ${'x'.repeat(400)}`,
        PrimaryURL: 'javascript:alert(1)',
      }),
    ),
  );

  const title = summary.findings[0].title ?? '';
  assert.ok(title.length <= maxTitleLength);
  assert.match(title, /^Ignore the rules and x+$/);
  assert.equal(summary.findings[0].url, undefined);
});

test('a link is kept only when it is an absolute https address without credentials', () => {
  assert.equal(safeUrl('https://avd.example/cve'), 'https://avd.example/cve');
  assert.equal(safeUrl('http://avd.example/cve'), undefined);
  assert.equal(safeUrl('https://user:secret@avd.example/'), undefined);
  assert.equal(safeUrl('/relative'), undefined);
  assert.equal(safeUrl(`https://avd.example/${'a'.repeat(600)}`), undefined);
  assert.equal(safeUrl(42), undefined);
});

test('clean drops what is not text, cuts by characters and says nothing for an empty value', () => {
  assert.equal(clean('a​b\u2066c', 10), 'abc');
  assert.equal(clean('😀😀😀', 2), '😀😀');
  assert.equal(clean('   ', 10), undefined);
  assert.equal(clean(undefined, 10), undefined);
});

test('a vulnerability without an id or a package is skipped, and a report of nothing has no results', () => {
  assert.equal(summarise(report({ Severity: 'HIGH' })).counts.high, 0);
  assert.equal(summarise(null).counts.high, 0);
  assert.equal(summarise({ Results: 'no' }).counts.high, 0);
  assert.equal(hasResults({ SchemaVersion: 2 }), false);
  assert.equal(hasResults({ Results: [] }), false);
  assert.equal(hasResults(report()), true);
});
