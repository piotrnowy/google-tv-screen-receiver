const { spawn } = require('node:child_process');
const { isIP } = require('node:net');
const { receiver } = require('./protocol.cjs');
const SERVICE = '_screenreceiver._tcp';

// Use Apple's mDNSResponder cache and interface selection. Do not bind another UDP 5353 browser.
class ReceiverDiscovery {
  constructor({ onDevices, onDiagnostic = () => {}, spawnProcess = spawn }) {
    this.onDevices = onDevices;
    this.onDiagnostic = onDiagnostic;
    this.spawnProcess = spawnProcess;
    this.services = new Map();
    this.browse = null;
    this.devices = [];
  }

  watch(args, onLine, onExit) {
    let child;
    try { child = this.spawnProcess('/usr/bin/dns-sd', args, { stdio: ['ignore', 'pipe', 'pipe'], env: { ...process.env, LC_ALL: 'C' } }); }
    catch (error) { this.onDiagnostic(`Bonjour: ${error.message}`); onExit(); return { stop() {} }; }
    let active = true, pending = '';
    const failed = message => {
      if (!active) return;
      active = false;
      child.kill();
      this.onDiagnostic(message);
      onExit();
    };
    child.stdout.setEncoding('utf8');
    child.stderr.setEncoding('utf8');
    child.stdout.on('data', chunk => {
      if (!active) return;
      pending += chunk;
      if (pending.length > 65536) { failed('Bonjour returned an oversized response.'); return; }
      let end;
      while ((end = pending.indexOf('\n')) !== -1) {
        const line = pending.slice(0, end).trimEnd(); pending = pending.slice(end + 1);
        onLine(line);
      }
    });
    child.stderr.on('data', chunk => { if (active) this.onDiagnostic(`Bonjour: ${chunk.trim().slice(0, 1000)}`); });
    child.on('error', error => failed(`Bonjour: ${error.message}`));
    child.on('close', code => failed(`Bonjour stopped (exit ${code}). Check Local Network access for Projector Connect in macOS Settings, then Refresh.`));
    return { stop() { active = false; child.kill(); } };
  }

  start() {
    if (this.browse) return;
    this.browse = this.watch(['-B', SERVICE, 'local.'], line => this.browseLine(line), () => {
      this.browse = null;
      this.clearServices();
    });
  }

  browseLine(line) {
    const row = /^\S+\s+(Add|Rmv)\s+\S+\s+(\d+)\s+(\S+)\s+(_screenreceiver\._tcp\.?)\s+(.+)$/i.exec(line);
    if (!row || row[3].toLowerCase() !== 'local.') return;
    const [, action, network, domain, , rawName] = row;
    const name = rawName.trim();
    if (!name || name.length > 255) return;
    const key = `${name.toLowerCase()}.${domain.toLowerCase()}`;
    let entry = this.services.get(key);
    if (action === 'Rmv') {
      if (!entry) return;
      entry.interfaces.delete(network);
      if (!entry.interfaces.size) { this.services.delete(key); entry.resolve?.stop(); entry.address?.stop(); this.publish(); }
      return;
    }
    if (entry) { entry.interfaces.add(network); return; }
    if (this.services.size >= 32) return;
    entry = { interfaces: new Set([network]), txt: {}, host: '', port: 0, addresses: new Map() };
    this.services.set(key, entry);
    entry.resolve = this.watch(['-L', name, SERVICE, domain], text => this.resolveLine(entry, text), () => {
      entry.txt = {}; this.publish();
    });
  }

  resolveLine(entry, line) {
    const location = /can be reached at (\S+):(\d+) \(interface \d+\)/.exec(line);
    if (location) {
      const host = location[1], port = Number(location[2]);
      if (port < 1 || port > 65535) return;
      entry.port = port;
      if (entry.host !== host) {
        entry.address?.stop(); entry.addresses.clear(); entry.host = host;
        entry.address = this.watch(['-G', 'v4', host], text => this.addressLine(entry, text), () => {
          entry.addresses.clear(); this.publish();
        });
      }
      this.publish();
    }
    if (/\bprotocol=|\bname64=|\bid=/.test(line)) {
      entry.txt = Object.fromEntries([...line.matchAll(/(?:^|\s)([A-Za-z0-9]+)=([^\s]*)/g)].map(match => [match[1], match[2]]));
      this.publish();
    }
  }

  addressLine(entry, line) {
    const row = /^\S+\s+(Add|Rmv)\s+\S+\s+(\d+)\s+\S+\s+(\S+)\s+\d+$/.exec(line);
    if (!row || isIP(row[3]) !== 4) return;
    const [, action, network, address] = row;
    const interfaces = entry.addresses.get(address) || new Set();
    if (action === 'Add') interfaces.add(network); else interfaces.delete(network);
    if (interfaces.size) entry.addresses.set(address, interfaces); else entry.addresses.delete(address);
    this.publish();
  }

  publish() {
    const byId = new Map();
    for (const entry of this.services.values()) {
      if (!entry.addresses.size) continue;
      const device = receiver({ ...entry, addresses: [...entry.addresses.keys()] });
      if (device) byId.set(device.id, device);
    }
    const next = [...byId.values()].sort((a, b) => a.id.localeCompare(b.id));
    if (JSON.stringify(next) === JSON.stringify(this.devices)) return;
    this.devices = next;
    this.onDevices(next);
  }

  clearServices() {
    for (const entry of this.services.values()) { entry.resolve?.stop(); entry.address?.stop(); }
    this.services.clear(); this.publish();
  }
  stop() { this.browse?.stop(); this.browse = null; this.clearServices(); }
  refresh() { this.stop(); this.start(); }
}
module.exports = { ReceiverDiscovery };
