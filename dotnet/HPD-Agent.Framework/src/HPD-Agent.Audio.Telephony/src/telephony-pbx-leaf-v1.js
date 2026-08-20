import dgram from 'node:dgram';
import crypto from 'node:crypto';

export class TelephonyPbxLeafV1 {
  #sessionId;
  #generation;
  #state = 'proposed';

  constructor({ sessionId, transportGeneration }) {
    if (!sessionId || !transportGeneration) throw new TypeError('telephony-authority-invalid');
    this.#sessionId = sessionId;
    this.#generation = transportGeneration;
  }

  get state() { return this.#state; }

  bind(authority) {
    this.#require(authority);
    if (this.#state !== 'proposed') return { kind: 'refused', safeCode: 'telephony-transition-invalid' };
    this.#state = 'bound';
    return { kind: 'completed' };
  }

  async callEcho(authority, options) {
    this.#require(authority);
    if (this.#state !== 'bound') return { kind: 'refused', safeCode: 'telephony-transition-invalid' };
    this.#state = 'active';
    try {
      const evidence = await executeSipRtpEcho(options);
      this.#state = 'stopped';
      return { kind: 'completed', ...evidence };
    } catch (error) {
      this.#state = 'stopped';
      return { kind: 'unavailable', safeCode: 'telephony-provider-unavailable', detail: error.message };
    }
  }

  #require(authority) {
    if (!authority || authority.sessionId !== this.#sessionId || authority.transportGeneration !== this.#generation)
      throw new Error('telephony-authority-stale');
  }
}

async function executeSipRtpEcho({ host, advertisedHost, sipPort = 5060, localSipPort = 5062, localRtpPort = 12000, timeoutMilliseconds = 10_000 }) {
  const sip = dgram.createSocket('udp4');
  const rtp = dgram.createSocket('udp4');
  await bind(sip, localSipPort);
  await bind(rtp, localRtpPort);
  const callId = `${crypto.randomUUID()}@${advertisedHost}`;
  const fromTag = crypto.randomBytes(8).toString('hex');
  const branch = `z9hG4bK-${crypto.randomBytes(8).toString('hex')}`;
  const uri = `sip:echo@${host}:${sipPort}`;
  const contact = `<sip:hpd@${advertisedHost}:${localSipPort}>`;
  const sdp = ['v=0', `o=hpd 1 1 IN IP4 ${advertisedHost}`, 's=HPD Audio PBX Qualification', `c=IN IP4 ${advertisedHost}`, 't=0 0', `m=audio ${localRtpPort} RTP/AVP 0`, 'a=rtpmap:0 PCMU/8000', 'a=sendrecv', ''].join('\r\n');
  const base = { uri, callId, fromTag, branch, contact, advertisedHost, localSipPort };
  try {
    sendSip(sip, host, sipPort, invite(base, sdp));
    const ok = await waitSip(sip, message => message.startsWith('SIP/2.0 200'), timeoutMilliseconds);
    const to = header(ok, 'To');
    sendSip(sip, host, sipPort, request('ACK', base, to, 1));
    const remote = parseAudio(ok);
    const received = [];
    rtp.on('message', packet => { if (packet.length >= 172 && (packet[1] & 0x7f) === 0) received.push(packet); });
    for (let sequence = 0; sequence < 8; sequence++) {
      const packet = pcmuPacket(sequence, sequence * 160);
      rtp.send(packet, remote.port, remote.host);
      await delay(20);
    }
    await waitUntil(() => received.length >= 3, timeoutMilliseconds);
    sendSip(sip, host, sipPort, request('BYE', base, to, 2));
    return { provider: 'asterisk', codec: 'PCMU/8000', sentPackets: 8, receivedPackets: received.length, callId };
  } finally {
    sip.close();
    rtp.close();
  }
}

function invite(b, sdp) { return request('INVITE', b, `<sip:echo@${b.uri.split('@')[1]}>`, 1, sdp); }
function request(method, b, to, cseq, body = '') {
  return [`${method} ${b.uri} SIP/2.0`, `Via: SIP/2.0/UDP ${b.advertisedHost}:${b.localSipPort};branch=${b.branch};rport`, `From: <sip:hpd@${b.advertisedHost}>;tag=${b.fromTag}`, `To: ${to}`, `Call-ID: ${b.callId}`, `CSeq: ${cseq} ${method}`, `Contact: ${b.contact}`, 'Max-Forwards: 70', ...(body ? ['Content-Type: application/sdp'] : []), `Content-Length: ${Buffer.byteLength(body)}`, '', body].join('\r\n');
}
function sendSip(socket, host, port, value) { socket.send(Buffer.from(value), port, host); }
function bind(socket, port) { return new Promise((resolve, reject) => { socket.once('error', reject); socket.bind(port, '0.0.0.0', resolve); }); }
function waitSip(socket, predicate, timeout) { return new Promise((resolve, reject) => { const timer = setTimeout(() => reject(new Error('sip-response-timeout')), timeout); const listener = buffer => { const value = buffer.toString(); if (!predicate(value)) return; clearTimeout(timer); socket.off('message', listener); resolve(value); }; socket.on('message', listener); }); }
function header(message, name) { return message.split(/\r?\n/).find(line => line.toLowerCase().startsWith(`${name.toLowerCase()}:`))?.slice(name.length + 1).trim() ?? ''; }
function parseAudio(message) { const body = message.split('\r\n\r\n')[1] ?? ''; const host = /^c=IN IP4 (.+)$/m.exec(body)?.[1]?.trim(); const port = Number(/^m=audio (\d+)/m.exec(body)?.[1]); if (!host || !port) throw new Error('sip-sdp-invalid'); return { host, port }; }
function pcmuPacket(sequence, timestamp) { const packet = Buffer.alloc(172, 0xff); packet[0] = 0x80; packet[1] = 0; packet.writeUInt16BE(sequence, 2); packet.writeUInt32BE(timestamp, 4); packet.writeUInt32BE(0x48504431, 8); return packet; }
function delay(ms) { return new Promise(resolve => setTimeout(resolve, ms)); }
function waitUntil(predicate, timeout) { return new Promise((resolve, reject) => { const started = Date.now(); const timer = setInterval(() => { if (predicate()) { clearInterval(timer); resolve(); } else if (Date.now() - started >= timeout) { clearInterval(timer); reject(new Error('rtp-echo-timeout')); } }, 20); }); }
