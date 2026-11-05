const { test } = require('node:test');
const assert = require('node:assert/strict');
const { EventEmitter, once } = require('node:events');
const net = require('node:net');
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');
const { lineReader } = require('../src/protocol.cjs');
test('app exchanges receiver signaling over TCP and releases session resources', async () => {
  const handlers = new Map(), events = new EventEmitter(); let window, stopped = 0, registered = 0;
  class Window extends EventEmitter {
    constructor() { super(); window = this; this.webContents = new EventEmitter(); this.webContents.mainFrame = {}; this.webContents.send = (channel, data) => events.emit(channel, data); this.webContents.setWindowOpenHandler = () => {}; }
    isDestroyed() { return false; }
    loadFile() {}
  }
  const browser = new EventEmitter(); browser.update = () => {}; browser.stop = () => {};
  class Bonjour { find() { return browser; } destroy() {} }
  const app = new EventEmitter(); app.whenReady = () => Promise.resolve(); app.quit = () => {};
  const electron = { app, BrowserWindow: Window, ipcMain: { handle: (name, fn) => handlers.set(name, fn) }, session: { defaultSession: { setPermissionRequestHandler() {}, setPermissionCheckHandler() {}, setDisplayMediaRequestHandler() {} } }, clipboard: {}, globalShortcut: { unregisterAll() { registered = 0; }, register() { registered++; return true; } }, powerSaveBlocker: { start: () => 1, isStarted: () => true, stop: () => stopped++ } };
  vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../src/main.cjs'), 'utf8'), {
    require: name => name === 'electron' ? electron : name === 'bonjour-service' ? { Bonjour } : name === './protocol.cjs' ? require('../src/protocol.cjs') : require(name),
    __dirname: path.join(__dirname, '../src'), setTimeout, clearTimeout, console
  });
  await Promise.resolve();
  const invoke = (name, ...args) => handlers.get(name)({ sender: window.webContents, senderFrame: window.webContents.mainFrame }, ...args);
  const server = net.createServer(); await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  const connection = once(server, 'connection'); let remote;
  try {
    const connecting = invoke('connect', `127.0.0.1:${server.address().port}`);
    [remote] = await connection;
    const received = new EventEmitter(); remote.on('data', lineReader(message => received.emit(message.type, message)));
    const hello = once(received, 'hello'); await connecting; assert.equal((await hello)[0].deviceName, 'Projector Connect for macOS');
    const offer = once(received, 'offer'); await invoke('signal', { type: 'offer', sdp: 'test-sdp' }); assert.equal((await offer)[0].sdp, 'test-sdp');
    const answer = once(events, 'signal'); remote.write('{"type":"answer","sdp":"answer-sdp"}\n'); assert.equal((await answer)[0].sdp, 'answer-sdp');
    assert.equal(await invoke('keys', true), true); assert.equal(registered, 3);
    const bye = once(received, 'bye'); await invoke('stop'); await bye; assert.equal(registered, 0); assert.equal(stopped, 1);
    assert.throws(() => invoke('signal', { type: 'offer', sdp: 'late' }), /disconnected/);
    assert.throws(() => handlers.get('stop')({ sender: {} }), /Untrusted/);
  } finally { await invoke('stop'); remote?.destroy(); await new Promise(resolve => server.close(resolve)); }
});
