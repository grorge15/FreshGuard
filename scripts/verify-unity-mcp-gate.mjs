#!/usr/bin/env node
// Read-only Unity MCP gate for this FreshGuard checkout.
import { spawn } from 'node:child_process';
import { createInterface } from 'node:readline';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const projectRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const server = spawn('uvx', [
  '--offline', '--from', 'mcpforunityserver==10.1.2', 'mcp-for-unity', '--transport', 'stdio',
], {
  cwd: projectRoot,
  env: { ...process.env, SystemRoot: process.env.SystemRoot || 'C:\\Windows' },
  stdio: ['pipe', 'pipe', 'pipe'],
  windowsHide: true,
});

let nextId = 1;
let stderr = '';
let closed = false;
const pending = new Map();
server.stderr.setEncoding('utf8');
server.stderr.on('data', chunk => { stderr = (stderr + chunk).slice(-4000); });
server.on('error', error => failPending(error));
server.on('exit', (code, signal) => {
  closed = true;
  failPending(new Error(`MCP server exited (${code ?? signal}); ${stderr.trim()}`));
});

function failPending(error) {
  for (const [id, request] of pending) {
    clearTimeout(request.timer);
    request.reject(error);
    pending.delete(id);
  }
}

createInterface({ input: server.stdout }).on('line', line => {
  let message;
  try { message = JSON.parse(line); } catch { return; }
  const request = pending.get(message.id);
  if (!request) return;
  pending.delete(message.id);
  clearTimeout(request.timer);
  if (message.error) request.reject(new Error(JSON.stringify(message.error)));
  else request.resolve(message.result);
});

function request(method, params, timeoutMs = 45000) {
  if (closed || !server.stdin.writable) return Promise.reject(new Error('MCP server unavailable'));
  const id = nextId++;
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => {
      pending.delete(id);
      reject(new Error(`${method} timed out after ${timeoutMs}ms; ${stderr.trim()}`));
    }, timeoutMs);
    pending.set(id, { resolve, reject, timer });
    server.stdin.write(JSON.stringify({ jsonrpc: '2.0', id, method, params }) + '\n');
  });
}

function resourceData(result, uri) {
  const text = result?.contents?.find(item => item.uri === uri && typeof item.text === 'string')?.text
    ?? result?.contents?.find(item => typeof item.text === 'string')?.text;
  if (!text) throw new Error(`MCP resource ${uri} returned no text`);
  const parsed = JSON.parse(text);
  if (parsed.success === false) throw new Error(`${uri}: ${parsed.error ?? text}`);
  return parsed.data ?? parsed;
}

async function readResource(uri) {
  return resourceData(await request('resources/read', { uri }), uri);
}

function samePath(a, b) {
  return path.normalize(a).replace(/[\\/]+$/, '').toLowerCase()
    === path.normalize(b).replace(/[\\/]+$/, '').toLowerCase();
}

function toolData(result, name) {
  if (result?.isError) throw new Error(`${name} failed: ${JSON.stringify(result.content)}`);
  const structured = result?.structuredContent;
  if (structured) return structured.result ?? structured;
  const text = result?.content?.find(item => typeof item.text === 'string')?.text;
  if (!text) throw new Error(`${name} returned no data`);
  return JSON.parse(text);
}

try {
  await request('initialize', {
    protocolVersion: '2025-03-26',
    capabilities: {},
    clientInfo: { name: 'freshguard-unity-gate', version: '1.0.0' },
  }, 60000);
  server.stdin.write(JSON.stringify({ jsonrpc: '2.0', method: 'notifications/initialized' }) + '\n');

  const info = await readResource('mcpforunity://project/info');
  const actualRoot = info.projectRoot;
  if (typeof actualRoot !== 'string' || !samePath(actualRoot, projectRoot)) {
    throw new Error(`Wrong Unity project: ${actualRoot ?? '(missing)'}; expected ${projectRoot}`);
  }

  const state = await readResource('mcpforunity://editor/state');
  let scene = state.editor?.active_scene?.path;
  const reasons = state.advice?.blocking_reasons;
  // MCP 10.1.2 rebuilds focused editor snapshots only on state changes, so an
  // idle, responsive editor can exceed its server freshness limit. In that
  // specific case obtain live values on the editor thread; never restamp or
  // bypass its cached status, compilation, project, or Console checks.
  if (Array.isArray(reasons) && reasons.length === 1 && reasons[0] === 'stale_status') {
    const probe = toolData(await request('tools/call', {
      name: 'execute_code',
      arguments: {
        action: 'execute',
        code: `return new {
          projectRoot = System.IO.Path.GetDirectoryName(UnityEngine.Application.dataPath),
          scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().path,
          isCompiling = UnityEditor.EditorApplication.isCompiling,
          isUpdating = UnityEditor.EditorApplication.isUpdating,
          isChangingPlayMode = UnityEditor.EditorApplication.isPlaying != UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode
        };`,
      },
    }), 'live editor probe');
    const live = probe?.data?.result;
    if (probe.success !== true || !live || !samePath(live.projectRoot ?? '', projectRoot) ||
        live.isCompiling !== false || live.isUpdating !== false || live.isChangingPlayMode !== false) {
      throw new Error(`Live Unity Editor is not ready: ${JSON.stringify(probe)}`);
    }
    scene = live.scene;
    console.log('MCP snapshot stale; live read-only editor probe confirms target and readiness.');
  } else if (state.advice?.ready_for_tools !== true ||
      state.compilation?.is_compiling === true || state.assets?.is_updating === true) {
    throw new Error(`Unity Editor is not ready: ${JSON.stringify(state.advice?.blocking_reasons ?? state.advice ?? state)}`);
  }
  if (typeof scene !== 'string' || !scene) throw new Error('No active Unity scene path');

  const consoleResult = await request('tools/call', {
    name: 'read_console',
    arguments: { action: 'get', types: ['error'], count: 20, format: 'json' },
  });
  if (consoleResult?.isError) throw new Error(`read_console failed: ${JSON.stringify(consoleResult.content)}`);
  let consoleData = consoleResult?.structuredContent;
  if (!consoleData) {
    const text = consoleResult?.content?.find(item => typeof item.text === 'string')?.text;
    if (!text) throw new Error('read_console returned no structured content');
    consoleData = JSON.parse(text);
  }
  if (consoleData.success !== true || !Array.isArray(consoleData.data)) {
    throw new Error(`Unexpected read_console response: ${JSON.stringify(consoleData)}`);
  }
  if (consoleData.data.length) {
    throw new Error(`${consoleData.data.length} Unity Console error(s): ${JSON.stringify(consoleData.data).slice(0, 2000)}`);
  }
  console.log(`PASS Unity MCP: ${actualRoot}; scene=${scene}; ready=true; Console errors=0`);
} catch (error) {
  console.error(`FAIL Unity MCP gate: ${error.message}`);
  process.exitCode = 1;
} finally {
  failPending(new Error('Gate finished'));
  server.stdin.end();
  server.kill();
}
