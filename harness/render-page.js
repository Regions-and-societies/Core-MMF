// Zero-dependency full-page screenshot via Chrome DevTools Protocol.
// node shoot.js <file-url-or-path> <out.png> [widthPx] [scheme]
const { spawn } = require('child_process');
const fs = require('fs');
const path = require('path');

const CHROME = 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';
const PORT = 9333;

const src = process.argv[2];
const out = process.argv[3];
const width = parseInt(process.argv[4] || '1000', 10);
const scheme = process.argv[5] || 'dark';
const url = src.startsWith('file:') || src.startsWith('http') ? src : 'file:///' + path.resolve(src).replace(/\\/g, '/');

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function getJson(p) {
  const res = await fetch('http://127.0.0.1:' + PORT + p);
  return res.json();
}

(async () => {
  const profile = path.join(process.env.TEMP || '.', 'cdp-shot-profile');
  const chrome = spawn(CHROME, [
    '--headless=new',
    '--remote-debugging-port=' + PORT,
    '--user-data-dir=' + profile,
    '--no-first-run', '--no-default-browser-check',
    '--disable-gpu', '--hide-scrollbars', '--force-color-profile=srgb',
    '--allow-file-access-from-files',
    'about:blank'
  ], { stdio: 'ignore' });

  let version = null;
  for (let i = 0; i < 60 && !version; i++) {
    try { version = await getJson('/json/version'); } catch (e) { await sleep(250); }
  }
  if (!version) throw new Error('Chrome did not open a debugging port');

  const ws = new WebSocket(version.webSocketDebuggerUrl);
  await new Promise((res, rej) => { ws.onopen = res; ws.onerror = rej; });

  let id = 0;
  const pending = new Map();
  const events = [];
  ws.onmessage = (m) => {
    const msg = JSON.parse(m.data);
    if (msg.id && pending.has(msg.id)) {
      const { resolve, reject } = pending.get(msg.id);
      pending.delete(msg.id);
      msg.error ? reject(new Error(msg.method + ': ' + msg.error.message)) : resolve(msg.result);
    } else if (msg.method) {
      events.push(msg);
    }
  };
  function send(method, params, sessionId) {
    const mid = ++id;
    return new Promise((resolve, reject) => {
      pending.set(mid, { resolve, reject });
      ws.send(JSON.stringify({ id: mid, method, params: params || {}, sessionId }));
    });
  }
  async function waitFor(method, ms) {
    const until = Date.now() + ms;
    while (Date.now() < until) {
      const i = events.findIndex((e) => e.method === method);
      if (i >= 0) { events.splice(i, 1); return true; }
      await sleep(60);
    }
    return false;
  }

  const { targetId } = await send('Target.createTarget', { url: 'about:blank' });
  const { sessionId } = await send('Target.attachToTarget', { targetId, flatten: true });

  await send('Page.enable', {}, sessionId);
  await send('Runtime.enable', {}, sessionId);
  await send('Emulation.setEmulatedMedia',
    { features: [{ name: 'prefers-color-scheme', value: scheme }] }, sessionId);
  await send('Emulation.setDeviceMetricsOverride',
    { width, height: 1200, deviceScaleFactor: 2, mobile: false }, sessionId);

  await send('Page.navigate', { url }, sessionId);
  await waitFor('Page.loadEventFired', 20000);
  // webfonts + the inline SVG build
  await send('Runtime.evaluate',
    { expression: 'document.fonts.ready.then(() => 1)', awaitPromise: true }, sessionId);
  await sleep(700);

  const h = await send('Runtime.evaluate', {
    expression: 'Math.ceil(Math.max(document.documentElement.scrollHeight, document.body.scrollHeight))',
    returnByValue: true
  }, sessionId);
  const height = h.result.value;

  await send('Emulation.setDeviceMetricsOverride',
    { width, height, deviceScaleFactor: 2, mobile: false }, sessionId);
  await sleep(250);

  const isJpg = /\.jpe?g$/i.test(out);
  const shotParams = { format: isJpg ? 'jpeg' : 'png', captureBeyondViewport: true, fromSurface: true };
  if (isJpg) shotParams.quality = 92;
  const shot = await send('Page.captureScreenshot', shotParams, sessionId);
  fs.writeFileSync(out, Buffer.from(shot.data, 'base64'));

  console.log('wrote ' + out + '  ' + (width * 2) + 'x' + (height * 2) + ' px (css ' + width + 'x' + height + ', ' + scheme + ', ' + (isJpg ? 'jpeg' : 'png') + ')');

  try { await send('Browser.close'); } catch (e) { /* closing races the socket */ }
  ws.close();
  chrome.kill();
  process.exit(0);
})().catch((e) => { console.error('FAILED: ' + e.message); process.exit(1); });
