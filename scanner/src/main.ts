// portarr-scanner: runs the Trivy CLI on the images Portarr names, and on the SBOM of the latest release of the
// repositories it names, and answers with their vulnerabilities (D-131). It only pulls image layers from registries and
// reads public release assets from GitHub: nothing runs on the machines, and Portarr never acts on them (D-16, D-31).

import { mkdir, rm } from 'node:fs/promises';
import { join } from 'node:path';

import { loadConfig } from './config.ts';
import { ReleaseSbomSource } from './release.ts';
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

  // Downloaded SBOMs live here, on disk and not in /tmp (a RAM tmpfs); a crash may leave one behind.
  const workDir = join(config.dataDir, 'work');
  await rm(workDir, { recursive: true, force: true });

  const scanner = new Scanner(
    new TrivyCli(config.trivyCommand, cacheDir, config.scanTimeoutMs),
    new ResultStore(config.dataDir),
    new ReleaseSbomSource({ workDir, ...(config.githubToken === undefined ? {} : { token: config.githubToken }) }),
    { rescanAfterMs: config.rescanAfterMs, forgetAfterMs: config.forgetAfterMs, maxImages: config.maxImages, log },
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
