const { StringDecoder } = require('node:string_decoder');
const MAX_LINE = 1_048_576;
function target(value) {
  if (typeof value !== 'string' || !value.trim() || /[\s/@?#]/.test(value.trim())) throw Error('Enter a TV address or hostname, with an optional port.');
  let url;
  try { url = new URL(`tcp://${value.trim()}`); } catch { throw Error('Invalid receiver address. Use tv.local:53530 or [IPv6]:53530.'); }
  const host = url.hostname.replace(/^\[|\]$/g, '');
  const port = Number(url.port || 53530);
  if (!host || !Number.isInteger(port) || port < 1 || port > 65535) throw Error('Invalid receiver port.');
  return { host, port };
}
function lineReader(onMessage) {
  const decoder = new StringDecoder('utf8'); let pending = '';
  return chunk => {
    pending += decoder.write(chunk);
    let index;
    while ((index = pending.indexOf('\n')) >= 0) {
      if (index > MAX_LINE) throw Error('Signaling message too large.');
      const line = pending.slice(0, index); pending = pending.slice(index + 1);
      let message;
      try { message = JSON.parse(line); } catch { throw Error('Invalid receiver signaling JSON.'); }
      if (message && typeof message.type === 'string') onMessage(message);
    }
    if (pending.length > MAX_LINE) throw Error('Signaling message too large.');
  };
}
function receiver(service) {
  const txt = service.txt || {};
  const field = key => Buffer.isBuffer(txt[key]) ? txt[key].toString('utf8') : String(txt[key] ?? '');
  const id = field('id'), name = Buffer.from(field('name64'), 'base64').toString('utf8').trim();
  if (field('protocol') !== '1' || !id || !name || !service.port) return null;
  const host = service.addresses?.find(x => /^\d+\.\d+\.\d+\.\d+$/.test(x)) || service.host;
  return host ? { id, name, host, port: service.port, busy: field('busy') === '1' } : null;
}
module.exports = { target, lineReader, receiver, MAX_LINE };
