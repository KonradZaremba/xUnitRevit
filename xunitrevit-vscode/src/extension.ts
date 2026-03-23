import * as vscode from 'vscode';
import { RevitTestController } from './testController';

let controller: RevitTestController | undefined;

export function activate(context: vscode.ExtensionContext) {
  controller = new RevitTestController(context);
  context.subscriptions.push(controller);
}

export function deactivate() {
  controller?.dispose();
}
