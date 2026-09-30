import { Room, RoomEvent, Track } from 'livekit-client';

export class LiveKitAudioTransportV1 {
  #sessionId;
  #generation;
  #state = 'proposed';
  #room = null;
  #context = null;
  #oscillator = null;
  #remoteAudio = [];
  #waiters = [];

  constructor({ sessionId, transportGeneration }) {
    if (typeof sessionId !== 'string' || sessionId.length === 0)
      throw new TypeError('sessionId is required');
    if (typeof transportGeneration !== 'string' || transportGeneration.length === 0)
      throw new TypeError('transportGeneration is required');
    this.#sessionId = sessionId;
    this.#generation = transportGeneration;
  }

  get state() { return this.#state; }
  get participantIdentity() { return this.#room?.localParticipant.identity ?? null; }
  get remoteAudioCount() { return this.#remoteAudio.length; }

  bind(authority) {
    this.#requireAuthority(authority);
    if (this.#state !== 'proposed') return this.#refused('livekit-transition-invalid');
    this.#state = 'bound';
    return { kind: 'completed' };
  }

  async connect(authority, url, token) {
    this.#requireAuthority(authority);
    if (this.#state !== 'bound') return this.#refused('livekit-transition-invalid');
    if (typeof url !== 'string' || typeof token !== 'string' || token.length === 0)
      return this.#refused('livekit-connection-argument-invalid');

    const room = new Room({ adaptiveStream: false, dynacast: false });
    room.on(RoomEvent.TrackSubscribed, (track, publication, participant) => {
      if (track.kind !== Track.Kind.Audio) return;
      const evidence = {
        participantIdentity: participant.identity,
        trackSid: publication.trackSid,
        kind: track.kind,
      };
      this.#remoteAudio.push(evidence);
      for (const resolve of this.#waiters.splice(0)) resolve(evidence);
    });
    await room.connect(url, token, { autoSubscribe: true });
    this.#room = room;
    this.#state = 'active';
    return { kind: 'connected', participantIdentity: room.localParticipant.identity };
  }

  async publishVirtualAudio(authority) {
    this.#requireAuthority(authority);
    if (this.#state !== 'active') return this.#refused('livekit-not-active');
    const Context = globalThis.AudioContext ?? globalThis.webkitAudioContext;
    if (!Context) return this.#refused('livekit-web-audio-unavailable');

    this.#context = new Context({ sampleRate: 48_000 });
    const destination = this.#context.createMediaStreamDestination();
    this.#oscillator = this.#context.createOscillator();
    this.#oscillator.frequency.value = 440;
    this.#oscillator.connect(destination);
    this.#oscillator.start();
    await this.#context.resume();
    const track = destination.stream.getAudioTracks()[0];
    const publication = await this.#room.localParticipant.publishTrack(track, {
      source: Track.Source.Microphone,
      name: 'hpd-virtual-audio',
    });
    return { kind: 'published', trackSid: publication.trackSid };
  }

  async waitForRemoteAudio(authority, timeoutMilliseconds = 15_000) {
    this.#requireAuthority(authority);
    if (this.#remoteAudio.length > 0) return this.#remoteAudio[0];
    return await new Promise((resolve, reject) => {
      const timer = setTimeout(() => reject(new Error('livekit-remote-audio-timeout')), timeoutMilliseconds);
      this.#waiters.push(value => {
        clearTimeout(timer);
        resolve(value);
      });
    });
  }

  async disconnect(authority) {
    this.#requireAuthority(authority);
    if (this.#state !== 'active') return this.#refused('livekit-transition-invalid');
    this.#oscillator?.stop();
    if (this.#context && this.#context.state !== 'closed') await this.#context.close();
    await this.#room.disconnect();
    this.#state = 'stopped';
    return { kind: 'completed' };
  }

  #requireAuthority(authority) {
    if (!authority || authority.sessionId !== this.#sessionId || authority.transportGeneration !== this.#generation)
      throw new Error('livekit-authority-stale');
  }

  #refused(safeCode) { return { kind: 'refused', safeCode }; }
}
