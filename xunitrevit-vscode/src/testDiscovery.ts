import * as vscode from 'vscode';

/**
 * Represents a discovered test method from a C# source file.
 */
export interface DiscoveredTest {
  namespace: string;
  className: string;
  methodName: string;
  uri: vscode.Uri;
  range: vscode.Range;
}

/**
 * Discovers xUnit tests by scanning C# files for [Fact] and [Theory] attributes.
 *
 * Parses source files directly rather than compiled DLLs — this means tests
 * are discovered even before building, and the tree updates on file save.
 *
 * Limitations:
 * - Only finds [Fact] and [Theory] (not custom test attributes)
 * - Namespace detection is regex-based (handles most cases but not all)
 */
export async function discoverTests(): Promise<DiscoveredTest[]> {
  const tests: DiscoveredTest[] = [];

  const files = await vscode.workspace.findFiles(
    '**/*.cs',
    '{**/obj/**,**/bin/**,**/node_modules/**}'
  );

  for (const file of files) {
    try {
      const doc = await vscode.workspace.openTextDocument(file);
      const text = doc.getText();

      // Quick check: skip files without test attributes
      if (!text.includes('[Fact') && !text.includes('[Theory')) {
        continue;
      }

      const namespace = extractNamespace(text);
      const className = extractClassName(text);
      if (!className) continue;

      // Find all [Fact] and [Theory] methods
      const methodRegex = /\[(Fact|Theory)(?:\([^\)]*\))?\]\s*\n\s*public\s+(?:async\s+)?(?:void|Task)\s+(\w+)\s*\(/gm;
      let match: RegExpExecArray | null;

      while ((match = methodRegex.exec(text)) !== null) {
        const methodName = match[2];
        const pos = doc.positionAt(match.index);

        tests.push({
          namespace: namespace || 'Unknown',
          className,
          methodName,
          uri: file,
          range: new vscode.Range(pos, pos),
        });
      }
    } catch {
      // Skip files that can't be read
    }
  }

  return tests;
}

function extractNamespace(text: string): string {
  // Handle file-scoped namespace (C# 10+): namespace Foo.Bar;
  const fileScopedMatch = text.match(/^namespace\s+([\w.]+)\s*;/m);
  if (fileScopedMatch) return fileScopedMatch[1];

  // Handle block-scoped namespace: namespace Foo.Bar { ... }
  const blockMatch = text.match(/namespace\s+([\w.]+)\s*\{/);
  return blockMatch ? blockMatch[1] : '';
}

function extractClassName(text: string): string | null {
  const match = text.match(/public\s+class\s+(\w+)/);
  return match ? match[1] : null;
}
