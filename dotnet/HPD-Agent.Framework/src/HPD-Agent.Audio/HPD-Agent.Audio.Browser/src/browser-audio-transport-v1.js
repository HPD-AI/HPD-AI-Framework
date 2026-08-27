const MAX_FRAME_BYTES = 1_048_576;
const MAX_QUEUE_FRAMES = 4_096;

export class BrowserAudioTransportV1 {
  #sessionId;
  #generation;
  #capacity;
  #sampleRate;
  #channels;
  #state = 'proposed';
  #context = null;
  #destination = null;
  #queue = [];
  #nextSequence = 0;

  constructor({ sessionId, transportGeneration, capacity = 8, sampleRate = 48_000, channels = 1 }) {
    if (typeof sessionId !== 'string' || sessionId.length === 0)
      throw new TypeError('sessionId is required');
    if (typeof transportGeneration !== 'string' || transportGeneration.length === 0)
      throw new TypeError('transportGeneration is required');
    if (!Number.isInteger(capacity) || capacity < 1 || capacity > MAX_QUEUE_FRAMES)
      throw new RangeError('capacity is outside the supported range');
    if (!Number.isInteger(sampleRate) || sampleRate < 8_000 || sampleRate > 384_000)
      throw new RangeError('sampleRate is outside the supported range');
    if (!Number.isInteger(channels) || channels < 1 || channels > 32)
      throw new RangeError('channels is outside the supported range');

    this.#sessionId = sessionId;
    this.#generation = transportGeneration;
    this.#capacity = capacity;
    this.#sampleRate = sampleRate;
    this.#channels = channels;
  }

  get state() { return this.#state; }
  get isVirtualDevice() { return true; }
  get queuedFrames() { return this.#queue.length; }
  get captureTrackCount() { return this.#destination?.stream.getAudioTracks().length ?? 0; }

  bind(authority) {
    this.#requireAuthority(authority);
    if (this.#state !== 'proposed') return { kind: 'refused', safeCode: 'browser-transport-transition-invalid' };
    this.#state = 'bound';
    return { kind: 'completed' };
  }

  async start(authority) {
    this.#requireAuthority(authority);
    if (this.#state !== 'bound') return { kind: 'refused', safeCode: 'browser-transport-transition-invalid' };
    const Context = globalThis.AudioContext ?? globalThis.webkitAudioContext;
    if (!Context) return { kind: 'refused', safeCode: 'browser-web-audio-unavailable' };

    this.#context = new Context({ sampleRate: this.#sampleRate });
    this.#destination = this.#context.createMediaStreamDestination();
    await this.#context.resume();
    this.#state = 'active';
    return { kind: 'completed' };
  }

  enqueuePlayout(authority, pcm) {
    this.#requireAuthority(authority);
    if (this.#state !== 'active') return { kind: 'refused', safeCode: 'browser-transport-not-active' };
    if (!(pcm instanceof Float32Array) || pcm.byteLength === 0 || pcm.byteLength > MAX_FRAME_BYTES)
      return { kind: 'refused', safeCode: 'browser-frame-invalid' };
    if (pcm.length % this.#channels !== 0)
      return { kind: 'refused', safeCode: 'browser-frame-geometry-invalid' };
    if (this.#queue.length >= this.#capacity)
      return { kind: 'refused', safeCode: 'browser-transport-capacity-refused' };

    const owned = new Float32Array(pcm);
    const sequence = ++this.#nextSequence;
    this.#queue.push({ sequence, owned });
    return { kind: 'accepted', sequence };
  }

  renderNext(authority) {
    this.#requireAuthority(authority);
    if (this.#state !== 'active') return { kind: 'refused', safeCode: 'browser-transport-not-active' };
    const frame = this.#queue.shift();
    if (!frame) return { kind: 'end' };

    const sampleFrames = frame.owned.length / this.#channels;
    const buffer = this.#context.createBuffer(this.#channels, sampleFrames, this.#sampleRate);
    for (let channel = 0; channel < this.#channels; channel++) {
      const values = buffer.getChannelData(channel);
      for (let frameIndex = 0; frameIndex < sampleFrames; frameIndex++)
        values[frameIndex] = frame.owned[(frameIndex * this.#channels) + channel];
    }
    const source = this.#context.createBufferSource();
    source.buffer = buffer;
    source.connect(this.#destination);
    source.start();
    return { kind: 'rendered', sequence: frame.sequence, sampleFrames };
  }

  async stop(authority) {
    this.#requireAuthority(authority);
    if (this.#state !== 'active') return { kind: 'refused', safeCode: 'browser-transport-transition-invalid' };
    this.#queue.length = 0;
    for (const track of this.#destination.stream.getTracks()) track.stop();
    await this.#context.close();
    this.#state = 'stopped';
    return { kind: 'completed' };
  }

  #requireAuthority(authority) {
    if (!authority || authority.sessionId !== this.#sessionId || authority.transportGeneration !== this.#generation)
      throw new Error('browser-transport-authority-stale');
  }
}
