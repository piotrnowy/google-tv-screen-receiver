/* global sender */
const $ = id => document.getElementById(id);
let devices = [], selected, phase = 'ready', peer, stream, generation = 0, timeout, disconnectTimer, activeName = '', lastTarget, discoveryFailed = false;
let remoteReady = false, pendingIce = [], inbound = Promise.resolve();
function log(message) { $('logs').textContent = ($('logs').textContent + `${new Date().toLocaleTimeString()} ${message}\n`).slice(-32000); }
function render() {
  const idle = phase === 'ready' || phase === 'error';
  $('primary').textContent = ({ ready: 'Share screen', connecting: 'Connecting...', connected: 'Stop sharing', error: 'Try again' })[phase];
  $('primary').disabled = !(phase === 'connected' || (idle && (phase === 'error' ? lastTarget : devices.some(d => d.id === selected && !d.busy))));
  $('cancel').hidden = phase !== 'connecting';
  for (const id of ['audio', 'address', 'connect']) $(id).disabled = !idle;
  $('mirror').disabled = !idle || !$('audio').checked;
  $('keys').disabled = !idle || !$('audio').checked || $('mirror').checked;
  $('badge').textContent = ({ ready: 'Ready', connecting: 'Connecting', connected: 'Connected', error: 'Error' })[phase];
  $('devices').replaceChildren(...devices.map(device => {
    const button = document.createElement('button'); button.className = `device${selected === device.id ? ' selected' : ''}`;
    button.disabled = !idle || device.busy; button.setAttribute('aria-pressed', String(selected === device.id));
    const text = document.createElement('span'), name = document.createElement('strong'), address = document.createElement('small'), badge = document.createElement('span');
    name.textContent = device.name; address.textContent = device.host; badge.textContent = device.busy ? 'In use' : 'Available';
    text.append(name, address); button.append(text, badge); button.onclick = () => { selected = device.id; render(); }; return button;
  }));
  $('empty').hidden = devices.length > 0;
  $('count').textContent = devices.length ? `${devices.length} device${devices.length === 1 ? '' : 's'} found` : discoveryFailed ? 'Discovery unavailable' : 'Looking for nearby devices...';
}
function status(title, help) { $('title').textContent = title; $('help').textContent = help; }
async function stop(error) {
  generation++; clearTimeout(timeout); clearTimeout(disconnectTimer);
  if (peer) { peer.onconnectionstatechange = null; peer.onicecandidate = null; peer.close(); peer = null; }
  if (stream) { stream.getTracks().forEach(track => { track.onended = null; track.stop(); }); stream = null; }
  remoteReady = false; pendingIce = [];
  await sender.stop();
  phase = error ? 'error' : 'ready';
  if (error) { log(error); status('Screen sharing stopped', `${error} Open Technical details for more information.`); }
  else status('Ready to share', 'Choose a device, then Share screen.');
  render();
}
async function share(address, name) {
  if (!['ready', 'error'].includes(phase)) return;
  const attempt = ++generation; lastTarget = { address, name }; activeName = name;
  phase = 'connecting'; status(`Connecting to ${name}...`, 'Choose a display in the macOS screen picker.'); render();
  try {
    await sender.connect(address, { playLocally: $('mirror').checked });
    if (attempt !== generation) return;
    timeout = setTimeout(() => stop('The receiver did not connect within 45 seconds.'), 45000);
    const captured = await navigator.mediaDevices.getDisplayMedia({ video: { width: { ideal: 1920, max: 1920 }, height: { ideal: 1080, max: 1080 }, frameRate: { ideal: 30, max: 30 } }, audio: $('audio').checked ? { channelCount: 2, sampleRate: 48000, echoCancellation: false, noiseSuppression: false, autoGainControl: false } : false, systemAudio: $('audio').checked ? 'include' : 'exclude' });
    if (attempt !== generation) { captured.getTracks().forEach(track => track.stop()); return; }
    stream = captured;
    if (!$('audio').checked) stream.getAudioTracks().forEach(track => { stream.removeTrack(track); track.stop(); });
    log(`[Capture] Audio tracks: ${stream.getAudioTracks().map(track => track.readyState).join(', ') || 'none'}`);
    if ($('audio').checked && !stream.getAudioTracks().some(track => track.readyState === 'live')) throw Error('macOS returned no live system audio. Check recording permissions for Projector Connect, then quit and reopen the app from Finder. You can turn System audio off to share video only.');
    peer = new RTCPeerConnection({ iceServers: [], bundlePolicy: 'max-bundle' });
    for (const track of stream.getTracks()) { track.onended = () => { if (attempt === generation) stop(track.kind === 'audio' ? 'macOS stopped system audio capture. Check recording permissions.' : undefined); }; peer.addTransceiver(track, { direction: 'sendonly', streams: [stream] }); }
    const video = peer.getTransceivers().find(t => t.sender.track?.kind === 'video');
    const codecs = RTCRtpSender.getCapabilities('video').codecs.filter(codec => codec.mimeType.toLowerCase() === 'video/h264');
    if (!codecs.length) throw Error('H.264 encoding is unavailable on this Mac.');
    video.setCodecPreferences(codecs);
    const audio = peer.getTransceivers().find(t => t.sender.track?.kind === 'audio');
    if (audio) audio.setCodecPreferences(RTCRtpSender.getCapabilities('audio').codecs.filter(codec => codec.mimeType.toLowerCase() === 'audio/opus'));
    peer.onicecandidate = event => { if (event.candidate && attempt === generation) sender.signal({ type: 'ice', ...event.candidate.toJSON() }).catch(error => stop(error.message)); };
    peer.onconnectionstatechange = async () => {
      if (attempt !== generation) return;
      const state = peer.connectionState; log(`WebRTC: ${state}`);
      if (state === 'connected') {
        clearTimeout(timeout); clearTimeout(disconnectTimer); phase = 'connected'; status(`Sharing with ${activeName}`, `System audio: ${audio ? 'On' : 'Off'} · Local playback: ${audio ? $('mirror').checked ? 'On' : 'Off' : 'Unchanged'}`); render();
        if (audio && !$('mirror').checked && $('keys').checked && !await sender.keys(true)) log('Some volume keys could not be registered. Another app may own them.');
      } else if (state === 'failed') stop('Media connection failed. Check your network and receiver.');
      else if (state === 'disconnected') { clearTimeout(disconnectTimer); disconnectTimer = setTimeout(() => stop('The media connection was lost.'), 10000); }
    };
    const parameters = video.sender.getParameters();
    if (parameters.encodings.length) { parameters.encodings[0].maxBitrate = 6_000_000; parameters.encodings[0].maxFramerate = 30; await video.sender.setParameters(parameters); }
    await peer.setLocalDescription(await peer.createOffer());
    if (attempt === generation) await sender.signal({ type: 'offer', sdp: peer.localDescription.sdp });
  } catch (error) { if (attempt === generation) await stop(error.message); }
}
sender.onSignal(message => {
  const attempt = generation;
  inbound = inbound.then(async () => {
    if (attempt !== generation) return;
    if (message.type === 'bye') { await stop(); return; }
    if (!peer) return;
    if (message.type === 'answer') {
      if (remoteReady) return;
      await peer.setRemoteDescription({ type: 'answer', sdp: message.sdp }); remoteReady = true;
      for (const candidate of pendingIce) await peer.addIceCandidate(candidate);
      pendingIce = [];
    } else if (message.type === 'ice') {
      const candidate = { candidate: message.candidate, sdpMid: message.sdpMid, sdpMLineIndex: message.sdpMLineIndex };
      if (remoteReady) await peer.addIceCandidate(candidate); else pendingIce.push(candidate);
    }
  }).catch(error => { if (attempt === generation) return stop(error.message); });
});
sender.onEnded(message => { if (phase === 'connecting' || phase === 'connected') stop(message); });
sender.onDiagnostic(message => {
  log(message);
  if (!devices.length && message.startsWith('Bonjour')) {
    discoveryFailed = true;
    $('count').textContent = 'Discovery unavailable';
    $('empty').firstElementChild.textContent = 'Automatic discovery is unavailable';
    $('empty').lastElementChild.textContent = 'Check Local Network access for Projector Connect in macOS Settings, then Refresh. You can also connect by IP address.';
  }
});
function updateDevices(value) { devices = value; if (devices.length) discoveryFailed = false; log(`[Discovery] ${devices.length} receiver(s) found`); if (!selected && devices.length === 1) selected = devices[0].id; render(); }
sender.onDevices(updateDevices);
$('refresh').onclick = () => {
  discoveryFailed = false;
  $('empty').firstElementChild.textContent = 'Looking for nearby devices...';
  $('empty').lastElementChild.textContent = 'Make sure the TV or projector is turned on and connected to the same network.';
  sender.refresh().then(updateDevices).catch(error => log(error.message));
};
$('primary').onclick = () => {
  if (phase === 'connected') return stop();
  if (phase === 'error' && lastTarget) return share(lastTarget.address, lastTarget.name);
  const device = devices.find(d => d.id === selected && !d.busy);
  if (device) share(`${device.host.includes(':') ? `[${device.host}]` : device.host}:${device.port}`, device.name);
};
$('cancel').onclick = () => stop();
$('audio').onchange = render;
$('mirror').onchange = render;
$('manual').onsubmit = event => { event.preventDefault(); share($('address').value.trim(), $('address').value.trim()); };
$('copy').onclick = async () => { await sender.copy($('logs').textContent); $('copy').textContent = 'Logs copied'; };
sender.browse().then(updateDevices).catch(error => log(error.message));
setTimeout(() => { if (!devices.length && !discoveryFailed) { $('empty').firstElementChild.textContent = 'No receivers found'; $('count').textContent = 'No devices found'; } }, 10000);
