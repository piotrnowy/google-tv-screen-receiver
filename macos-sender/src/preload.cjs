const { contextBridge, ipcRenderer } = require('electron');
const api = {};
for (const name of ['browse', 'connect', 'signal', 'stop', 'keys', 'copy']) api[name] = (...args) => ipcRenderer.invoke(name, ...args);
for (const name of ['devices', 'signal', 'ended', 'diagnostic']) api[`on${name[0].toUpperCase()}${name.slice(1)}`] = callback => ipcRenderer.on(name, (_event, value) => callback(value));
contextBridge.exposeInMainWorld('sender', api);
