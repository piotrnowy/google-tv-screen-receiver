const { test } = require('node:test');
const assert = require('node:assert/strict');
const { createDisplayCaptureHandler } = require('../src/capture.cjs');
function setup(playLocally = () => false) {
  const frame = {}; let allowed = true, result;
  const handler = createDisplayCaptureHandler({ authorized: () => allowed, mainFrame: () => frame, playLocally });
  const request = { frame, videoRequested: true, audioRequested: true };
  const invoke = value => { handler(value, streams => { result = streams; }); return result; };
  return { request, invoke, deny: () => { allowed = false; } };
}
test('projector-only sharing defaults to native loopback with local playback suppressed', () => {
  const h = setup();
  const first = h.invoke(h.request), second = h.invoke(h.request);
  assert.equal(first.audio, 'loopbackWithMute');
  assert.match(first.video.id, /^window:-4:-\d+$/);
  assert.notEqual(first.video.id, second.video.id);
  assert.equal(first.video.name, '');
});
test('mirror mode is opt-in and does not carry into the next projector-only capture', () => {
  let mirror = true;
  const h = setup(() => mirror);
  assert.equal(h.invoke(h.request).audio, 'loopback');
  mirror = false;
  assert.equal(h.invoke(h.request).audio, 'loopbackWithMute');
});
test('video-only sharing never requests audio', () => {
  const h = setup();
  assert.equal(Object.hasOwn(h.invoke({ ...h.request, audioRequested: false }), 'audio'), false);
});
test('untrusted frames, audio-only requests and inactive sessions are denied', () => {
  const h = setup();
  assert.equal(h.invoke({ ...h.request, frame: {} }), null);
  assert.equal(h.invoke({ ...h.request, frame: null }), null);
  assert.equal(h.invoke({ ...h.request, videoRequested: false }), null);
  h.deny(); assert.equal(h.invoke(h.request), null);
});
