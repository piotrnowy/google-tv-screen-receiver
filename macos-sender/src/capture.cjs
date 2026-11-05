// Electron 45.2.1's useSystemPicker wrapper grants only { video }, dropping requested audio.
// Use the identical Chromium native-picker source and explicitly attach system loopback.
// Source: https://github.com/electron/electron/blob/v45.2.1/lib/browser/api/session.ts
// macOS still owns both the screen-selection UI and the system-audio permission prompt.
function createDisplayCaptureHandler({ authorized, mainFrame, playLocally = () => false, diagnostic = () => {} }) {
  let selection = -1;
  return (request, callback) => {
    if (!authorized() || !request.frame || request.frame !== mainFrame() || !request.videoRequested) {
      callback(null); return;
    }
    const streams = { video: { id: `window:-4:${selection--}`, name: '' } };
    // CoreAudio suppresses hardware playback for the lifetime of the muted tap;
    // stopping the audio track releases it without modifying the Mac's volume.
    if (request.audioRequested) streams.audio = playLocally() ? 'loopback' : 'loopbackWithMute';
    diagnostic(`[Capture] Native screen picker; system audio ${request.audioRequested ? streams.audio : 'off'}`);
    callback(streams);
  };
}
module.exports = { createDisplayCaptureHandler };
