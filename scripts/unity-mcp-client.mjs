// Small stdio client for the project's configured Unity MCP server.
import { spawn } from 'node:child_process';
import { createInterface } from 'node:readline';
import { readFile } from 'node:fs/promises';

const server = spawn('uvx', ['--offline', '--from', 'mcpforunityserver==10.1.2',
  'mcp-for-unity', '--transport', 'stdio'], { windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'] });
let next = 1, diagnostic = '';
const pending = new Map();
server.stderr.on('data', b => { diagnostic = (diagnostic + b).slice(-3000); });
server.on('error', fail);
server.on('exit', () => fail(new Error('MCP server exited: ' + diagnostic)));
function fail(error) { for (const p of pending.values()) { clearTimeout(p.timer); p.reject(error); } pending.clear(); }
createInterface({ input: server.stdout }).on('line', line => {
  let m; try { m = JSON.parse(line); } catch { return; }
  const p = pending.get(m.id); if (!p) return;
  pending.delete(m.id); clearTimeout(p.timer);
  if (m.error) p.reject(new Error(JSON.stringify(m.error))); else p.resolve(m.result);
});
function request(method, params, timeout = 60000) {
  return new Promise((resolve, reject) => {
    const id = next++;
    const timer = setTimeout(() => { pending.delete(id); reject(new Error(method + ' timeout: ' + diagnostic)); }, timeout);
    pending.set(id, { resolve, reject, timer });
    server.stdin.write(JSON.stringify({ jsonrpc: '2.0', id, method, params }) + '\n');
  });
}
try {
  await request('initialize', { protocolVersion: '2025-03-26', capabilities: {},
    clientInfo: { name: 'freshguard-unity-client', version: '1.0' } });
  server.stdin.write(JSON.stringify({ jsonrpc: '2.0', method: 'notifications/initialized' }) + '\n');
  const [mode, name, argPath] = process.argv.slice(2);
  let result;
  if (mode === 'resource') result = await request('resources/read', { uri: name });
  else if (mode === 'tools') {
    result = await request('tools/list', {});
    if (name) result = result.tools.filter(t => new RegExp(name).test(t.name));
    else result = result.tools.map(t => ({ name: t.name, description: t.description.slice(0, 150) }));
  } else if (mode === 'code') {
    result = await request('tools/call', { name: 'execute_code', arguments: {
      action: 'execute', code: await readFile(name, 'utf8') } }, 120000);
  } else if (mode === 'tool') {
    const args = argPath ? JSON.parse(await readFile(argPath, 'utf8')) : {};
    result = await request('tools/call', { name, arguments: args }, 120000);
  } else throw new Error('Usage: resource <uri> | tools [regex] | tool <name> [args.json]');
  console.log(JSON.stringify(result));
  if (result?.isError) process.exitCode = 1;
} catch (e) { console.error(e.message); process.exitCode = 1; }
finally { fail(new Error('Client finished')); server.stdin.end(); server.kill(); }
