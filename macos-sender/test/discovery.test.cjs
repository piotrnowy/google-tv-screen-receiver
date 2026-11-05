const { test } = require('node:test');
const assert = require('node:assert/strict');
const { EventEmitter } = require('node:events');
const { PassThrough } = require('node:stream');
const { ReceiverDiscovery } = require('../src/discovery.cjs');
function harness() {
  const children = [], errors = [];
  const discovery = new ReceiverDiscovery({
    onDevices() {}, onDiagnostic: value => errors.push(value),
    spawnProcess: (executable, args) => {
      assert.equal(executable, '/usr/bin/dns-sd');
      const child = new EventEmitter(); child.args = args; child.stdout = new PassThrough(); child.stderr = new PassThrough(); child.killed = false;
      child.kill = () => { child.killed = true; child.emit('close', 0); };
      children.push(child); return child;
    }
  });
  discovery.start();
  const child = (flag, name) => children.findLast(c => c.args[0] === flag && (!name || c.args.includes(name)) && !c.killed);
  const line = (target, value) => target.stdout.write(value + '\n');
  const browse = (action, name = 'ScreenReceiver-a', network = 6) => line(child('-B'), `12:00:00.000 ${action} 2 ${network} local. _screenreceiver._tcp. ${name}`);
  const resolve = (name, host, id) => {
    const resolver = child('-L', name);
    line(resolver, `12:00:00.000 ${name}._screenreceiver._tcp.local. can be reached at ${host}:53530 (interface 6)`);
    line(resolver, ` name64=${Buffer.from('Bedroom projector').toString('base64')} protocol=1 id=${id} busy=0`);
    line(child('-G', host), `12:00:00.000 Add 40000002 6 ${host} 192.168.10.172 120`);
  };
  return { discovery, children, errors, child, line, browse, resolve };
}
test('native discovery combines fragmented browse, service and address callbacks', () => {
  const h = harness();
  h.child('-B').stdout.write('12:00:00.000 Add 2 6 local. _screenreceiver.');
  h.child('-B').stdout.write('_tcp. ScreenReceiver-a\n');
  assert.equal(h.discovery.devices.length, 0);
  h.line(h.child('-L'), ` name64=${Buffer.from('Projektor sypialni').toString('base64')} protocol=1 id=a busy=0`);
  h.line(h.child('-L'), '12:00:00.000 service can be reached at android.local.:53530 (interface 6)');
  h.line(h.child('-G'), '12:00:00.000 Add 40000002 6 android.local. 192.168.10.172 120');
  assert.deepEqual(h.discovery.devices, [{ id: 'a', name: 'Projektor sypialni', host: '192.168.10.172', port: 53530, busy: false }]);
  h.discovery.stop(); assert.ok(h.children.every(c => c.killed));
});
test('busy changes and duplicate interface removal update cards correctly', () => {
  const h = harness(); h.browse('Add'); h.browse('Add', 'ScreenReceiver-a', 7); h.resolve('ScreenReceiver-a', 'android.local.', 'a');
  assert.equal(h.children.filter(c => c.args[0] === '-L').length, 1);
  h.line(h.child('-L'), ` name64=${Buffer.from('Bedroom projector').toString('base64')} protocol=1 id=a busy=1`);
  assert.equal(h.discovery.devices[0].busy, true);
  h.browse('Rmv'); assert.equal(h.discovery.devices.length, 1);
  h.browse('Rmv', 'ScreenReceiver-a', 7); assert.deepEqual(h.discovery.devices, []);
  h.discovery.stop();
});
test('host changes after reboot replace the old address resolver', () => {
  const h = harness(); h.browse('Add'); h.resolve('ScreenReceiver-a', 'old.local.', 'a');
  const oldAddress = h.child('-G');
  h.line(h.child('-L'), '12:00:00.000 service can be reached at new.local.:53530 (interface 6)');
  assert.equal(oldAddress.killed, true); assert.equal(h.discovery.devices.length, 0);
  h.line(h.child('-G'), '12:00:00.000 Add 40000002 6 new.local. 192.168.10.200 120');
  assert.equal(h.discovery.devices[0].host, '192.168.10.200');
  h.discovery.stop();
});
test('same friendly names stay separate and invalid protocol is hidden', () => {
  const h = harness(); h.browse('Add'); h.resolve('ScreenReceiver-a', 'a.local.', 'a');
  h.browse('Add', 'ScreenReceiver-b'); h.resolve('ScreenReceiver-b', 'b.local.', 'b');
  assert.deepEqual(h.discovery.devices.map(d => d.id), ['a', 'b']);
  h.line(h.child('-L', 'ScreenReceiver-b'), ` name64=${Buffer.from('Bedroom projector').toString('base64')} protocol=2 id=b busy=0`);
  assert.deepEqual(h.discovery.devices.map(d => d.id), ['a']);
  h.discovery.stop();
});
test('native process failures are visible and Refresh restarts discovery', () => {
  const h = harness(); h.browse('Add'); h.resolve('ScreenReceiver-a', 'android.local.', 'a');
  h.child('-B').emit('close', 1);
  assert.equal(h.discovery.devices.length, 0); assert.match(h.errors[0], /Local Network/);
  h.discovery.refresh(); h.browse('Add'); h.resolve('ScreenReceiver-a', 'android.local.', 'a');
  assert.equal(h.discovery.devices.length, 1);
  h.discovery.stop(); assert.ok(h.children.every(c => c.killed));
});
