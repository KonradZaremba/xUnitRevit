import * as vscode from 'vscode';
import { RevitTestController } from './testController';

let controller: RevitTestController | undefined;

export function activate(context: vscode.ExtensionContext) {
  console.log('xUnitRevit extension activating...');

  try {
    controller = new RevitTestController(context);
    context.subscriptions.push(controller);

    context.subscriptions.push(
      vscode.commands.registerCommand('xunitRevit.refreshTests', () => {
        controller?.refresh();
      })
    );

    // Auto-discover after a short delay
    setTimeout(() => controller?.refresh(), 3000);

    console.log('xUnitRevit extension activated.');
  } catch (err) {
    console.error('xUnitRevit activation failed:', err);
  }
}

export function deactivate() {
  controller?.dispose();
}
