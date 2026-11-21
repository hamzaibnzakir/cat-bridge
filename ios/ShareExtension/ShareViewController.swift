import UIKit
import SwiftUI
import UniformTypeIdentifiers
import CatBridgeCore

/// "Send to Laptop" in the iPhone share sheet: links open on the laptop, text goes to its clipboard, photos and files are sent over.
@objc(ShareViewController)
final class ShareViewController: UIViewController {
    override func viewDidLoad() {
        super.viewDidLoad()
        let model = ShareModel(context: extensionContext)
        let host = UIHostingController(rootView: ShareSheetView(model: model) { [weak self] in
            self?.extensionContext?.completeRequest(returningItems: nil)
        })
        host.view.backgroundColor = .clear
        addChild(host)
        host.view.frame = view.bounds
        host.view.autoresizingMask = [.flexibleWidth, .flexibleHeight]
        view.addSubview(host.view)
        host.didMove(toParent: self)
        Task { await model.load() }
    }
}

@MainActor
@Observable
final class ShareModel {
    enum Item: Sendable { case web(URL), text(String), file(URL, String) }

    var items: [Item] = []
    var loading = true
    var status: String?
    var failed = false
    var done = false
    var pairing: Pairing? = PairingStore.load()
    @ObservationIgnored let context: NSExtensionContext?

    init(context: NSExtensionContext?) { self.context = context }

    var webURL: URL? { items.compactMap { if case .web(let u) = $0 { return u } else { return nil } }.first }
    var texts: [String] { items.compactMap { if case .text(let t) = $0 { return t } else { return nil } } }
    var files: [(URL, String)] { items.compactMap { if case .file(let u, let n) = $0 { return (u, n) } else { return nil } } }

    func load() async {
        let providers = ((context?.inputItems as? [NSExtensionItem]) ?? []).flatMap { $0.attachments ?? [] }
        for p in providers {
            if let item = await Self.read(p) { items.append(item) }
        }
        loading = false
    }

    nonisolated private static func read(_ p: NSItemProvider) async -> Item? {
        let fileTypes: [UTType] = [.image, .movie, .pdf, .audio, .data]
        let isFileLike = fileTypes.contains { p.hasItemConformingToTypeIdentifier($0.identifier) } && !p.hasItemConformingToTypeIdentifier(UTType.url.identifier)
        if isFileLike || p.hasItemConformingToTypeIdentifier(UTType.fileURL.identifier) {
            let type = p.registeredTypeIdentifiers.first { UTType($0)?.conforms(to: .data) == true } ?? UTType.data.identifier
            if let f = await copyFile(p, type: type) { return f }
        }
        if p.hasItemConformingToTypeIdentifier(UTType.url.identifier) {
            if let url = await loadItem(p, UTType.url.identifier) as? URL {
                if url.isFileURL, let f = copy(url) { return f }
                if !url.isFileURL { return .web(url) }
            }
        }
        if p.hasItemConformingToTypeIdentifier(UTType.plainText.identifier) {
            if let t = await loadItem(p, UTType.plainText.identifier) as? String {
                if let u = URL(string: t.trimmingCharacters(in: .whitespacesAndNewlines)), let s = u.scheme, s.hasPrefix("http") { return .web(u) }
                return .text(t)
            }
        }
        return nil
    }

    nonisolated private static func loadItem(_ p: NSItemProvider, _ type: String) async -> NSSecureCoding? {
        await withCheckedContinuation { cont in
            p.loadItem(forTypeIdentifier: type, options: nil) { item, _ in cont.resume(returning: item) }
        }
    }

    nonisolated private static func copyFile(_ p: NSItemProvider, type: String) async -> Item? {
        await withCheckedContinuation { cont in
            p.loadFileRepresentation(forTypeIdentifier: type) { url, _ in
                cont.resume(returning: url.flatMap { copy($0, suggested: p.suggestedName) })
            }
        }
    }

    /// The system deletes shared files as soon as the callback returns, so keep our own copy.
    nonisolated private static func copy(_ url: URL, suggested: String? = nil) -> Item? {
        let dir = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString, isDirectory: true)
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        var name = url.lastPathComponent
        if let s = suggested, !s.isEmpty, !url.pathExtension.isEmpty, !s.hasSuffix("." + url.pathExtension) { name = s + "." + url.pathExtension }
        let dest = dir.appendingPathComponent(name)
        let scoped = url.startAccessingSecurityScopedResource()
        defer { if scoped { url.stopAccessingSecurityScopedResource() } }
        do { try FileManager.default.copyItem(at: url, to: dest); return .file(dest, name) } catch { return nil }
    }

    // ───────── actions ─────────

    func run(_ title: String, _ work: @escaping (BridgeClient) async throws -> Void) {
        guard let p = pairing else { return }
        status = title; failed = false
        Task {
            do {
                try await work(BridgeClient(pairing: p, timeout: 20))
                status = "Done"; done = true
                UINotificationFeedbackGenerator().notificationOccurred(.success)
            } catch {
                status = error.localizedDescription; failed = true
            }
        }
    }

    func usePairingLink(_ text: String) {
        if let p = Pairing.parse(text) { PairingStore.save(p); pairing = p }
        else { status = "That isn't a Cat Bridge pairing link."; failed = true }
    }
}

struct ShareSheetView: View {
    @Bindable var model: ShareModel
    let close: () -> Void
    @State private var link = ""

    private let bg = Color(red: 0.055, green: 0.055, blue: 0.063)
    private let card = Color(red: 0.094, green: 0.094, blue: 0.106)
    private let accent = Color(red: 1.0, green: 0.70, blue: 0.48)

    var body: some View {
        VStack(spacing: 0) {
            Spacer(minLength: 0)
            VStack(alignment: .leading, spacing: 14) {
                HStack {
                    Image(systemName: "laptopcomputer").foregroundStyle(accent)
                    Text(model.pairing.map { "Send to \($0.name)" } ?? "Send to Laptop").font(.system(size: 18, weight: .bold, design: .rounded))
                    Spacer()
                    Button { close() } label: { Image(systemName: "xmark.circle.fill").font(.system(size: 24)).foregroundStyle(.secondary) }
                }
                if model.pairing == nil { setup } else if model.loading { ProgressView().frame(maxWidth: .infinity) } else { content }
                if let s = model.status {
                    HStack(spacing: 8) {
                        if !model.done && !model.failed { ProgressView() }
                        if model.done { Image(systemName: "checkmark.circle.fill").foregroundStyle(.green) }
                        Text(s).font(.system(size: 14, design: .rounded)).foregroundStyle(model.failed ? .red : .secondary)
                    }
                }
            }
            .padding(20)
            .background(card, in: RoundedRectangle(cornerRadius: 26, style: .continuous))
            .padding(12)
        }
        .preferredColorScheme(.dark)
        .onChange(of: model.done) { _, d in if d { DispatchQueue.main.asyncAfter(deadline: .now() + 0.7) { close() } } }
    }

    @ViewBuilder private var content: some View {
        if let u = model.webURL {
            Text(u.absoluteString).font(.system(size: 14, design: .rounded)).foregroundStyle(.secondary).lineLimit(2)
            big("Open on laptop", "safari") { model.run("Opening…") { try await $0.openLink(u.absoluteString) } }
            small("Copy to laptop clipboard") { model.run("Copying…") { try await $0.setLaptopClipboard(u.absoluteString) } }
        }
        if !model.texts.isEmpty {
            let t = model.texts.joined(separator: "\n")
            Text(t).font(.system(size: 14, design: .rounded)).foregroundStyle(.secondary).lineLimit(4)
            big("Copy to laptop clipboard", "doc.on.clipboard") { model.run("Copying…") { try await $0.setLaptopClipboard(t) } }
        }
        if !model.files.isEmpty {
            let files = model.files
            Text(files.count == 1 ? files[0].1 : "\(files.count) items").font(.system(size: 14, design: .rounded)).foregroundStyle(.secondary).lineLimit(2)
            big(files.count == 1 ? "Send to laptop" : "Send \(files.count) items to laptop", "arrow.up.circle") {
                model.run("Sending…") { c in
                    for (i, f) in files.enumerated() {
                        await MainActor.run { model.status = files.count > 1 ? "Sending \(i + 1) of \(files.count)…" : "Sending…" }
                        try await c.upload(fileAt: f.0, name: f.1)
                    }
                }
            }
        }
        if model.webURL == nil && model.texts.isEmpty && model.files.isEmpty {
            Text("There's nothing here Cat Bridge can send.").font(.system(size: 14, design: .rounded)).foregroundStyle(.secondary)
        }
    }

    @ViewBuilder private var setup: some View {
        Text("Open the Cat Bridge app once so this works here. Or paste the pairing link from Cat Center → iPhone → Copy pairing link:")
            .font(.system(size: 14, design: .rounded)).foregroundStyle(.secondary)
        TextField("catbridge://pair?…", text: $link).textInputAutocapitalization(.never).autocorrectionDisabled()
            .padding(12).background(bg, in: RoundedRectangle(cornerRadius: 14, style: .continuous))
        big("Use this link", "link") { model.usePairingLink(link.isEmpty ? (UIPasteboard.general.string ?? "") : link) }
    }

    private func big(_ title: String, _ icon: String, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Label(title, systemImage: icon).font(.system(size: 16, weight: .semibold, design: .rounded))
                .foregroundStyle(Color(red: 0.1, green: 0.06, blue: 0.03))
                .frame(maxWidth: .infinity).padding(.vertical, 14)
                .background(accent, in: RoundedRectangle(cornerRadius: 16, style: .continuous))
        }
        .disabled(model.status != nil && !model.failed)
    }

    private func small(_ title: String, action: @escaping () -> Void) -> some View {
        Button(title, action: action).font(.system(size: 15, weight: .semibold, design: .rounded)).frame(maxWidth: .infinity)
            .disabled(model.status != nil && !model.failed)
    }
}
