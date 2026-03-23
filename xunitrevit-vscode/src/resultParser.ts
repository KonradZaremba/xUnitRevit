import * as fs from 'fs';
import { XMLParser } from 'fast-xml-parser';

/**
 * A single test result parsed from JUnit XML.
 */
export interface TestResultEntry {
  name: string;
  className: string;
  outcome: 'passed' | 'failed' | 'skipped';
  duration: number;
  message?: string;
}

/**
 * Parses a JUnit XML file (as output by xUnitRevit's TestResultWriter)
 * into an array of TestResultEntry objects.
 *
 * Handles the specific JUnit XML structure produced by HeadlessRunner:
 *   <testsuites>
 *     <testsuite name="ClassName" tests="N" ...>
 *       <testcase name="FullName" classname="ClassName" time="0.123">
 *         <failure message="...">stacktrace</failure>
 *         <skipped message="..." />
 *       </testcase>
 *     </testsuite>
 *   </testsuites>
 */
export async function parseJUnitResults(filePath: string): Promise<TestResultEntry[]> {
  if (!fs.existsSync(filePath)) {
    return [];
  }

  const xml = fs.readFileSync(filePath, 'utf-8');
  const parser = new XMLParser({
    ignoreAttributes: false,
    attributeNamePrefix: '@_',
    isArray: (name) => name === 'testsuite' || name === 'testcase',
  });

  const parsed = parser.parse(xml);
  const results: TestResultEntry[] = [];

  const testsuites = parsed?.testsuites?.testsuite;
  if (!testsuites) return results;

  for (const suite of testsuites) {
    const testcases = suite.testcase;
    if (!testcases) continue;

    for (const tc of testcases) {
      const name = tc['@_name'] || '';
      const className = tc['@_classname'] || suite['@_name'] || '';
      const time = parseFloat(tc['@_time'] || '0') * 1000; // Convert to ms

      let outcome: TestResultEntry['outcome'] = 'passed';
      let message: string | undefined;

      if (tc.failure) {
        outcome = 'failed';
        message = tc.failure['@_message'] || tc.failure['#text'] || 'Test failed';
      } else if (tc.skipped) {
        outcome = 'skipped';
        message = tc.skipped['@_message'];
      }

      results.push({ name, className, outcome, duration: time, message });
    }
  }

  return results;
}
