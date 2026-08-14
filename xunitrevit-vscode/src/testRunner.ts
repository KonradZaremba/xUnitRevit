import * as vscode from 'vscode';
import { spawn } from 'child_process';
import { RevitConfig } from './configuration';

/**
 * Runs Revit tests by spawning the PowerShell test runner script.
 *
 * Local mode: spawns run-revit-tests.ps1 directly.
 * Remote mode (sshTarget set): spawns run-revit-tests-remote.ps1 with SSH target.
 *
 * Streams output to the VS Code Output channel in real-time.
 * Returns the full output when complete.
 */
export function runRevitTests(config: RevitConfig, token: vscode.CancellationToken): Promise<string> {
  return new Promise((resolve, reject) => {
    const workspaceRoot = vscode.workspace.workspaceFolders?.[0]?.uri.fsPath;
    if (!workspaceRoot) {
      reject(new Error('No workspace folder found'));
      return;
    }

    // Determine which script to run
    const scriptPath = config.testScriptPath || `${workspaceRoot}/run-revit-tests.ps1`;
    let args: string[];

    if (config.sshTarget) {
      // Remote execution
      const remoteScript = `${workspaceRoot}/run-revit-tests-remote.ps1`;
      args = [
        '-ExecutionPolicy', 'Bypass',
        '-File', remoteScript,
        '-SshTarget', config.sshTarget,
        '-RevitVersion', config.revitVersion,
        '-Timeout', String(config.timeout),
      ];
    } else {
      // Local execution
      args = [
        '-ExecutionPolicy', 'Bypass',
        '-File', scriptPath,
        '-RevitVersion', config.revitVersion,
        '-Timeout', String(config.timeout),
      ];
    }

    if (config.skipBuild) {
      args.push('-SkipBuild');
    }

    const outputChannel = vscode.window.createOutputChannel('xUnitRevit');
    outputChannel.show(true);
    outputChannel.appendLine(`Running: powershell ${args.join(' ')}`);
    outputChannel.appendLine('---');

    let output = '';
    const proc = spawn('powershell', args, {
      cwd: workspaceRoot,
      stdio: ['ignore', 'pipe', 'pipe'],
    });

    proc.stdout.on('data', (data: Buffer) => {
      const text = data.toString();
      output += text;
      outputChannel.append(text);
    });

    proc.stderr.on('data', (data: Buffer) => {
      const text = data.toString();
      output += text;
      outputChannel.append(text);
    });

    // Handle cancellation
    token.onCancellationRequested(() => {
      outputChannel.appendLine('\n--- Cancelled by user ---');
      proc.kill('SIGTERM');
    });

    proc.on('close', (code) => {
      outputChannel.appendLine(`\n--- Exited with code ${code} ---`);
      // Don't reject on non-zero exit — IntentionalFailure test causes exit code 1
      resolve(output);
    });

    proc.on('error', (err) => {
      reject(new Error(`Failed to start PowerShell: ${err.message}`));
    });
  });
}
