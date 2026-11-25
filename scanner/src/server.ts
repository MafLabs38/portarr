// The scanner's HTTP API, for Portarr only: a bearer shared secret on every route but /healthz, JSON in and out.
// Publish its port only on an address Portarr reaches (or share a Docker network): the secret is the only gate.

import { createHash, timingSafeEqual } from 'node:crypto';
import { createServer, type IncomingMessage, type Server, type ServerResponse } from 'node:http';

import { parseTargets } from './reference.ts';
import type { Scanner } from './scanner.ts';

/** 200 references of 300 characters, with their quotes. */
const maxBodyBytes = 128 * 1024;

function digest(value: string): Buffer {
  return createHash('sha256').update(value, 'utf8').digest();
}

/** Constant time, whatever the lengths: both sides are hashed first. */
export function secretMatches(header: string | undefined, secret: string): boolean {
  const presented = header?.startsWith('Bearer ') ? header.slice('Bearer '.length).trim() : '';
  return presented.length > 0 && timingSafeEqual(digest(presented), digest(secret));
}

function send(response: ServerResponse, status: number, body?: unknown): void {
  if (body === undefined) {
    response.writeHead(status).end();
    return;
  }
  response
    .writeHead(status, { 'Content-Type': 'application/json; charset=utf-8', 'Cache-Control': 'no-store' })
    .end(JSON.stringify(body));
}

async function readJson(request: IncomingMessage): Promise<unknown> {
  let size = 0;
  const chunks: Buffer[] = [];
  for await (const chunk of request) {
    size += (chunk as Buffer).length;
    if (size > maxBodyBytes) {
      throw new HttpError(413, 'The request is too large.');
    }
    chunks.push(chunk as Buffer);
  }
  try {
    return JSON.parse(Buffer.concat(chunks).toString('utf8'));
  } catch {
    throw new HttpError(400, 'The body must be JSON.');
  }
}

class HttpError extends Error {
  readonly status: number;
  constructor(status: number, message: string) {
    super(message);
    this.status = status;
  }
}

async function route(
  scanner: Scanner,
  request: IncomingMessage,
  response: ServerResponse,
  path: string,
): Promise<void> {
  const method = request.method ?? 'GET';
  if (path === '/v1/results') {
    if (method !== 'GET') {
      throw new HttpError(405, 'Method not allowed.');
    }
    send(response, 200, scanner.results());
    return;
  }
  if (path === '/v1/scans') {
    if (method !== 'POST') {
      throw new HttpError(405, 'Method not allowed.');
    }
    const body = (await readJson(request)) as { targets?: unknown } | null;
    const parsed = parseTargets(body?.targets);
    if ('error' in parsed) {
      throw new HttpError(400, parsed.error);
    }
    send(response, 202, scanner.request(parsed.targets));
    return;
  }
  throw new HttpError(404, 'Not found.');
}

export function createScannerServer(
  scanner: Scanner,
  secret: string,
  log: (message: string) => void = () => undefined,
): Server {
  return createServer((request, response) => {
    const path = (request.url ?? '/').split('?')[0];
    if (path === '/healthz') {
      send(response, 200, { status: 'ok' });
      return;
    }
    if (!secretMatches(request.headers.authorization, secret)) {
      response.setHeader('WWW-Authenticate', 'Bearer');
      send(response, 401, { error: 'A bearer token is required.' });
      return;
    }
    route(scanner, request, response, path).catch((error: unknown) => {
      if (error instanceof HttpError) {
        send(response, error.status, { error: error.message });
        return;
      }
      log(`request failed: ${error instanceof Error ? error.message : String(error)}`);
      send(response, 500, { error: 'The scanner failed.' });
    });
  });
}
