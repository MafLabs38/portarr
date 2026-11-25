// portarr-scanner: runs the Trivy CLI on the images Portarr names and answers with their vulnerabilities (D-131).
// It only pulls image layers from registries: nothing runs on the machines, and Portarr never acts on them (D-16, D-31).

import { mkdir } from 'node:fs/promises';
import { join } from 'node:path';

import { loadConfig } from './config.ts';
import { Scanner } from './scanner.ts';
import { createScannerServer } from './server.ts';
import { ResultStore } from './store.ts';
import { TrivyCli } from './trivy.ts';

function log(message: string): void {
  process.stdout.write(`${new Date().toISOString()} ${message}\n`);
}

async function main(): Promise<void> {
  const config = loadConfig(process.env);
  const cacheDir = join(config.dataDir, 'trivy');
  await mkdir(cacheDir, { recursive: true, mode: 0o700 });

  const scanner = new Scanner(
    new TrivyCli(config.trivyCommand, cacheDir, config.scanTimeoutMs),
    new ResultStore(config.dataDir),
    { rescanAfterMs: config.rescanAfterMs, forgetAfterMs: config.forgetAfterMs, log },
  );
  await scanner.init();

  const server = createScannerServer(scanner, config.secret, log);
  server.listen(config.port, config.host, () => {
    log(`portarr-scanner listening on ${config.host}:${config.port}`);
  });

  const stop = () => {
    server.close();
    setTimeout(() => process.exit(0), 1000).unref();
  };
  process.on('SIGTERM', stop);
  process.on('SIGINT', stop);
}

try {
  await main();
} catch (error: unknown) {
  process.stderr.write(`${error instanceof Error ? error.message : String(error)}\n`);
  process.exit(1);
}
