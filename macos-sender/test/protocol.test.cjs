const { test } = require('node:test');
const assert = require('node:assert/strict');
const { target, lineReader, receiver, MAX_LINE } = require('../src/protocol.cjs');
test('manual connections accept hostnames and bracketed IPv6 without accepting URLs', () => {
  assert.deepEqual(target('tv.local'), { host: 'tv.local', port: 53530 });
  assert.deepEqual(target('[::1]:1234'), { host: '::1', port: 1234 });
  for (const invalid of ['', 'https://tv', 'user@tv', 'tv/path', 'tv:0', 'tv:65536']) assert.throws(() => target(invalid));
});
test('TCP framing handles fragmented UTF-8 and coalesced messages', () => {
  const result = [], read = lineReader(message => result.push(message));
  const bytes = Buffer.from('{"type":"answer","sdp":"é"}\n{"type":"bye"}\n');
  for (const byte of bytes) read(Buffer.from([byte]));
  assert.deepEqual(result, [{ type: 'answer', sdp: 'é' }, { type: 'bye' }]);
});
test('oversized and invalid receiver messages fail closed', () => {
  assert.throws(() => lineReader(() => {})(Buffer.from('x'.repeat(MAX_LINE + 1))));
  assert.throws(() => lineReader(() => {})(Buffer.from('invalid\n')));
});
test('discovery validates protocol and preserves receiver identities', () => {
  const service = { host: 'tv.local', port: 53530, txt: { protocol: Buffer.from('1'), id: Buffer.from('uuid'), name64: Buffer.from(Buffer.from('TV pokój').toString('base64')), busy: Buffer.from('1') } };
  assert.deepEqual(receiver(service), { id: 'uuid', name: 'TV pokój', host: 'tv.local', port: 53530, busy: true });
  assert.equal(receiver({ ...service, txt: { ...service.txt, protocol: '2' } }), null);
});
