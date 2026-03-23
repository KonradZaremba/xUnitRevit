import * as vscode from 'vscode';
import { discoverTests } from './testDiscovery';
import { runRevitTests } from './testRunner';
import { parseJUnitResults } from './resultParser';
import { getConfig } from './configuration';

/**
 * VS Code Test Controller for xUnitRevit.
 *
 * Discovers xUnit [Fact]/[Theory] tests in C# files and runs them
 * inside Revit (headless mode) via run-revit-tests.ps1.
 *
 * Architecture:
 *   1. Discovery: scans *.cs files for [Fact]/[Theory] attributes
 *   2. Execution: spawns PowerShell → run-revit-tests.ps1 → Revit headless
 *   3. Results: parses JUnit XML output → maps back to TestItems
 */
export class RevitTestController implements vscode.Disposable {
  private ctrl: vscode.TestController;
  private runProfile: vscode.TestRunProfile;
  private watcher: vscode.FileSystemWatcher;

  constructor(context: vscode.ExtensionContext) {
    this.ctrl = vscode.tests.createTestController('xunitRevit', 'xUnitRevit');

    this.runProfile = this.ctrl.createRunProfile(
      'Run in Revit',
      vscode.TestRunProfileKind.Run,
      (request, token) => this.runTests(request, token)
    );

    // Watch for C# file changes to refresh test discovery
    this.watcher = vscode.workspace.createFileSystemWatcher('**/*.cs');
    this.watcher.onDidChange(() => this.refreshTests());
    this.watcher.onDidCreate(() => this.refreshTests());
    this.watcher.onDidDelete(() => this.refreshTests());

    // Initial discovery
    this.refreshTests();
  }

  private async refreshTests() {
    const items = await discoverTests();

    // Clear existing items
    this.ctrl.items.forEach(item => this.ctrl.items.delete(item.id));

    // Build test tree: namespace → class → method
    for (const test of items) {
      const classId = `${test.namespace}.${test.className}`;
      let classItem = this.ctrl.items.get(classId);

      if (!classItem) {
        classItem = this.ctrl.createTestItem(classId, test.className, test.uri);
        classItem.description = test.namespace;
        this.ctrl.items.add(classItem);
      }

      const testItem = this.ctrl.createTestItem(
        `${classId}.${test.methodName}`,
        test.methodName,
        test.uri
      );
      testItem.range = test.range;
      classItem.children.add(testItem);
    }
  }

  private async runTests(request: vscode.TestRunRequest, token: vscode.CancellationToken) {
    const run = this.ctrl.createTestRun(request);
    const config = getConfig();

    // Mark all tests as queued
    const allTests: vscode.TestItem[] = [];
    const collectTests = (items: vscode.TestItemCollection) => {
      items.forEach(item => {
        if (item.children.size > 0) {
          collectTests(item.children);
        } else {
          allTests.push(item);
          run.enqueued(item);
        }
      });
    };

    if (request.include) {
      request.include.forEach(item => {
        if (item.children.size > 0) {
          collectTests(item.children);
        } else {
          allTests.push(item);
          run.enqueued(item);
        }
      });
    } else {
      collectTests(this.ctrl.items);
    }

    // Mark as started
    allTests.forEach(t => run.started(t));

    try {
      // Run Revit tests
      const output = await runRevitTests(config, token);
      run.appendOutput(output.replace(/\n/g, '\r\n'));

      // Parse results
      const workspaceRoot = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath;
      if (!workspaceRoot) {
        run.appendOutput('No workspace folder found\r\n');
        allTests.forEach(t => run.errored(t, new vscode.TestMessage('No workspace')));
        run.end();
        return;
      }

      const resultsPath = `${workspaceRoot}/RevitTestResults.xml`;
      const results = await parseJUnitResults(resultsPath);

      // Map results to test items
      for (const test of allTests) {
        const result = results.find(r => test.id.endsWith(r.name) || r.name.includes(test.label));

        if (!result) {
          run.skipped(test);
        } else if (result.outcome === 'passed') {
          run.passed(test, result.duration);
        } else if (result.outcome === 'failed') {
          const msg = new vscode.TestMessage(result.message || 'Test failed');
          run.failed(test, msg, result.duration);
        } else if (result.outcome === 'skipped') {
          run.skipped(test);
        }
      }
    } catch (err: any) {
      const msg = err.message || String(err);
      run.appendOutput(`Error: ${msg}\r\n`);
      allTests.forEach(t => run.errored(t, new vscode.TestMessage(msg)));
    }

    run.end();
  }

  dispose() {
    this.ctrl.dispose();
    this.watcher.dispose();
  }
}
