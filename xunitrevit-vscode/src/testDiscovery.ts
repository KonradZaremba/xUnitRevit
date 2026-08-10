import * as vscode from 'vscode';
import * as fs from 'fs';

/**
 * Represents a discovered test method from a C# source file.
 */
export interface DiscoveredTest {
  namespace: string;
  className: string;
  methodName: string;
  uri: vscode.Uri;
  line: number;
}

/**
 * Discovers xUnit tests by scanning C# files for [Fact] and [Theory] attributes.
 * Uses fs.readFileSync instead of vscode.openTextDocument to avoid crashing
 * the extension host when many files are present.
 */
export async function discoverTests(): Promise<DiscoveredTest[]> {
  const tests: DiscoveredTest[] = [];

  const files = await vscode.workspace.findFiles(
    '{**/SampleLibrary*/**/*.cs,**/Tests/**/*.cs,**/*Tests.cs,**/*Test.cs}',
    '{**/obj/**,**/bin/**,**/node_modules/**}',
    200 // limit to 200 files max
  );

  for (const file of files) {
    try {
      const text = fs.readFileSync(file.fsPath, 'utf-8');

      // Quick check: skip files without test attributes
      if (!text.includes('[Fact') && !text.includes('[Theory')) {
        continue;
      }

      const namespace = extractNamespace(text);
      const classes = extractClasses(text);

      for (const cls of classes) {
        // Find all [Fact] and [Theory] methods
        const methodRegex = /\[(Fact|Theory)(?:\([^\)]*\))?\]\s*\r?\n\s*public\s+(?:async\s+)?(?:void|Task)\s+(\w+)\s*\(/gm;
        let match: RegExpExecArray | null;

        while ((match = methodRegex.exec(text)) !== null) {
          const methodName = match[2];
          const line = text.substring(0, match.index).split('\n').length - 1;

          tests.push({
            namespace: namespace || 'Unknown',
            className: cls,
            methodName,
            uri: file,
            line,
          });
        }
      }
    } catch {
      // Skip files that can't be read
    }
  }

  return tests;
}

function extractNamespace(text: string): string {
  const fileScopedMatch = text.match(/^namespace\s+([\w.]+)\s*;/m);
  if (fileScopedMatch) return fileScopedMatch[1];

  const blockMatch = text.match(/namespace\s+([\w.]+)\s*\{/);
  return blockMatch ? blockMatch[1] : '';
}

function extractClasses(text: string): string[] {
  const classes: string[] = [];
  const regex = /public\s+class\s+(\w+)/g;
  let match: RegExpExecArray | null;
  while ((match = regex.exec(text)) !== null) {
    classes.push(match[1]);
  }
  return classes;
}
