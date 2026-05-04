import Foundation

struct TrustedClientRecord: Codable {
    let clientId: UInt64
    var alias: String
    let firstApprovedAt: Date
    var lastSeenAt: Date
    var autoAccept: Bool
}

final class TrustStore {
    private let fileURL: URL
    private var recordsByClientId: [UInt64: TrustedClientRecord] = [:]

    init(appFolderName: String = AppIdentity.appName) {
        let appSupportURL = FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent("Library/Application Support", isDirectory: true)
            .appendingPathComponent(appFolderName, isDirectory: true)
        self.fileURL = appSupportURL.appendingPathComponent("trusted-clients.json")
        load()
    }

    var trustedCount: Int {
        recordsByClientId.count
    }

    func isTrusted(clientId: UInt64) -> Bool {
        recordsByClientId[clientId]?.autoAccept == true
    }

    func allRecords() -> [TrustedClientRecord] {
        recordsByClientId.values.sorted { $0.firstApprovedAt < $1.firstApprovedAt }
    }

    func markApproved(clientId: UInt64, alias: String) {
        let now = Date()
        if var existing = recordsByClientId[clientId] {
            existing.alias = alias
            existing.lastSeenAt = now
            existing.autoAccept = true
            recordsByClientId[clientId] = existing
        } else {
            recordsByClientId[clientId] = TrustedClientRecord(
                clientId: clientId,
                alias: alias,
                firstApprovedAt: now,
                lastSeenAt: now,
                autoAccept: true
            )
        }
        save()
    }

    func markSeen(clientId: UInt64) {
        guard var existing = recordsByClientId[clientId] else {
            return
        }
        existing.lastSeenAt = Date()
        recordsByClientId[clientId] = existing
        save()
    }

    func clear() {
        recordsByClientId.removeAll()
        save()
    }

    private func load() {
        guard let data = try? Data(contentsOf: fileURL) else {
            recordsByClientId = [:]
            return
        }

        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601

        guard let records = try? decoder.decode([TrustedClientRecord].self, from: data) else {
            recordsByClientId = [:]
            return
        }

        recordsByClientId = Dictionary(uniqueKeysWithValues: records.map { ($0.clientId, $0) })
    }

    private func save() {
        let parentDirectory = fileURL.deletingLastPathComponent()
        try? FileManager.default.createDirectory(at: parentDirectory, withIntermediateDirectories: true)

        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        encoder.dateEncodingStrategy = .iso8601

        let payload = allRecords()
        guard let data = try? encoder.encode(payload) else {
            return
        }

        try? data.write(to: fileURL)
    }
}
