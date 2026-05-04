import AppKit

final class BridgeViewController: NSViewController, BridgePeripheralManagerDelegate, KeyboardCaptureManagerDelegate {
    private let settingsStore = BridgeSettingsStore()
    private let trustStore = TrustStore()
    private let keyboardCaptureManager = KeyboardCaptureManager()

    private lazy var peripheralManager: BridgePeripheralManager = {
        let manager = BridgePeripheralManager(trustStore: trustStore)
        manager.delegate = self
        return manager
    }()

    private let iconBadgeView = IconBadgeView()
    private let headlineLabel = NSTextField(labelWithString: "Starting")
    private let messageLabel = NSTextField(wrappingLabelWithString: "Preparing Bluetooth.")
    private let shortcutLabel = NSTextField(wrappingLabelWithString: "")
    private let shortcutPickerLabel = NSTextField(labelWithString: "Quick Switch")
    private let devicePillLabel = CapsuleLabel(title: "Windows")

    private lazy var primaryButton = makePrimaryButton(title: "Connect", action: #selector(handlePrimaryAction))
    private lazy var secondaryButton = makeSecondaryButton(title: "Retry", action: #selector(handleSecondaryAction))
    private lazy var approveButton = makePrimaryButton(title: "Approve", action: #selector(approvePending))
    private lazy var denyButton = makeSecondaryButton(title: "Deny", action: #selector(denyPending))
    private lazy var shortcutPicker: NSPopUpButton = {
        let picker = NSPopUpButton(frame: .zero, pullsDown: false)
        picker.target = self
        picker.action = #selector(handleShortcutPickerChanged)
        return picker
    }()

    private let actionRow = NSStackView()
    private let approvalRow = NSStackView()
    private let shortcutSettingsRow = NSStackView()

    private var currentStatus = "Initializing..."
    private var currentConnectionStatus = "Control subs: 0 | Input subs: 0 | Snapshot subs: 0 | Approved: 0"
    private var currentCaptureStatus = "starting..."
    private var currentTrustedCount = 0
    private var currentPendingClient: PendingClientApproval?
    private var isRemoteModeActive = false

    override func loadView() {
        let backgroundView = NSVisualEffectView()
        backgroundView.material = .popover
        backgroundView.blendingMode = .behindWindow
        backgroundView.state = .active
        backgroundView.wantsLayer = true
        backgroundView.layer?.cornerRadius = 18
        backgroundView.layer?.masksToBounds = true
        view = backgroundView

        configureTypography()
        configureRows()

        let contentStack = NSStackView(views: [
            iconBadgeView,
            headlineLabel,
            messageLabel,
            shortcutLabel,
            shortcutSettingsRow,
            devicePillLabel,
            actionRow,
            approvalRow,
        ])
        contentStack.orientation = .vertical
        contentStack.spacing = 14
        contentStack.alignment = .leading
        contentStack.translatesAutoresizingMaskIntoConstraints = false

        let card = makeCard(containing: contentStack)

        let rootStack = NSStackView(views: [card])
        rootStack.orientation = .vertical
        rootStack.spacing = 0
        rootStack.alignment = .leading
        rootStack.translatesAutoresizingMaskIntoConstraints = false

        view.addSubview(rootStack)

        NSLayoutConstraint.activate([
            rootStack.topAnchor.constraint(equalTo: view.topAnchor, constant: 42),
            rootStack.leadingAnchor.constraint(equalTo: view.leadingAnchor, constant: 16),
            rootStack.trailingAnchor.constraint(equalTo: view.trailingAnchor, constant: -16),
            rootStack.bottomAnchor.constraint(lessThanOrEqualTo: view.bottomAnchor, constant: -18),
            card.widthAnchor.constraint(equalTo: rootStack.widthAnchor),
            iconBadgeView.widthAnchor.constraint(equalToConstant: 34),
            iconBadgeView.heightAnchor.constraint(equalToConstant: 34),
            primaryButton.widthAnchor.constraint(greaterThanOrEqualToConstant: 118),
        ])
    }

    override func viewDidLoad() {
        super.viewDidLoad()
        keyboardCaptureManager.delegate = self
        keyboardCaptureManager.applyShortcutConfiguration(settingsStore.shortcutConfiguration)
        configureShortcutPicker()
        NotificationCenter.default.addObserver(
            self,
            selector: #selector(handleAppDidBecomeActive),
            name: NSApplication.didBecomeActiveNotification,
            object: nil
        )
        refreshUI()
        peripheralManager.start()
        keyboardCaptureManager.start()
    }

    @objc private func handlePrimaryAction() {
        if captureNeedsAttention() {
            openCaptureSettings()
            return
        }

        if isRemoteModeActive {
            peripheralManager.deactivateRemoteInput()
            keyboardCaptureManager.isRemoteInputActive = false
            return
        }

        let isActive = peripheralManager.toggleRemoteInputMode()
        keyboardCaptureManager.isRemoteInputActive = isActive
    }

    @objc private func retryPermission() {
        keyboardCaptureManager.retryStart()
    }

    @objc private func handleShortcutPickerChanged() {
        guard let rawValue = shortcutPicker.selectedItem?.representedObject as? String,
              let option = ShortcutKeyOption(rawValue: rawValue) else {
            return
        }

        settingsStore.updateToggleKey(option)
        keyboardCaptureManager.applyShortcutConfiguration(settingsStore.shortcutConfiguration)
        refreshUI()
    }

    @objc private func handleSecondaryAction() {
        if captureNeedsAttention() {
            retryPermission()
        }
    }

    @objc private func approvePending() {
        peripheralManager.approvePendingClient()
    }

    @objc private func denyPending() {
        peripheralManager.denyPendingClient()
    }

    private func openCaptureSettings() {
        let normalized = currentCaptureStatus.lowercased()
        let anchor = normalized.contains("accessibility")
            ? "Privacy_Accessibility"
            : "Privacy_ListenEvent"

        guard let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?\(anchor)") else {
            return
        }

        NSWorkspace.shared.open(url)
    }

    @objc private func handleAppDidBecomeActive() {
        if captureNeedsAttention() {
            keyboardCaptureManager.retryStart(promptIfNeeded: false)
        }
    }

    func bridgePeripheralManager(_ manager: BridgePeripheralManager, didUpdateStatus status: String) {
        currentStatus = status
        refreshUI()
    }

    func bridgePeripheralManager(_ manager: BridgePeripheralManager, didUpdatePendingClient pendingClient: PendingClientApproval?) {
        currentPendingClient = pendingClient
        refreshUI()
    }

    func bridgePeripheralManager(_ manager: BridgePeripheralManager, didUpdateTrustedClientCount trustedCount: Int) {
        currentTrustedCount = trustedCount
        refreshUI()
    }

    func bridgePeripheralManager(_ manager: BridgePeripheralManager, didUpdateConnectionStatus connectionStatus: String) {
        currentConnectionStatus = connectionStatus
        refreshUI()
    }

    func bridgePeripheralManager(_ manager: BridgePeripheralManager, didUpdateRemoteInputState isActive: Bool) {
        isRemoteModeActive = isActive
        keyboardCaptureManager.isRemoteInputActive = isActive
        refreshUI()
    }

    func bridgePeripheralManager(_ manager: BridgePeripheralManager, didAppendLog message: String) {
    }

    func keyboardCaptureManager(_ manager: KeyboardCaptureManager, didUpdateStatus status: String) {
        currentCaptureStatus = status
        refreshUI()
    }

    func keyboardCaptureManager(_ manager: KeyboardCaptureManager, didLog message: String) {
    }

    func keyboardCaptureManager(_ manager: KeyboardCaptureManager, didReceiveCommand command: CaptureCommand) {
        switch command {
        case .toggleRemoteInput:
            let isActive = peripheralManager.toggleRemoteInputMode()
            keyboardCaptureManager.isRemoteInputActive = isActive
        case .emergencyStop:
            peripheralManager.deactivateRemoteInput()
            keyboardCaptureManager.isRemoteInputActive = false
        }
    }

    func keyboardCaptureManager(_ manager: KeyboardCaptureManager, didCapture inputEvent: CapturedInputEvent) {
        _ = peripheralManager.sendCapturedKeyEvent(inputEvent)
    }

    private func refreshUI() {
        let state = currentDisplayState()

        headlineLabel.stringValue = state.headline
        messageLabel.stringValue = state.message
        syncShortcutPickerSelection()

        iconBadgeView.apply(symbolName: state.symbolName, tintColor: state.tintColor)

        if let shortcutText = state.shortcutText {
            shortcutLabel.isHidden = false
            shortcutLabel.stringValue = shortcutText
        } else {
            shortcutLabel.isHidden = true
            shortcutLabel.stringValue = ""
        }

        if let pillText = state.pillText {
            devicePillLabel.isHidden = false
            devicePillLabel.apply(title: pillText, fillColor: state.pillFillColor, textColor: state.pillTextColor)
        } else {
            devicePillLabel.isHidden = true
        }

        primaryButton.isHidden = !state.showsPrimaryButton
        primaryButton.apply(title: state.primaryButtonTitle, style: .primary, isEnabled: state.primaryButtonEnabled)

        secondaryButton.isHidden = !state.showsSecondaryButton
        secondaryButton.apply(title: state.secondaryButtonTitle, style: .secondary, isEnabled: true)

        approvalRow.isHidden = !state.showsApprovalButtons
        if state.showsApprovalButtons, let pendingClient = currentPendingClient {
            devicePillLabel.isHidden = false
            devicePillLabel.apply(
                title: "Windows \(String(pendingClient.clientId.hexClientId.suffix(6)))",
                fillColor: NSColor.systemOrange.withAlphaComponent(0.14),
                textColor: NSColor.systemOrange.blended(withFraction: 0.2, of: .labelColor) ?? .systemOrange
            )
        }
    }

    private func currentDisplayState() -> DisplayState {
        let shortcuts = settingsStore.shortcutConfiguration
        let quickSwitchText = "Quick Switch: \(shortcuts.toggleDisplayText)"
        let liveShortcutText = "Quick Switch: \(shortcuts.toggleDisplayText)    Emergency: \(shortcuts.emergencyDisplayText)"

        if captureNeedsAttention() {
            return DisplayState(
                headline: "Keyboard Access",
                message: "Turn on Accessibility. If macOS still blocks capture, also turn on Input Monitoring.",
                shortcutText: nil,
                symbolName: "exclamationmark.shield.fill",
                tintColor: .systemOrange,
                pillText: nil,
                pillFillColor: .clear,
                pillTextColor: .clear,
                showsPrimaryButton: true,
                primaryButtonTitle: "Open Settings",
                primaryButtonEnabled: true,
                showsSecondaryButton: true,
                secondaryButtonTitle: "Retry",
                showsApprovalButtons: false
            )
        }

        if let pendingClient = currentPendingClient {
            return DisplayState(
                headline: "Approve Windows",
                message: "A nearby receiver wants access.",
                shortcutText: nil,
                symbolName: "questionmark.circle.fill",
                tintColor: .systemOrange,
                pillText: "Windows \(String(pendingClient.clientId.hexClientId.suffix(6)))",
                pillFillColor: NSColor.systemOrange.withAlphaComponent(0.14),
                pillTextColor: NSColor.systemOrange.blended(withFraction: 0.2, of: .labelColor) ?? .systemOrange,
                showsPrimaryButton: false,
                primaryButtonTitle: "",
                primaryButtonEnabled: false,
                showsSecondaryButton: false,
                secondaryButtonTitle: "",
                showsApprovalButtons: true
            )
        }

        if isRemoteModeActive {
            return DisplayState(
                headline: "Connected",
                message: shortcuts.isQuickSwitchEnabled
                    ? "Keyboard is exclusive to Windows. Use Quick Switch to return."
                    : "Keyboard is exclusive to Windows. Use Disconnect or Emergency to return.",
                shortcutText: liveShortcutText,
                symbolName: "checkmark.circle.fill",
                tintColor: .systemGreen,
                pillText: "Exclusive",
                pillFillColor: NSColor.systemGreen.withAlphaComponent(0.14),
                pillTextColor: NSColor.systemGreen.blended(withFraction: 0.2, of: .labelColor) ?? .systemGreen,
                showsPrimaryButton: true,
                primaryButtonTitle: "Disconnect",
                primaryButtonEnabled: true,
                showsSecondaryButton: false,
                secondaryButtonTitle: "",
                showsApprovalButtons: false
            )
        }

        if hasReadyReceiver() {
            return DisplayState(
                headline: "Ready",
                message: shortcuts.isQuickSwitchEnabled
                    ? "Press Connect or use Quick Switch to jump to Windows."
                    : "Press Connect to jump to Windows.",
                shortcutText: quickSwitchText,
                symbolName: "link.circle.fill",
                tintColor: .controlAccentColor,
                pillText: "Receiver connected",
                pillFillColor: NSColor.controlAccentColor.withAlphaComponent(0.12),
                pillTextColor: .controlAccentColor,
                showsPrimaryButton: true,
                primaryButtonTitle: "Connect",
                primaryButtonEnabled: true,
                showsSecondaryButton: false,
                secondaryButtonTitle: "",
                showsApprovalButtons: false
            )
        }

        if currentStatus.localizedCaseInsensitiveContains("bluetooth state: poweredoff") {
            return DisplayState(
                headline: "Bluetooth Is Off",
                message: "Turn Bluetooth back on to make this Mac discoverable.",
                shortcutText: nil,
                symbolName: "bolt.slash.circle.fill",
                tintColor: .systemRed,
                pillText: nil,
                pillFillColor: .clear,
                pillTextColor: .clear,
                showsPrimaryButton: false,
                primaryButtonTitle: "",
                primaryButtonEnabled: false,
                showsSecondaryButton: false,
                secondaryButtonTitle: "",
                showsApprovalButtons: false
            )
        }

        if currentStatus.localizedCaseInsensitiveContains("unauthorized") {
            return DisplayState(
                headline: "Bluetooth Access Needed",
                message: "Allow Bluetooth access for \(AppIdentity.displayName).",
                shortcutText: nil,
                symbolName: "lock.circle.fill",
                tintColor: .systemOrange,
                pillText: nil,
                pillFillColor: .clear,
                pillTextColor: .clear,
                showsPrimaryButton: false,
                primaryButtonTitle: "",
                primaryButtonEnabled: false,
                showsSecondaryButton: false,
                secondaryButtonTitle: "",
                showsApprovalButtons: false
            )
        }

        return DisplayState(
            headline: currentTrustedCount > 0 ? "Waiting for Windows" : "Open the Windows App",
            message: currentTrustedCount > 0
                ? "This Mac is ready whenever the Windows app connects."
                : "Launch the Windows app to discover this Mac.",
            shortcutText: currentTrustedCount > 0 ? quickSwitchText : nil,
            symbolName: "dot.radiowaves.left.and.right",
            tintColor: .controlAccentColor,
            pillText: nil,
            pillFillColor: NSColor.white.withAlphaComponent(0.52),
            pillTextColor: .secondaryLabelColor,
            showsPrimaryButton: false,
            primaryButtonTitle: "",
            primaryButtonEnabled: false,
            showsSecondaryButton: false,
            secondaryButtonTitle: "",
            showsApprovalButtons: false
        )
    }

    private func hasReadyReceiver() -> Bool {
        integer(after: "Input subs:", in: currentConnectionStatus) > 0
            && integer(after: "Approved:", in: currentConnectionStatus) > 0
    }

    private func integer(after marker: String, in text: String) -> Int {
        guard let range = text.range(of: marker) else {
            return 0
        }

        let suffix = text[range.upperBound...]
        let digits = suffix.drop(while: { !$0.isNumber }).prefix(while: { $0.isNumber })
        return Int(String(digits)) ?? 0
    }

    private func captureNeedsAttention() -> Bool {
        let normalized = currentCaptureStatus.lowercased()
        return normalized.contains("unavailable")
            || normalized.contains("permission")
            || normalized.contains("denied")
    }

    private func configureTypography() {
        headlineLabel.font = .systemFont(ofSize: 24, weight: .semibold)
        headlineLabel.maximumNumberOfLines = 2

        messageLabel.font = .systemFont(ofSize: 13, weight: .medium)
        messageLabel.textColor = .secondaryLabelColor
        messageLabel.maximumNumberOfLines = 2

        shortcutLabel.font = .monospacedSystemFont(ofSize: 11.5, weight: .medium)
        shortcutLabel.textColor = .secondaryLabelColor
        shortcutLabel.maximumNumberOfLines = 2

        shortcutPickerLabel.font = .systemFont(ofSize: 11.5, weight: .medium)
        shortcutPickerLabel.textColor = .secondaryLabelColor
    }

    private func configureRows() {
        actionRow.orientation = .horizontal
        actionRow.spacing = 8
        actionRow.alignment = .centerY
        actionRow.addArrangedSubview(primaryButton)
        actionRow.addArrangedSubview(secondaryButton)

        approvalRow.orientation = .horizontal
        approvalRow.spacing = 8
        approvalRow.alignment = .centerY
        approvalRow.addArrangedSubview(approveButton)
        approvalRow.addArrangedSubview(denyButton)

        shortcutSettingsRow.orientation = .horizontal
        shortcutSettingsRow.spacing = 8
        shortcutSettingsRow.alignment = .centerY
        shortcutSettingsRow.addArrangedSubview(shortcutPickerLabel)
        shortcutSettingsRow.addArrangedSubview(shortcutPicker)
    }

    private func configureShortcutPicker() {
        shortcutPicker.removeAllItems()
        for option in ShortcutKeyOption.allCases {
            shortcutPicker.menu?.addItem(shortcutPickerItem(for: option))
        }
        syncShortcutPickerSelection()
    }

    private func syncShortcutPickerSelection() {
        let selectedOption = settingsStore.shortcutConfiguration.toggleKey
        for item in shortcutPicker.itemArray {
            item.state = .off
        }

        if let item = shortcutPicker.itemArray.first(where: { ($0.representedObject as? String) == selectedOption.rawValue }) {
            shortcutPicker.select(item)
        }
    }

    private func shortcutPickerItem(for option: ShortcutKeyOption) -> NSMenuItem {
        let title = option == .disabled
            ? option.title
            : "\(settingsStore.shortcutConfiguration.modifierDisplay) + \(option.title)"
        let item = NSMenuItem(title: title, action: nil, keyEquivalent: "")
        item.representedObject = option.rawValue
        return item
    }

    private func makeCard(containing content: NSView) -> NSView {
        let card = NSView()
        card.wantsLayer = true
        card.layer?.backgroundColor = NSColor.windowBackgroundColor.withAlphaComponent(0.78).cgColor
        card.layer?.cornerRadius = 18
        card.layer?.borderWidth = 1
        card.layer?.borderColor = NSColor.black.withAlphaComponent(0.05).cgColor
        card.layer?.shadowColor = NSColor.black.withAlphaComponent(0.10).cgColor
        card.layer?.shadowOpacity = 1
        card.layer?.shadowRadius = 14
        card.layer?.shadowOffset = NSSize(width: 0, height: -3)

        card.addSubview(content)

        NSLayoutConstraint.activate([
            content.topAnchor.constraint(equalTo: card.topAnchor, constant: 16),
            content.leadingAnchor.constraint(equalTo: card.leadingAnchor, constant: 16),
            content.trailingAnchor.constraint(equalTo: card.trailingAnchor, constant: -16),
            content.bottomAnchor.constraint(equalTo: card.bottomAnchor, constant: -16),
        ])

        return card
    }

    private func makePrimaryButton(title: String, action: Selector) -> BridgeButton {
        BridgeButton(title: title, target: self, action: action, style: .primary)
    }

    private func makeSecondaryButton(title: String, action: Selector) -> BridgeButton {
        BridgeButton(title: title, target: self, action: action, style: .secondary)
    }
}

private struct DisplayState {
    let headline: String
    let message: String
    let shortcutText: String?
    let symbolName: String
    let tintColor: NSColor
    let pillText: String?
    let pillFillColor: NSColor
    let pillTextColor: NSColor
    let showsPrimaryButton: Bool
    let primaryButtonTitle: String
    let primaryButtonEnabled: Bool
    let showsSecondaryButton: Bool
    let secondaryButtonTitle: String
    let showsApprovalButtons: Bool
}

private final class CapsuleLabel: NSTextField {
    init(title: String) {
        super.init(frame: .zero)
        isEditable = false
        isBordered = false
        drawsBackground = false
        alignment = .center
        font = .systemFont(ofSize: 11.5, weight: .semibold)
        wantsLayer = true
        layer?.cornerRadius = 999
        apply(title: title, fillColor: NSColor.white.withAlphaComponent(0.52), textColor: .secondaryLabelColor)
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    func apply(title: String, fillColor: NSColor, textColor: NSColor) {
        stringValue = "  \(title)  "
        self.textColor = textColor
        layer?.backgroundColor = fillColor.cgColor
    }
}

private final class IconBadgeView: NSView {
    private var tintColor: NSColor = .controlAccentColor

    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        wantsLayer = true
        layer?.cornerRadius = 10
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    override func draw(_ dirtyRect: NSRect) {
        super.draw(dirtyRect)

        let badgeRect = bounds.insetBy(dx: 0.5, dy: 0.5)
        let badgePath = NSBezierPath(roundedRect: badgeRect, xRadius: 10, yRadius: 10)
        tintColor.setFill()
        badgePath.fill()

        NSColor.white.setFill()
        fillRoundedRect(NSRect(x: 8, y: bounds.height - 19, width: 9, height: 9), radius: 3)
        fillRoundedRect(NSRect(x: 8, y: bounds.height - 32, width: 9, height: 9), radius: 3)
        fillRoundedRect(NSRect(x: 21, y: bounds.height - 29.5, width: 13, height: 13), radius: 4)
        let dotRect = NSRect(x: 33.5, y: bounds.height - 19.5, width: 4.5, height: 4.5)
        NSBezierPath(ovalIn: dotRect).fill()
    }

    func apply(symbolName: String, tintColor: NSColor) {
        self.tintColor = tintColor
        layer?.backgroundColor = NSColor.clear.cgColor
        needsDisplay = true
    }

    private func fillRoundedRect(_ rect: NSRect, radius: CGFloat) {
        let path = NSBezierPath(roundedRect: rect, xRadius: radius, yRadius: radius)
        path.fill()
    }
}

private final class BridgeButton: NSButton {
    enum Style {
        case primary
        case secondary
    }

    private var bridgeStyle: Style

    init(title: String, target: AnyObject?, action: Selector, style: Style) {
        bridgeStyle = style
        super.init(frame: .zero)
        self.target = target
        self.action = action
        isBordered = false
        focusRingType = .none
        wantsLayer = true
        layer?.cornerRadius = 9
        setButtonType(.momentaryPushIn)
        apply(title: title, style: style, isEnabled: true)
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    override var intrinsicContentSize: NSSize {
        let size = super.intrinsicContentSize
        return NSSize(width: size.width + 24, height: 34)
    }

    func apply(title: String, style: Style, isEnabled: Bool) {
        bridgeStyle = style
        self.isEnabled = isEnabled

        let colors = palette(for: style, isEnabled: isEnabled)
        layer?.backgroundColor = colors.background.cgColor
        attributedTitle = NSAttributedString(
            string: title,
            attributes: [
                .foregroundColor: colors.text,
                .font: NSFont.systemFont(ofSize: 13.5, weight: .semibold),
            ]
        )
    }

    private func palette(for style: Style, isEnabled: Bool) -> (background: NSColor, text: NSColor) {
        guard isEnabled else {
            return (
                NSColor.controlColor.withAlphaComponent(0.55),
                NSColor.secondaryLabelColor.withAlphaComponent(0.65)
            )
        }

        switch style {
        case .primary:
            return (NSColor.controlAccentColor, .white)
        case .secondary:
            return (
                NSColor.controlColor.withAlphaComponent(0.46),
                NSColor.secondaryLabelColor
            )
        }
    }
}
