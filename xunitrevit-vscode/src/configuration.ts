import * as vscode from 'vscode';

/**
 * Configuration for the xUnitRevit extension.
 * Read from VS Code settings (settings.json) under the "xunitRevit" prefix.
 */
export interface RevitConfig {
  revitVersion: string;
  timeout: number;
  testScriptPath: string;
  sshTarget: string;
  skipBuild: boolean;
}

/**
 * Reads current extension configuration from VS Code settings.
 */
export function getConfig(): RevitConfig {
  const config = vscode.workspace.getConfiguration('xunitRevit');
  return {
    revitVersion: config.get<string>('revitVersion', '2026'),
    timeout: config.get<number>('timeout', 600),
    testScriptPath: config.get<string>('testScriptPath', ''),
    sshTarget: config.get<string>('sshTarget', ''),
    skipBuild: config.get<boolean>('skipBuild', false),
  };
}
