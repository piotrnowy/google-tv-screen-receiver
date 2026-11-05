const { app, BrowserWindow, ipcMain, session, clipboard, globalShortcut, powerSaveBlocker } = require('electron');
const net = require('node:net');
const path = require('node:path');
const { ReceiverDiscovery } = require('./discovery.cjs');
const { createDisplayCaptureHandler } = require('./capture.cjs');
const { target, lineReader, connectionError, MAX_LINE } = require('./protocol.cjs');
let window, socket, discovery, blocker, timer, authorizedCapture = false, playLocally = false;
const emit = (channel, value) => { if (window && !window.isDestroyed()) window.webContents.send(channel, value); };
function cleanup() {
  clearTimeout(timer); authorizedCapture = false;
  if (socket) { socket.end(JSON.stringify({ type: 'bye' }) + '\n'); socket.destroy(); socket = null; }
  globalShortcut.unregisterAll();
  if (blocker !== undefined && powerSaveBlocker.isStarted(blocker)) powerSaveBlocker.stop(blocker);
  blocker = undefined;
}
function send(message) {
  if (!socket || socket.destroyed) throw Error('Receiver is disconnected.');
  const line = JSON.stringify(message);
  if (line.length > MAX_LINE) throw Error('Signaling message too large.');
  socket.write(line + '\n');
}
app.whenReady().then(() => {
  window = new BrowserWindow({ width: 900, height: 740, minWidth: 760, minHeight: 560, backgroundColor: '#0e151f', title: 'Projector Connect', webPreferences: { preload: path.join(__dirname, 'preload.cjs'), contextIsolation: true, nodeIntegration: false, sandbox: true } });
  const trusted = event => event.sender === window.webContents && event.senderFrame === window.webContents.mainFrame;
  const handle = (name, callback) => ipcMain.handle(name, (event, ...args) => { if (!trusted(event)) throw Error('Untrusted request.'); return callback(...args); });
  window.webContents.setWindowOpenHandler(() => ({ action: 'deny' }));
  window.webContents.on('will-navigate', event => event.preventDefault());
  session.defaultSession.setPermissionRequestHandler((contents, permission, callback) => callback(contents === window.webContents && permission === 'display-capture' && authorizedCapture));
  session.defaultSession.setPermissionCheckHandler((contents, permission) => contents === window.webContents && permission === 'display-capture' && authorizedCapture);
  // The Electron wrapper's useSystemPicker:true path drops audio. Supply the same
  // native-picker source explicitly, together with loopback when requested.
  session.defaultSession.setDisplayMediaRequestHandler(createDisplayCaptureHandler({
    authorized: () => authorizedCapture,
    mainFrame: () => window.webContents.mainFrame,
    playLocally: () => playLocally,
    diagnostic: message => emit('diagnostic', message)
  }), { useSystemPicker: false });
  handle('browse', () => discovery.devices);
  handle('refresh', () => { discovery.refresh(); return discovery.devices; });
  handle('connect', async (address, options = {}) => {
    cleanup(); const destination = target(address);
    playLocally = options?.playLocally === true;
    await new Promise((resolve, reject) => {
      const connection = net.createConnection(destination); socket = connection;
      const fail = error => {
        emit('diagnostic', `[Signaling] ${error.message}`);
        const failure = connectionError(error);
        reject(failure);
        if (socket === connection) { cleanup(); emit('ended', failure.message); }
      };
      connection.setNoDelay(true);
      timer = setTimeout(() => fail(Error('Connection timed out. Check the TV and local network.')), 10000);
      connection.on('error', fail);
      const read = lineReader(message => emit('signal', message));
      connection.on('data', chunk => { try { read(chunk); } catch (error) { fail(error); } });
      connection.on('close', () => { reject(Error('Connection closed.')); if (socket === connection) { cleanup(); emit('ended', 'Receiver closed the connection.'); } });
      connection.once('connect', () => { clearTimeout(timer); authorizedCapture = true; send({ type: 'hello', deviceName: 'Projector Connect for macOS' }); blocker = powerSaveBlocker.start('prevent-display-sleep'); resolve(); });
    });
  });
  handle('signal', message => {
    if (!message || !['offer', 'ice', 'getVolume', 'volume'].includes(message.type)) throw Error('Unsupported signaling message.');
    send(message);
  });
  handle('stop', cleanup);
  handle('keys', enabled => {
    globalShortcut.unregisterAll();
    if (!enabled) return true;
    return [['VolumeUp', 'up'], ['VolumeDown', 'down'], ['VolumeMute', 'muteToggle']].map(([key, action]) => globalShortcut.register(key, () => { try { send({ type: 'volume', action }); } catch {} })).every(Boolean);
  });
  handle('copy', text => { if (typeof text === 'string') clipboard.writeText(text.slice(-32000)); });
  discovery = new ReceiverDiscovery({
    onDevices: devices => {
      console.info(`[Discovery] ${devices.length} receiver(s): ${devices.map(device => device.name).join(', ')}`);
      emit('devices', devices);
    },
    onDiagnostic: message => { console.warn(message); emit('diagnostic', message); }
  });
  discovery.start();
  window.on('closed', () => { cleanup(); discovery.stop(); app.quit(); });
  window.webContents.on('render-process-gone', cleanup);
  window.webContents.on('unresponsive', cleanup);
  window.loadFile(path.join(__dirname, 'index.html'));
});
app.on('before-quit', cleanup);
