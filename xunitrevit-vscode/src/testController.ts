import * as vscode from 'vscode';
import * as path from 'path';
import * as fs from 'fs';
import { discoverTests } from './testDiscovery';
import { runRevitTests } from './testRunner';
import { parseJUnitResults } from './resultParser';
import { getConfig } from './configuration';

/**
 * VS Code Test Controller for xUnitRevit.
 *
 * Shows up as "xUnitRevit" in Test Explorer alongside C# Dev Kit.
 * When user clicks Run, they choose between:
 *   - C# Dev Kit's "dotnet test" (runs locally, no Revit)
 *   - Our "Run in Revit" (launches Revit headless via run-revit-tests.ps1)
 */
export class RevitTestController implements vscode.Disposable {
  private ctrl: vscode.TestController;
  private runProfile: vscode.TestRunProfile;
  private watcher: vscode.FileSystemWatcher;
  private refreshTimer: NodeJS.Timeout | undefined;

  constructor(context: vscode.ExtensionContext) {
    this.ctrl = vscode.tests.createTestController('xunitRevit', 'xUnitRevit (Revit)');

    this.runProfile = this.ctrl.createRunProfile(
      'Run in Revit (headless)',
      vscode.TestRunProfileKind.Run,
      (request, token) => this.runTests(request, token)
    );

    // Watch for C# file changes — debounced refresh
    this.watcher = vscode.workspace.createFileSystemWatcher('**/*.cs');
    this.watcher.onDidChange(() => this.scheduleRefresh());
    this.watcher.onDidCreate(() => this.scheduleRefresh());
    this.watcher.onDidDelete(() => this.scheduleRefresh());
  }

  public refresh() {
    this.refreshTests();
  }

  private scheduleRefresh() {
    if (this.refreshTimer) clearTimeout(this.refreshTimer);
    this.refreshTimer = setTimeout(() => this.refreshTests(), 1000);
  }

  private async refreshTests() {
    try {
      const items = await discoverTests();

      // Clear existing items
      this.ctrl.items.forEach(item => this.ctrl.items.delete(item.id));

      // Build test tree: class → method
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
        testItem.range = new vscode.Range(test.line, 0, test.line, 0);
        classItem.children.add(testItem);
      }

      if (items.length > 0) {
        vscode.window.showInformationMessage(`xUnitRevit: discovered ${items.length} tests`);
      }
    } catch (err) {
      console.error('xUnitRevit: test discovery failed', err);
    }
  }

  /**
   * Finds run-revit-tests.ps1 by searching workspace root and parent directories.
   */
  private findTestScript(): string | undefined {
    const config = getConfig();
    if (config.testScriptPath && fs.existsSync(config.testScriptPath)) {
      return config.testScriptPath;
    }

    const workspaceRoot = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath;
    if (!workspaceRoot) return undefined;

    // Search current dir and up to 3 parents
    let dir = workspaceRoot;
    for (let i = 0; i < 4; i++) {
      const candidate = path.join(dir, 'run-revit-tests.ps1');
      if (fs.existsSync(candidate)) return candidate;
      const parent = path.dirname(dir);
      if (parent === dir) break;
      dir = parent;
    }

    return undefined;
  }

  private async runTests(request: vscode.TestRunRequest, token: vscode.CancellationToken) {
    const run = this.ctrl.createTestRun(request);
    const config = getConfig();

    // Find the test script
    const scriptPath = this.findTestScript();
    if (!scriptPath) {
      const msg = 'Cannot find run-revit-tests.ps1. Set xunitRevit.testScriptPath in settings.';
      vscode.window.showErrorMessage(msg);
      run.end();
      return;
    }

    // Collect all leaf test items
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

    allTests.forEach(t => run.started(t));

    try {
      // Override script path in config
      const runConfig = { ...config, testScriptPath: scriptPath };
      const output = await runRevitTests(runConfig, token);
      run.appendOutput(output.replace(/\n/g, '\r\n'));

      // Parse JUnit XML results
      const scriptDir = path.dirname(scriptPath);
      const resultsPath = path.join(scriptDir, 'RevitTestResults.xml');
      const results = await parseJUnitResults(resultsPath);

      // Map results back to test items
      for (const test of allTests) {
        const result = results.find(r =>
          r.name.endsWith(`.${test.label}`) ||
          r.name.includes(test.label) ||
          test.id.endsWith(r.name)
        );

        if (!result) {
          run.skipped(test);
        } else if (result.outcome === 'passed') {
          run.passed(test, result.duration);
        } else if (result.outcome === 'failed') {
          run.failed(test, new vscode.TestMessage(result.message || 'Test failed'), result.duration);
        } else {
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
    if (this.refreshTimer) clearTimeout(this.refreshTimer);
  }
}
