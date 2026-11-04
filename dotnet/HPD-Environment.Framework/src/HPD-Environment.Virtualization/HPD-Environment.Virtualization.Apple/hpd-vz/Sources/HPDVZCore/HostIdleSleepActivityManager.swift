import Foundation

public protocol HostIdleSleepActivityManaging: AnyObject {
    func begin(operationId: String)
    func end(operationId: String)
}

/// Holds only an idle-system-sleep assertion. Apple still permits explicit
/// sleep, lid-close, and other non-idle sleep causes, which are reconciled by
/// HostPowerMonitor. Every assertion has a hard expiry and process exit also
/// releases it.
public final class HostIdleSleepActivityManager:
    HostIdleSleepActivityManaging,
    @unchecked Sendable
{
    private static let maximumDuration: TimeInterval = 2 * 60 * 60
    private let lock = NSLock()
    private var activities: [String: NSObjectProtocol] = [:]

    public init() {}

    public func begin(operationId: String) {
        guard !operationId.isEmpty, operationId.count <= 256 else { return }
        lock.lock()
        guard activities[operationId] == nil else {
            lock.unlock()
            return
        }
        let activity = ProcessInfo.processInfo.beginActivity(
            options: [.idleSystemSleepDisabled],
            reason: "HPD Environment durable storage operation")
        activities[operationId] = activity
        lock.unlock()

        DispatchQueue.global(qos: .utility).asyncAfter(
            deadline: .now() + Self.maximumDuration
        ) { [weak self] in
            self?.end(operationId: operationId)
        }
    }

    public func end(operationId: String) {
        lock.lock()
        let activity = activities.removeValue(forKey: operationId)
        lock.unlock()
        if let activity {
            ProcessInfo.processInfo.endActivity(activity)
        }
    }

    deinit {
        lock.lock()
        let retained = Array(activities.values)
        activities.removeAll()
        lock.unlock()
        for activity in retained {
            ProcessInfo.processInfo.endActivity(activity)
        }
    }
}
