import assert from 'node:assert/strict';
import test from 'node:test';

import { clean, hasResults, maxFindings, maxIds, maxTitleLength, safeUrl, summarise } from '../src/report.ts';
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

test('the ids of every finding are passed on, any severity, distinct and sorted, beyond the findings kept', () => {
  const many = Array.from({ length: 80 }, (_, index) =>
    vulnerability({ VulnerabilityID: `CVE-2026-${String(index).padStart(4, '0')}`, PkgName: `p${index}` }),
  );

  const summary = summarise(
    report(
      ...many,
      vulnerability({ VulnerabilityID: 'GHSA-abcd-efgh-ijkl', Severity: 'LOW' }),
      vulnerability({ VulnerabilityID: 'CVE-2026-0001', PkgName: 'another', Severity: 'MEDIUM' }),
    ),
  );

  assert.equal(summary.findings.length, maxFindings);
  assert.equal(summary.ids.length, 81);
  assert.equal(summary.ids[0], 'CVE-2026-0000');
  assert.equal(summary.ids.at(-1), 'GHSA-abcd-efgh-ijkl');
  assert.ok(summary.ids.includes('CVE-2026-0079'), 'an id whose finding was cut is still there');
  assert.equal(summary.idsTruncated, false);
});

test('an id that is not a plain id is not passed on, its finding is still counted', () => {
  const summary = summarise(
    report(
      vulnerability({ VulnerabilityID: 'CVE-2026-0001 <b>' }),
      vulnerability({ VulnerabilityID: '-leading-dash' }),
      vulnerability({ VulnerabilityID: `CVE-${'9'.repeat(70)}` }),
      vulnerability({ VulnerabilityID: 'DLA-1234-1', Severity: 'LOW' }),
    ),
  );

  assert.deepEqual(summary.ids, ['DLA-1234-1']);
  assert.equal(summary.counts.high, 3);
});

test(`beyond ${maxIds} ids the most severe are kept, and the list says it was cut`, () => {
  const low = Array.from({ length: maxIds }, (_, index) =>
    vulnerability({ VulnerabilityID: `A-${index}`, PkgName: 'p', Severity: 'LOW' }),
  );

  const summary = summarise(report(...low, vulnerability({ VulnerabilityID: 'Z-1', Severity: 'CRITICAL' })));

  assert.equal(summary.ids.length, maxIds);
  assert.equal(summary.idsTruncated, true);
  assert.ok(summary.ids.includes('Z-1'), 'the critical one is kept although it sorts last');
  assert.equal(summary.ids.at(-1), 'Z-1');
});
