import CoreBluetooth
import Foundation

struct PendingClientApproval {
    let centralIdentifier: UUID
    let clientId: UInt64
    let clientVersion: UInt16
    let capabilityFlags: UInt16
    let requestedAt: Date

    var displayText: String {
        "Pending client 0x\(clientId.hexClientId) from \(centralIdentifier.uuidString.prefix(8)) version \(clientVersion) flags 0x\(String(format: "%04X", capabilityFlags))"
    }
}

protocol BridgePeripheralManagerDelegate: AnyObject {
    func bridgePeripheralManager(_ manager: BridgePeripheralManager, didUpdateStatus status: String)
    func bridgePeripheralManager(_ manager: BridgePeripheralManager, didUpdatePendingClient pendingClient: PendingClientApproval?)
    func bridgePeripheralManager(_ manager: BridgePeripheralManager, didUpdateTrustedClientCount trustedCount: Int)
    func bridgePeripheralManager(_ manager: BridgePeripheralManager, didUpdateConnectionStatus connectionStatus: String)
    func bridgePeripheralManager(_ manager: BridgePeripheralManager, didUpdateRemoteInputState isActive: Bool)
    func bridgePeripheralManager(_ manager: BridgePeripheralManager, didAppendLog message: String)
}

final class BridgePeripheralManager: NSObject, CBPeripheralManagerDelegate {
    weak var delegate: BridgePeripheralManagerDelegate?

    private let trustStore: TrustStore
    private let logFileURL = FileManager.default.homeDirectoryForCurrentUser
        .appendingPathComponent("Library/Logs/\(AppIdentity.logFileName)")
    private lazy var serverId: UInt64 = loadOrCreateServerId()

    private var peripheralManager: CBPeripheralManager?
    private var published = false
    private var pendingClient: PendingClientApproval? {
        didSet {
            delegate?.bridgePeripheralManager(self, didUpdatePendingClient: pendingClient)
        }
    }

    private var controlSubscribers: [UUID: CBCentral] = [:]
    private var inputEventSubscribers: [UUID: CBCentral] = [:]
    private var inputSnapshotSubscribers: [UUID: CBCentral] = [:]
    private var approvedCentralIdentifiers = Set<UUID>()

    private var controlCharacteristic: CBMutableCharacteristic?
    private var inputEventCharacteristic: CBMutableCharacteristic?
    private var inputSnapshotCharacteristic: CBMutableCharacteristic?

    private var controlSequence: UInt16 = 0
    private var inputEventSequence: UInt16 = 0
    private var snapshotSequence: UInt16 = 0
    private var lastInputEventTimestampMs: UInt64?

    private var remoteInputActive = false {
        didSet {
            if oldValue != remoteInputActive {
                delegate?.bridgePeripheralManager(self, didUpdateRemoteInputState: remoteInputActive)
            }
        }
    }

    private var currentModifiers: UInt8 = 0
    private var currentPressedUsages = [UInt8]()
    private(set) var isRunning = false

    init(trustStore: TrustStore) {
        self.trustStore = trustStore
        super.init()
    }

    var hasApprovedConnectedCentral: Bool {
        !approvedTargetsForInputEvents().isEmpty
    }

    func start() {
        if isRunning {
            if let peripheralManager, peripheralManager.state == .poweredOn, !published {
                published = true
                publishBridgeService(on: peripheralManager)
            }
            return
        }

        isRunning = true
        log("Starting BLE peripheral manager for Phase 2 skeleton.")
        delegate?.bridgePeripheralManager(self, didUpdateTrustedClientCount: trustStore.trustedCount)
        delegate?.bridgePeripheralManager(self, didUpdateRemoteInputState: remoteInputActive)
        notifyConnectionStatus()
        delegate?.bridgePeripheralManager(self, didUpdateStatus: "Initializing CoreBluetooth...")

        if peripheralManager == nil {
            peripheralManager = CBPeripheralManager(delegate: self, queue: .main)
        } else if let peripheralManager, peripheralManager.state == .poweredOn, !published {
            published = true
            publishBridgeService(on: peripheralManager)
        }
    }

    func stop() {
        guard isRunning else {
            return
        }

        log("Stopping BLE peripheral manager.")
        isRunning = false
        pendingClient = nil

        if remoteInputActive {
            sendReleaseAll()
        }

        remoteInputActive = false
        resetSessionState(removeApprovals: true)
        controlCharacteristic = nil
        inputEventCharacteristic = nil
        inputSnapshotCharacteristic = nil
        peripheralManager?.stopAdvertising()
        peripheralManager?.removeAllServices()
        delegate?.bridgePeripheralManager(self, didUpdateStatus: "Bridge stopped")
    }

    func restartAdvertising() {
        guard isRunning else {
            start()
            return
        }

        log("Restart requested. Clearing bridge session state and restarting advertising.")
        pendingClient = nil
        remoteInputActive = false
        resetSessionState(removeApprovals: true)
        controlCharacteristic = nil
        inputEventCharacteristic = nil
        inputSnapshotCharacteristic = nil
        peripheralManager?.stopAdvertising()
        peripheralManager?.removeAllServices()

        if let peripheralManager, peripheralManager.state == .poweredOn {
            published = true
            publishBridgeService(on: peripheralManager)
        } else {
            published = false
            delegate?.bridgePeripheralManager(self, didUpdateStatus: "Initializing CoreBluetooth...")
        }
    }

    func approvePendingClient() {
        guard let pendingClient else {
            log("Approve requested, but there is no pending client.")
            return
        }

        approvedCentralIdentifiers.insert(pendingClient.centralIdentifier)
        trustStore.markApproved(clientId: pendingClient.clientId, alias: pendingClientAlias(for: pendingClient))
        delegate?.bridgePeripheralManager(self, didUpdateTrustedClientCount: trustStore.trustedCount)
        sendTrustStatus(
            to: pendingClient.centralIdentifier,
            status: .approved,
            reason: .userApproved
        )
        log("Approved client 0x\(pendingClient.clientId.hexClientId).")
        self.pendingClient = nil
        notifyConnectionStatus()
    }

    func denyPendingClient() {
        guard let pendingClient else {
            log("Deny requested, but there is no pending client.")
            return
        }

        approvedCentralIdentifiers.remove(pendingClient.centralIdentifier)
        sendTrustStatus(
            to: pendingClient.centralIdentifier,
            status: .denied,
            reason: .userDenied
        )
        log("Denied client 0x\(pendingClient.clientId.hexClientId).")
        self.pendingClient = nil
        notifyConnectionStatus()
    }

    func clearTrustedClients() {
        trustStore.clear()
        approvedCentralIdentifiers.removeAll()
        if remoteInputActive {
            deactivateRemoteInput(source: .system)
        }
        delegate?.bridgePeripheralManager(self, didUpdateTrustedClientCount: trustStore.trustedCount)
        notifyConnectionStatus()
        log("Cleared all trusted clients.")
    }

    @discardableResult
    func toggleRemoteInputMode(source: ModeChangeSource = .macUser) -> Bool {
        if remoteInputActive {
            deactivateRemoteInput(source: source)
            return false
        }

        guard hasApprovedConnectedCentral else {
            log("Cannot activate remote input because no approved Windows session is connected and subscribed to InputEvent.")
            delegate?.bridgePeripheralManager(self, didUpdateStatus: "Waiting for approved Windows session")
            return false
        }

        remoteInputActive = true
        resetInputState()
        sendModeChange(mode: .remoteInputActive, source: source)
        sendCurrentSnapshot(reason: .modeStart)
        delegate?.bridgePeripheralManager(self, didUpdateStatus: "Remote input active")
        log("Remote input activated.")
        return true
    }

    func deactivateRemoteInput(source: ModeChangeSource = .macUser) {
        guard remoteInputActive else {
            return
        }

        sendReleaseAll()
        resetInputState()
        sendCurrentSnapshot(reason: .modeStop)
        remoteInputActive = false
        sendModeChange(mode: .idle, source: source)
        delegate?.bridgePeripheralManager(self, didUpdateStatus: "Advertising as \(AppIdentity.bleLocalName)")
        log("Remote input deactivated.")
    }

    @discardableResult
    func sendCapturedKeyEvent(_ capturedEvent: CapturedInputEvent) -> Bool {
        guard remoteInputActive else {
            return false
        }

        guard let peripheralManager, let inputEventCharacteristic else {
            log("Cannot send InputEvent because the peripheral is not ready.")
            return false
        }

        let targets = approvedTargetsForInputEvents()
        guard !targets.isEmpty else {
            log("Cannot send InputEvent because no approved InputEvent subscribers are connected.")
            return false
        }

        synchronizePressedState(with: capturedEvent)
        let payload = KeyEventMessage(
            sequence: nextInputEventSequence(),
            action: capturedEvent.action,
            hidUsage: capturedEvent.hidUsage,
            modifiers: capturedEvent.modifiers,
            eventFlags: capturedEvent.eventFlags,
            deltaMilliseconds: nextInputDelta(nowMs: capturedEvent.timestampMilliseconds)
        ).encode()

        let success = peripheralManager.updateValue(payload, for: inputEventCharacteristic, onSubscribedCentrals: targets)
        if success {
            log("Sent InputEvent action=\(capturedEvent.action) usage=0x\(String(format: "%02X", capturedEvent.hidUsage)) modifiers=0x\(String(format: "%02X", capturedEvent.modifiers))")
        } else {
            log("InputEvent delivery queued or failed for action=\(capturedEvent.action) usage=0x\(String(format: "%02X", capturedEvent.hidUsage))")
        }

        return success
    }

    func peripheralManagerDidUpdateState(_ peripheral: CBPeripheralManager) {
        let status = "Bluetooth state: \(describe(state: peripheral.state))"
        log("\(status). Authorization=\(describe(authorization: CBManager.authorization))")

        if !isRunning {
            delegate?.bridgePeripheralManager(
                self,
                didUpdateStatus: peripheral.state == .poweredOn ? "Bridge stopped" : status
            )
            return
        }

        delegate?.bridgePeripheralManager(self, didUpdateStatus: status)

        guard peripheral.state == .poweredOn else {
            if remoteInputActive {
                deactivateRemoteInput(source: .system)
            }
            return
        }

        guard !published else {
            return
        }

        published = true
        publishBridgeService(on: peripheral)
    }

    func peripheralManager(_ peripheral: CBPeripheralManager, didAdd service: CBService, error: Error?) {
        if let error {
            let nsError = error as NSError
            log("Failed to add service \(service.uuid): \(error.localizedDescription) [domain=\(nsError.domain) code=\(nsError.code)]")
            delegate?.bridgePeripheralManager(self, didUpdateStatus: "Service publish failed")
            return
        }

        log("Added service \(service.uuid). Starting advertising.")
        peripheral.startAdvertising([
            CBAdvertisementDataLocalNameKey: AppIdentity.bleLocalName,
            CBAdvertisementDataServiceUUIDsKey: [BridgeUUIDs.service],
        ])
    }

    func peripheralManagerDidStartAdvertising(_ peripheral: CBPeripheralManager, error: Error?) {
        if let error {
            log("Advertising failed: \(error.localizedDescription)")
            delegate?.bridgePeripheralManager(self, didUpdateStatus: "Advertising failed")
            return
        }

        log("Advertising started with service \(BridgeUUIDs.service).")
        delegate?.bridgePeripheralManager(self, didUpdateStatus: "Advertising as \(AppIdentity.bleLocalName)")
    }

    func peripheralManager(_ peripheral: CBPeripheralManager, central: CBCentral, didSubscribeTo characteristic: CBCharacteristic) {
        switch characteristic.uuid {
        case BridgeUUIDs.control:
            controlSubscribers[central.identifier] = central
        case BridgeUUIDs.inputEvent:
            inputEventSubscribers[central.identifier] = central
        case BridgeUUIDs.inputSnapshot:
            inputSnapshotSubscribers[central.identifier] = central
        default:
            break
        }

        log("Central subscribed to \(characteristic.uuid) id=\(central.identifier.uuidString) mtu=\(central.maximumUpdateValueLength)")
        notifyConnectionStatus()
    }

    func peripheralManager(_ peripheral: CBPeripheralManager, central: CBCentral, didUnsubscribeFrom characteristic: CBCharacteristic) {
        switch characteristic.uuid {
        case BridgeUUIDs.control:
            controlSubscribers.removeValue(forKey: central.identifier)
        case BridgeUUIDs.inputEvent:
            inputEventSubscribers.removeValue(forKey: central.identifier)
        case BridgeUUIDs.inputSnapshot:
            inputSnapshotSubscribers.removeValue(forKey: central.identifier)
        default:
            break
        }

        if pendingClient?.centralIdentifier == central.identifier {
            pendingClient = nil
        }

        let stillReferenced = controlSubscribers[central.identifier] != nil
            || inputEventSubscribers[central.identifier] != nil
            || inputSnapshotSubscribers[central.identifier] != nil
        if !stillReferenced {
            approvedCentralIdentifiers.remove(central.identifier)
        }

        if remoteInputActive && approvedTargetsForInputEvents().isEmpty {
            log("All approved InputEvent subscribers are gone; deactivating remote input.")
            deactivateRemoteInput(source: .system)
        }

        log("Central unsubscribed from \(characteristic.uuid) id=\(central.identifier.uuidString)")
        notifyConnectionStatus()
    }

    func peripheralManagerIsReady(toUpdateSubscribers peripheral: CBPeripheralManager) {
        log("Peripheral is ready to send more notifications.")
    }

    func peripheralManager(_ peripheral: CBPeripheralManager, didReceiveRead request: CBATTRequest) {
        guard request.characteristic.uuid == inputSnapshotCharacteristic?.uuid else {
            log("Rejecting read for unsupported characteristic \(request.characteristic.uuid)")
            peripheral.respond(to: request, withResult: .attributeNotFound)
            return
        }

        let snapshot = makeSnapshot(reason: .resync).encode()

        guard request.offset <= snapshot.count else {
            peripheral.respond(to: request, withResult: .invalidOffset)
            return
        }

        request.value = snapshot.subdata(in: request.offset..<snapshot.count)
        peripheral.respond(to: request, withResult: .success)
        log("Served InputSnapshot read to \(request.central.identifier.uuidString).")
    }

    func peripheralManager(_ peripheral: CBPeripheralManager, didReceiveWrite requests: [CBATTRequest]) {
        for request in requests {
            guard request.characteristic.uuid == controlCharacteristic?.uuid else {
                peripheral.respond(to: request, withResult: .requestNotSupported)
                continue
            }

            let data = request.value ?? Data()
            log("Received Control write from \(request.central.identifier.uuidString) bytes=\(data.count) hex=\(data.hexString)")
            handleControlWrite(data, from: request.central)
            peripheral.respond(to: request, withResult: .success)
        }
    }

    private func publishBridgeService(on peripheral: CBPeripheralManager) {
        let service = CBMutableService(type: BridgeUUIDs.service, primary: true)

        let control = CBMutableCharacteristic(
            type: BridgeUUIDs.control,
            properties: [.write, .writeWithoutResponse, .notify],
            value: nil,
            permissions: [.writeable]
        )

        let inputEvent = CBMutableCharacteristic(
            type: BridgeUUIDs.inputEvent,
            properties: [.notify],
            value: nil,
            permissions: []
        )

        let inputSnapshot = CBMutableCharacteristic(
            type: BridgeUUIDs.inputSnapshot,
            properties: [.read, .notify],
            value: nil,
            permissions: [.readable]
        )

        service.characteristics = [control, inputEvent, inputSnapshot]
        controlCharacteristic = control
        inputEventCharacteristic = inputEvent
        inputSnapshotCharacteristic = inputSnapshot

        log("Publishing custom \(AppIdentity.bleLocalName) service for Phase 2.")
        peripheral.add(service)
    }

    private func handleControlWrite(_ data: Data, from central: CBCentral) {
        guard let hello = ControlMessageCodec.decodeClientHello(data) else {
            log("Ignored unsupported Control payload from \(central.identifier.uuidString).")
            return
        }

        log("Decoded ClientHello clientId=0x\(hello.clientId.hexClientId) version=\(hello.clientVersion) flags=0x\(String(format: "%04X", hello.capabilityFlags))")

        if trustStore.isTrusted(clientId: hello.clientId) {
            trustStore.markSeen(clientId: hello.clientId)
            approvedCentralIdentifiers.insert(central.identifier)
            delegate?.bridgePeripheralManager(self, didUpdateTrustedClientCount: trustStore.trustedCount)
            sendTrustStatus(to: central.identifier, status: .approved, reason: .autoTrusted)
            log("Client 0x\(hello.clientId.hexClientId) is already trusted.")
            notifyConnectionStatus()
            return
        }

        pendingClient = PendingClientApproval(
            centralIdentifier: central.identifier,
            clientId: hello.clientId,
            clientVersion: hello.clientVersion,
            capabilityFlags: hello.capabilityFlags,
            requestedAt: Date()
        )

        sendTrustStatus(to: central.identifier, status: .pending, reason: .unknownClient)
        log("Queued pending approval for client 0x\(hello.clientId.hexClientId).")
        notifyConnectionStatus()
    }

    private func sendTrustStatus(to centralIdentifier: UUID, status: TrustStatusCode, reason: TrustReason) {
        let payload = ControlMessageCodec.encodeTrustStatus(
            sequence: nextControlSequence(),
            status: status,
            reason: reason,
            serverFlags: 0,
            serverId: serverId
        )

        let success = sendControlPayload(payload, to: [centralIdentifier])
        log("Sent TrustStatus \(status) to \(centralIdentifier.uuidString). success=\(success)")
    }

    private func sendModeChange(mode: RemoteInputMode, source: ModeChangeSource) {
        let payload = ControlMessageCodec.encodeModeChange(
            sequence: nextControlSequence(),
            mode: mode,
            source: source,
            flags: 0
        )

        let success = sendControlPayload(payload, to: approvedCentralIdentifiers)
        log("Sent ModeChange mode=\(mode) source=\(source) success=\(success)")
    }

    private func sendReleaseAll() {
        let payload = ControlMessageCodec.encodeReleaseAll(sequence: nextControlSequence())
        let success = sendControlPayload(payload, to: approvedCentralIdentifiers)
        log("Sent ReleaseAll success=\(success)")
    }

    private func sendCurrentSnapshot(reason: SnapshotReason) {
        guard let peripheralManager, let inputSnapshotCharacteristic else {
            return
        }

        let targets = approvedTargetsForSnapshots()
        guard !targets.isEmpty else {
            return
        }

        let payload = makeSnapshot(reason: reason).encode()
        let success = peripheralManager.updateValue(payload, for: inputSnapshotCharacteristic, onSubscribedCentrals: targets)
        log("Sent InputSnapshot reason=\(reason) success=\(success) pressed=\(currentPressedUsages.count) modifiers=0x\(String(format: "%02X", currentModifiers))")
    }

    private func makeSnapshot(reason: SnapshotReason) -> KeyboardSnapshotMessage {
        KeyboardSnapshotMessage(
            sequence: nextSnapshotSequence(),
            reason: reason,
            modifiers: currentModifiers,
            usages: currentPressedUsages
        )
    }

    private func approvedTargetsForInputEvents() -> [CBCentral] {
        approvedCentralIdentifiers.compactMap { inputEventSubscribers[$0] }
    }

    private func approvedTargetsForSnapshots() -> [CBCentral] {
        approvedCentralIdentifiers.compactMap { inputSnapshotSubscribers[$0] }
    }

    private func sendControlPayload(_ payload: Data, to identifiers: some Sequence<UUID>) -> Bool {
        guard let peripheralManager, let controlCharacteristic else {
            log("Cannot send Control payload because the peripheral is not ready.")
            return false
        }

        let targets = identifiers.compactMap { controlSubscribers[$0] }
        guard !targets.isEmpty else {
            return false
        }

        return peripheralManager.updateValue(payload, for: controlCharacteristic, onSubscribedCentrals: targets)
    }

    private func synchronizePressedState(with capturedEvent: CapturedInputEvent) {
        currentModifiers = capturedEvent.modifiers

        guard capturedEvent.hidUsage != 0 else {
            return
        }

        switch capturedEvent.action {
        case .keyDown:
            if !currentPressedUsages.contains(capturedEvent.hidUsage) {
                currentPressedUsages.append(capturedEvent.hidUsage)
            }
        case .keyUp:
            currentPressedUsages.removeAll { $0 == capturedEvent.hidUsage }
        case .keyRepeat, .modifiersOnly:
            break
        }

        if currentPressedUsages.count > 6 {
            currentPressedUsages = Array(currentPressedUsages.prefix(6))
        }
    }

    private func resetInputState() {
        currentModifiers = 0
        currentPressedUsages = []
        lastInputEventTimestampMs = nil
    }

    private func resetSessionState(removeApprovals: Bool) {
        published = false
        controlSubscribers.removeAll()
        inputEventSubscribers.removeAll()
        inputSnapshotSubscribers.removeAll()
        if removeApprovals {
            approvedCentralIdentifiers.removeAll()
        }
        resetInputState()
        notifyConnectionStatus()
    }

    private func nextInputDelta(nowMs: UInt64) -> UInt16 {
        defer {
            lastInputEventTimestampMs = nowMs
        }

        guard let previous = lastInputEventTimestampMs else {
            return 0
        }

        return UInt16(min(nowMs - previous, UInt64(UInt16.max)))
    }

    private func pendingClientAlias(for pendingClient: PendingClientApproval) -> String {
        "Windows-\(pendingClient.centralIdentifier.uuidString.prefix(8))"
    }

    private func nextControlSequence() -> UInt16 {
        controlSequence &+= 1
        return controlSequence
    }

    private func nextInputEventSequence() -> UInt16 {
        inputEventSequence &+= 1
        return inputEventSequence
    }

    private func nextSnapshotSequence() -> UInt16 {
        snapshotSequence &+= 1
        return snapshotSequence
    }

    private func notifyConnectionStatus() {
        let connectionStatus = "Control subs: \(controlSubscribers.count) | Input subs: \(inputEventSubscribers.count) | Snapshot subs: \(inputSnapshotSubscribers.count) | Approved: \(approvedCentralIdentifiers.count)"
        delegate?.bridgePeripheralManager(self, didUpdateConnectionStatus: connectionStatus)
    }

    private func loadOrCreateServerId() -> UInt64 {
        let defaultsKey = "keyboard-bridge.server-id"
        let existing = UserDefaults.standard.object(forKey: defaultsKey) as? NSNumber
        if let existing {
            return existing.uint64Value
        }

        let generated = UInt64.random(in: UInt64.min...UInt64.max)
        UserDefaults.standard.set(NSNumber(value: generated), forKey: defaultsKey)
        return generated
    }

    private func log(_ message: String) {
        appendLogToDisk(message)
        delegate?.bridgePeripheralManager(self, didAppendLog: message)
        NSLog("[\(AppIdentity.logPrefix)] %@", message)
    }

    private func appendLogToDisk(_ message: String) {
        let formatter = ISO8601DateFormatter()
        let line = "[\(formatter.string(from: Date()))] \(message)\n"
        let data = Data(line.utf8)

        do {
            try FileManager.default.createDirectory(
                at: logFileURL.deletingLastPathComponent(),
                withIntermediateDirectories: true
            )

            if !FileManager.default.fileExists(atPath: logFileURL.path) {
                try Data().write(to: logFileURL)
            }

            let handle = try FileHandle(forWritingTo: logFileURL)
            try handle.seekToEnd()
            try handle.write(contentsOf: data)
            try handle.close()
        } catch {
            NSLog("[\(AppIdentity.logPrefix)] Failed to append log file: %@", error.localizedDescription)
        }
    }

    private func describe(state: CBManagerState) -> String {
        switch state {
        case .unknown:
            return "unknown"
        case .resetting:
            return "resetting"
        case .unsupported:
            return "unsupported"
        case .unauthorized:
            return "unauthorized"
        case .poweredOff:
            return "poweredOff"
        case .poweredOn:
            return "poweredOn"
        @unknown default:
            return "future(\(state.rawValue))"
        }
    }

    private func describe(authorization: CBManagerAuthorization) -> String {
        switch authorization {
        case .allowedAlways:
            return "allowedAlways"
        case .restricted:
            return "restricted"
        case .denied:
            return "denied"
        case .notDetermined:
            return "notDetermined"
        @unknown default:
            return "future(\(authorization.rawValue))"
        }
    }
}
