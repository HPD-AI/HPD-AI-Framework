import Foundation
import ScreenCaptureKit
import AVFoundation
import CoreMedia
import CoreGraphics

// ── C-callable types exposed to C# via P/Invoke ───────────────────────────────

/// Passed to hpd_list_sources. Called once per source discovered.
public typealias HpdSourceCallback = @convention(c) (
    _ sourceId: UInt32,
    _ displayName: UnsafePointer<CChar>,
    _ kind: Int32,          // 0 = screen, 1 = window
    _ width: Int32,
    _ height: Int32
) -> Void

/// Called once when listing is complete (or failed). error is null on success.
public typealias HpdListCompleteCallback = @convention(c) (
    _ error: UnsafePointer<CChar>?
) -> Void

/// Called when a recording session stops (or fails mid-capture).
public typealias HpdStopCallback = @convention(c) (
    _ videoPath: UnsafePointer<CChar>,
    _ durationMs: Int64,
    _ width: Int32,
    _ height: Int32,
    _ frameRate: Int32,
    _ error: UnsafePointer<CChar>?
) -> Void

// ── Audio options ─────────────────────────────────────────────────────────────

struct HpdAudioOptions {
    let captureMic: Bool
    let captureSystemAudio: Bool
    let micGain: Float
    let systemAudioGain: Float
}

// ── Session registry (18.2 availability wrapper) ──────────────────────────────

@available(macCatalyst 18.2, *)
private final class SessionRegistry {
    static let shared = SessionRegistry()
    private init() {}

    private var sessions: [UInt32: HpdCaptureSession] = [:]
    private let lock = NSLock()
    private var nextId: UInt32 = 1

    func allocateId() -> UInt32 {
        lock.lock(); defer { lock.unlock() }
        let id = nextId; nextId &+= 1; return id
    }

    func store(_ session: HpdCaptureSession, id: UInt32) {
        lock.lock(); defer { lock.unlock() }
        sessions[id] = session
    }

    func remove(id: UInt32) -> HpdCaptureSession? {
        lock.lock(); defer { lock.unlock() }
        return sessions.removeValue(forKey: id)
    }
}

// ── C exports ─────────────────────────────────────────────────────────────────

/// Enumerate all capturable screens and windows.
@_cdecl("hpd_list_sources")
public func hpdListSources(
    onSource: HpdSourceCallback,
    onComplete: HpdListCompleteCallback
) {
    guard #available(macCatalyst 18.2, *) else {
        "ScreenCaptureKit requires macCatalyst 18.2 or later.".withCString { onComplete($0) }
        return
    }
    Task {
        do {
            let content = try await SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: true)
            var sourceId: UInt32 = 1

            for display in content.displays {
                let name = "Display \(display.displayID)"
                name.withCString { ptr in
                    onSource(sourceId, ptr, 0, Int32(display.width), Int32(display.height))
                }
                sourceId += 1
            }

            for window in content.windows {
                guard let appName = window.owningApplication?.applicationName,
                      window.isOnScreen,
                      window.frame.width > 100,
                      window.frame.height > 100 else { continue }
                let name = "\(appName) — \(window.title ?? "Window")"
                name.withCString { ptr in
                    onSource(sourceId, ptr, 1, Int32(window.frame.width), Int32(window.frame.height))
                }
                sourceId += 1
            }
            onComplete(nil)
        } catch {
            error.localizedDescription.withCString { onComplete($0) }
        }
    }
}

/// Begin capturing the given source. Returns session handle (> 0) or 0 on immediate failure.
/// Audio flags: captureMic / captureSystemAudio, independent gain 0.0–2.0 each.
@_cdecl("hpd_start_capture")
public func hpdStartCapture(
    sourceId: UInt32,
    isWindow: Bool,
    frameRate: Int32,
    outputPath: UnsafePointer<CChar>,
    captureMic: Bool,
    captureSystemAudio: Bool,
    micGain: Float,
    systemAudioGain: Float,
    onStop: HpdStopCallback
) -> UInt32 {
    guard #available(macCatalyst 18.2, *) else {
        "ScreenCaptureKit requires macCatalyst 18.2 or later.".withCString {
            onStop("", 0, 0, 0, frameRate, $0)
        }
        return 0
    }

    let path = String(cString: outputPath)
    let audioOptions = HpdAudioOptions(
        captureMic: captureMic,
        captureSystemAudio: captureSystemAudio,
        micGain: micGain,
        systemAudioGain: systemAudioGain
    )
    let registry = SessionRegistry.shared
    let sessionId = registry.allocateId()

    Task {
        do {
            let content = try await SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: true)
            var counter: UInt32 = 1
            var filter: SCContentFilter? = nil

            for display in content.displays {
                if counter == sourceId {
                    filter = SCContentFilter(display: display, excludingWindows: [])
                    break
                }
                counter += 1
            }

            if filter == nil {
                for window in content.windows {
                    guard window.owningApplication != nil,
                          window.isOnScreen,
                          window.frame.width > 100,
                          window.frame.height > 100 else { continue }
                    if counter == sourceId {
                        filter = SCContentFilter(desktopIndependentWindow: window)
                        break
                    }
                    counter += 1
                }
            }

            guard let resolvedFilter = filter else {
                "Source id \(sourceId) not found.".withCString { onStop("", 0, 0, 0, frameRate, $0) }
                return
            }

            let session = HpdCaptureSession(
                sessionId: sessionId,
                filter: resolvedFilter,
                frameRate: Int(frameRate),
                outputPath: path,
                audioOptions: audioOptions,
                onStop: onStop
            )
            registry.store(session, id: sessionId)
            try await session.start()
        } catch {
            error.localizedDescription.withCString { onStop("", 0, 0, 0, frameRate, $0) }
        }
    }

    return sessionId
}

/// Return the current cursor position normalised to the given display size.
/// Uses CoreGraphics (available on macCatalyst). Safe to call from any thread. Non-blocking.
@_cdecl("hpd_get_cursor")
public func hpdGetCursor(displayWidth: Int32, displayHeight: Int32, outCx: UnsafeMutablePointer<Double>, outCy: UnsafeMutablePointer<Double>) {
    // CGEventCreate(nil) gives us the current mouse position via Core Graphics.
    // This is available on macCatalyst; NSEvent/NSScreen are AppKit-only.
    guard let event = CGEvent(source: nil) else {
        outCx.pointee = 0.5
        outCy.pointee = 0.5
        return
    }
    let loc = event.location   // CGPoint in screen coordinates (origin = top-left on CG)
    let w = displayWidth > 0 ? Double(displayWidth) : 1.0
    let h = displayHeight > 0 ? Double(displayHeight) : 1.0
    outCx.pointee = max(0.0, min(1.0, loc.x / w))
    outCy.pointee = max(0.0, min(1.0, loc.y / h))
}

/// Stop a running capture session.
@_cdecl("hpd_stop_capture")
public func hpdStopCapture(sessionId: UInt32) {
    guard #available(macCatalyst 18.2, *) else { return }
    SessionRegistry.shared.remove(id: sessionId)?.stop()
}

// ── Capture session ───────────────────────────────────────────────────────────

@available(macCatalyst 18.2, *)
final class HpdCaptureSession: NSObject, SCStreamOutput, AVCaptureAudioDataOutputSampleBufferDelegate {

    private let sessionId: UInt32
    private let filter: SCContentFilter
    private let frameRate: Int
    private let outputPath: String
    private let audioOptions: HpdAudioOptions
    private let onStop: HpdStopCallback

    // ── Video (SCStream → AVAssetWriter) ──────────────────────────────────────
    private var stream: SCStream?
    private var assetWriter: AVAssetWriter?
    private var videoInput: AVAssetWriterInput?
    private var systemAudioInput: AVAssetWriterInput?   // nil when not capturing system audio
    private var pixelBufferAdaptor: AVAssetWriterInputPixelBufferAdaptor?

    private var startTime: CMTime = .invalid
    private var lastPresentationTime: CMTime = .invalid
    private var width: Int = 0
    private var height: Int = 0
    private var startDate: Date = .now

    private let queue = DispatchQueue(label: "com.hpd.recorder.capture", qos: .userInteractive)

    // ── Microphone (AVCaptureSession → AVAssetWriterInput, direct) ───────────
    private var captureSession: AVCaptureSession?
    private var micWriterInput: AVAssetWriterInput?   // written directly into the main MP4

    init(sessionId: UInt32, filter: SCContentFilter, frameRate: Int, outputPath: String,
         audioOptions: HpdAudioOptions, onStop: @escaping HpdStopCallback) {
        self.sessionId = sessionId
        self.filter = filter
        self.frameRate = frameRate
        self.outputPath = outputPath
        self.audioOptions = audioOptions
        self.onStop = onStop
    }

    func start() async throws {
        let cfg = SCStreamConfiguration()
        cfg.minimumFrameInterval = CMTime(value: 1, timescale: CMTimeScale(frameRate))
        cfg.queueDepth = 5
        cfg.showsCursor = true

        // System audio via SCStream (macCatalyst 18.2+)
        if audioOptions.captureSystemAudio {
            cfg.capturesAudio = true
            cfg.sampleRate = 44100
            cfg.channelCount = 2
        }

        let displaySize = await filter.contentRect.size
        self.width = max(2, Int(displaySize.width) & ~1)   // even, min 2
        self.height = max(2, Int(displaySize.height) & ~1)
        cfg.width = self.width
        cfg.height = self.height

        let url = URL(fileURLWithPath: outputPath)
        try? FileManager.default.removeItem(at: url)
        try FileManager.default.createDirectory(
            at: url.deletingLastPathComponent(),
            withIntermediateDirectories: true)

        let writer = try AVAssetWriter(outputURL: url, fileType: .mp4)
        self.assetWriter = writer

        let videoSettings: [String: Any] = [
            AVVideoCodecKey: AVVideoCodecType.h264,
            AVVideoWidthKey: self.width,
            AVVideoHeightKey: self.height,
            AVVideoCompressionPropertiesKey: [
                AVVideoAverageBitRateKey: bitrateFor(width: self.width, height: self.height, fps: frameRate),
                AVVideoMaxKeyFrameIntervalKey: frameRate,
                AVVideoProfileLevelKey: AVVideoProfileLevelH264HighAutoLevel
            ]
        ]

        let videoInput = AVAssetWriterInput(mediaType: .video, outputSettings: videoSettings)
        videoInput.expectsMediaDataInRealTime = true
        self.videoInput = videoInput

        let adaptor = AVAssetWriterInputPixelBufferAdaptor(
            assetWriterInput: videoInput,
            sourcePixelBufferAttributes: [
                kCVPixelBufferPixelFormatTypeKey as String: kCVPixelFormatType_32BGRA,
                kCVPixelBufferWidthKey as String: self.width,
                kCVPixelBufferHeightKey as String: self.height
            ]
        )
        self.pixelBufferAdaptor = adaptor
        writer.add(videoInput)

        // System audio input for AVAssetWriter
        if audioOptions.captureSystemAudio {
            let audioSettings: [String: Any] = [
                AVFormatIDKey: kAudioFormatMPEG4AAC,
                AVSampleRateKey: 44100,
                AVNumberOfChannelsKey: 2,
                AVEncoderBitRateKey: 128_000
            ]
            let sysAudioInput = AVAssetWriterInput(mediaType: .audio, outputSettings: audioSettings)
            sysAudioInput.expectsMediaDataInRealTime = true
            self.systemAudioInput = sysAudioInput
            writer.add(sysAudioInput)
        }

        // Microphone input — written directly into the same MP4 via AVCaptureAudioDataOutput
        if audioOptions.captureMic {
            let micAudioSettings: [String: Any] = [
                AVFormatIDKey: kAudioFormatMPEG4AAC,
                AVSampleRateKey: 44100,
                AVNumberOfChannelsKey: 1,   // mono mic
                AVEncoderBitRateKey: 96_000
            ]
            let micInput = AVAssetWriterInput(mediaType: .audio, outputSettings: micAudioSettings)
            micInput.expectsMediaDataInRealTime = true
            self.micWriterInput = micInput
            writer.add(micInput)
        }

        writer.startWriting()

        let stream = SCStream(filter: filter, configuration: cfg, delegate: nil)
        try stream.addStreamOutput(self, type: .screen, sampleHandlerQueue: queue)
        if audioOptions.captureSystemAudio {
            try stream.addStreamOutput(self, type: .audio, sampleHandlerQueue: queue)
        }
        try await stream.startCapture()
        self.stream = stream
        self.startDate = Date()

        // Start microphone capture via AVCaptureAudioDataOutput (available on macCatalyst)
        if audioOptions.captureMic {
            startMicCapture()
        }
    }

    // ── Microphone capture via AVCaptureAudioDataOutput ────────────────────────

    private func startMicCapture() {
        let session = AVCaptureSession()
        guard let mic = AVCaptureDevice.default(for: .audio),
              let micInput = try? AVCaptureDeviceInput(device: mic),
              session.canAddInput(micInput) else {
            // Mic unavailable — continue without mic track
            return
        }
        session.addInput(micInput)

        // AVCaptureAudioDataOutput is available on macCatalyst (unlike AVCaptureAudioFileOutput)
        let dataOutput = AVCaptureAudioDataOutput()
        let micQueue = DispatchQueue(label: "com.hpd.recorder.mic", qos: .userInteractive)
        dataOutput.setSampleBufferDelegate(self, queue: micQueue)

        guard session.canAddOutput(dataOutput) else { return }
        session.addOutput(dataOutput)

        session.startRunning()
        self.captureSession = session
    }

    // ── Stop ──────────────────────────────────────────────────────────────────

    func stop() {
        captureSession?.stopRunning()
        stream?.stopCapture { [weak self] error in
            self?.finalizeVideo(stopError: error)
        }
    }

    // ── Finalize video ────────────────────────────────────────────────────────

    private func finalizeVideo(stopError: Error?) {
        guard let writer = assetWriter, let input = videoInput else {
            let msg = stopError?.localizedDescription ?? "Writer not initialized"
            msg.withCString { onStop("", 0, Int32(width), Int32(height), Int32(frameRate), $0) }
            return
        }

        input.markAsFinished()
        systemAudioInput?.markAsFinished()
        micWriterInput?.markAsFinished()

        let durMs: Int64
        if lastPresentationTime != .invalid && startTime != .invalid {
            durMs = Int64(CMTimeGetSeconds(CMTimeSubtract(lastPresentationTime, startTime)) * 1000)
        } else {
            durMs = Int64(Date().timeIntervalSince(startDate) * 1000)
        }

        writer.finishWriting { [weak self] in
            guard let self = self else { return }
            if let err = stopError ?? writer.error {
                err.localizedDescription.withCString {
                    self.onStop("", durMs, Int32(self.width), Int32(self.height), Int32(self.frameRate), $0)
                }
            } else {
                self.outputPath.withCString {
                    self.onStop($0, durMs, Int32(self.width), Int32(self.height), Int32(self.frameRate), nil)
                }
            }
        }
    }

    // ── SCStreamOutput delegate ────────────────────────────────────────────────

    func stream(_ stream: SCStream, didOutputSampleBuffer sampleBuffer: CMSampleBuffer, of type: SCStreamOutputType) {
        switch type {
        case .screen:
            handleVideoFrame(sampleBuffer)
        case .audio:
            handleSystemAudioFrame(sampleBuffer)
        default:
            break
        }
    }

    private func handleVideoFrame(_ sampleBuffer: CMSampleBuffer) {
        guard let writer = assetWriter,
              let input = videoInput,
              let adaptor = pixelBufferAdaptor,
              input.isReadyForMoreMediaData else { return }

        let pts = CMSampleBufferGetPresentationTimeStamp(sampleBuffer)
        guard pts.isValid else { return }

        if startTime == .invalid {
            startTime = pts
            writer.startSession(atSourceTime: pts)
        }
        lastPresentationTime = pts

        if let imageBuffer = CMSampleBufferGetImageBuffer(sampleBuffer) {
            adaptor.append(imageBuffer, withPresentationTime: pts)
        }
    }

    private func handleSystemAudioFrame(_ sampleBuffer: CMSampleBuffer) {
        guard let input = systemAudioInput,
              input.isReadyForMoreMediaData,
              startTime != .invalid else { return }

        // Apply system audio gain by scaling sample values if gain != 1.0
        if audioOptions.systemAudioGain != 1.0,
           let scaled = scaledAudioBuffer(sampleBuffer, gain: audioOptions.systemAudioGain) {
            input.append(scaled)
        } else {
            input.append(sampleBuffer)
        }
    }

    // AVCaptureAudioDataOutputSampleBufferDelegate — receives mic samples
    func captureOutput(_ output: AVCaptureOutput, didOutput sampleBuffer: CMSampleBuffer,
                       from connection: AVCaptureConnection) {
        guard let input = micWriterInput,
              input.isReadyForMoreMediaData,
              startTime != .invalid else { return }

        if audioOptions.micGain != 1.0,
           let scaled = scaledAudioBuffer(sampleBuffer, gain: audioOptions.micGain) {
            input.append(scaled)
        } else {
            input.append(sampleBuffer)
        }
    }

    // Scale PCM float32 interleaved audio by a gain factor
    private func scaledAudioBuffer(_ buffer: CMSampleBuffer, gain: Float) -> CMSampleBuffer? {
        guard let blockBuffer = CMSampleBufferGetDataBuffer(buffer) else { return nil }
        var length = 0
        var dataPointer: UnsafeMutablePointer<CChar>? = nil
        guard CMBlockBufferGetDataPointer(blockBuffer, atOffset: 0,
                                          lengthAtOffsetOut: nil,
                                          totalLengthOut: &length,
                                          dataPointerOut: &dataPointer) == noErr,
              let ptr = dataPointer else { return nil }

        let floatPtr = UnsafeMutableRawPointer(ptr).bindMemory(to: Float.self, capacity: length / MemoryLayout<Float>.size)
        let count = length / MemoryLayout<Float>.size
        for i in 0..<count {
            floatPtr[i] *= gain
        }
        return buffer  // mutated in-place — blockBuffer is writable for SCStream audio
    }

    private func bitrateFor(width: Int, height: Int, fps: Int) -> Int {
        // good tier: 0.08 bpp
        return max(2_000_000, Int(Double(width * height) * 0.08 * Double(fps)))
    }
}

