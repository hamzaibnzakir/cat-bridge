import SwiftUI
import PhotosUI
import UniformTypeIdentifiers
import CatBridgeCore

/// A photo or video picked in PhotosPicker, copied to a temporary file (so big videos never sit in memory).
struct PickedFile: Transferable {
    let url: URL
    static var transferRepresentation: some TransferRepresentation {
        FileRepresentation(importedContentType: .item) { received in
            let dir = FileManager.default.temporaryDirectory.appendingPathComponent("CatBridgeUploads", isDirectory: true)
            try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
            let dest = dir.appendingPathComponent(String(UUID().uuidString.prefix(6)) + "-" + received.file.lastPathComponent)
            try FileManager.default.copyItem(at: received.file, to: dest)
            return PickedFile(url: dest)
        }
    }
}

struct FilesView: View {
    @Environment(AppModel.self) private var model
    @State private var picks: [PhotosPickerItem] = []
    @State private var importing = false
    @State private var progress: String?
    @State private var shareURL: URL?
    @State private var downloading: Int?

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(alignment: .leading, spacing: 14) {
                    SectionTitle(text: "Send to laptop")
                    CardBox {
                        Text("Files land in Downloads\\Cat Bridge on the laptop and on the island's shelf.")
                            .font(.system(size: 14, design: .rounded)).foregroundStyle(Theme.dim)
                        PhotosPicker(selection: $picks, maxSelectionCount: 30, matching: .any(of: [.images, .videos])) {
                            Label("Photos and videos", systemImage: "photo.on.rectangle")
                                .font(.system(size: 16, weight: .semibold, design: .rounded))
                                .foregroundStyle(Theme.onAccent)
                                .frame(maxWidth: .infinity).padding(.vertical, 14)
                                .background(Theme.accent, in: RoundedRectangle(cornerRadius: 16, style: .continuous))
                        }
                        .buttonStyle(PressStyle())
                        SecondaryButton(title: "Files", icon: "folder") { importing = true }
                        if let progress {
                            HStack(spacing: 10) {
                                ProgressView()
                                Text(progress).font(.system(size: 14, design: .rounded)).foregroundStyle(Theme.dim)
                            }
                        }
                    }

                    SectionTitle(text: "On the laptop's shelf")
                    if model.shelf.isEmpty {
                        CardBox {
                            Text("Nothing on the shelf. Drop files on the island or the desktop cat to get them here.")
                                .font(.system(size: 14, design: .rounded)).foregroundStyle(Theme.dim)
                        }
                    } else {
                        VStack(spacing: 8) {
                            ForEach(model.shelf) { e in shelfRow(e) }
                        }
                    }
                }
                .padding(.horizontal, 16)
                .padding(.bottom, 30)
            }
            .refreshable { await model.refresh() }
            .background(Theme.bg)
            .navigationTitle("Files")
            .onChange(of: picks) { _, items in
                guard !items.isEmpty else { return }
                let batch = items
                picks = []
                Task { await sendPhotos(batch) }
            }
            .fileImporter(isPresented: $importing, allowedContentTypes: [.item], allowsMultipleSelection: true) { result in
                if case .success(let urls) = result { Task { await sendFiles(urls) } }
            }
            .sheet(item: Binding(get: { shareURL.map { SharedFile(url: $0) } }, set: { shareURL = $0?.url })) { f in
                ShareSheet(items: [f.url]).presentationDetents([.medium, .large])
            }
        }
    }

    private struct SharedFile: Identifiable { let url: URL; var id: String { url.path } }

    private func shelfRow(_ e: ShelfEntry) -> some View {
        HStack(spacing: 12) {
            Image(systemName: e.isFolder ? "folder.fill" : icon(for: e.name))
                .foregroundStyle(Theme.accent)
                .frame(width: 38, height: 38)
                .background(Theme.cardHi, in: RoundedRectangle(cornerRadius: 11, style: .continuous))
            VStack(alignment: .leading, spacing: 2) {
                Text(e.name).font(.system(size: 15, weight: .semibold, design: .rounded)).lineLimit(1)
                Text(e.isFolder ? "Folder (can't download)" : Fmt.size(e.size)).font(.system(size: 13, design: .rounded)).foregroundStyle(Theme.dim)
            }
            Spacer()
            if !e.isFolder {
                Button {
                    download(e)
                } label: {
                    if downloading == e.index { ProgressView() }
                    else { Image(systemName: "arrow.down.circle.fill").font(.system(size: 26)).foregroundStyle(Theme.accent) }
                }
                .disabled(downloading != nil)
            }
        }
        .padding(12)
        .background(Theme.card, in: RoundedRectangle(cornerRadius: 18, style: .continuous))
        .overlay(RoundedRectangle(cornerRadius: 18, style: .continuous).stroke(Theme.edge, lineWidth: 1))
    }

    private func icon(for name: String) -> String {
        let ext = (name as NSString).pathExtension.lowercased()
        if ["jpg", "jpeg", "png", "heic", "gif", "webp"].contains(ext) { return "photo" }
        if ["mp4", "mov", "m4v", "mkv"].contains(ext) { return "film" }
        if ["mp3", "m4a", "wav", "flac"].contains(ext) { return "music.note" }
        if ["pdf"].contains(ext) { return "doc.richtext" }
        if ["zip", "rar", "7z"].contains(ext) { return "doc.zipper" }
        return "doc"
    }

    private func download(_ e: ShelfEntry) {
        guard let c = model.client else { return }
        downloading = e.index
        Task {
            do { shareURL = try await c.download(e) }
            catch { model.show(error.localizedDescription, error: true) }
            downloading = nil
        }
    }

    private func sendPhotos(_ items: [PhotosPickerItem]) async {
        guard let c = model.client else { return }
        var sent = 0
        for (i, item) in items.enumerated() {
            progress = "Sending \(i + 1) of \(items.count)…"
            do {
                if let f = try await item.loadTransferable(type: PickedFile.self) {
                    try await c.upload(fileAt: f.url, name: String(f.url.lastPathComponent.drop(while: { $0 != "-" }).dropFirst()))
                    try? FileManager.default.removeItem(at: f.url)
                    sent += 1
                } else if let data = try await item.loadTransferable(type: Data.self) {
                    let ext = item.supportedContentTypes.first?.preferredFilenameExtension ?? "jpg"
                    try await c.upload(data: data, name: "iPhone \(Date().formatted(.iso8601.year().month().day()))-\(i + 1).\(ext)")
                    sent += 1
                }
            } catch {
                model.show(error.localizedDescription, error: true)
            }
        }
        progress = nil
        if sent > 0 { model.show(sent == 1 ? "Sent to laptop" : "\(sent) items sent to laptop") }
        await model.refresh()
    }

    private func sendFiles(_ urls: [URL]) async {
        guard let c = model.client else { return }
        var sent = 0
        for (i, url) in urls.enumerated() {
            progress = "Sending \(i + 1) of \(urls.count)…"
            let scoped = url.startAccessingSecurityScopedResource()
            defer { if scoped { url.stopAccessingSecurityScopedResource() } }
            do { try await c.upload(fileAt: url); sent += 1 }
            catch { model.show(error.localizedDescription, error: true) }
        }
        progress = nil
        if sent > 0 { model.show(sent == 1 ? "Sent to laptop" : "\(sent) files sent to laptop") }
        await model.refresh()
    }
}
