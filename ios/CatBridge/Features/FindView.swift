import SwiftUI
import MapKit
import CatBridgeCore

/// Where's my laptop? Map, loud sound, lost mode.
struct FindView: View {
    @Environment(AppModel.self) private var model
    @State private var position: MapCameraPosition = .automatic
    @State private var location: LaptopLocation? = FindView.lastKnown
    @State private var loading = false
    @State private var error: String?
    @State private var lostMessage = UserDefaults.standard.string(forKey: "lost.message") ?? ""
    @State private var ringing = false

    static var lastKnown: LaptopLocation? {
        guard let d = UserDefaults.standard.data(forKey: "lastLocation.v1") else { return nil }
        return try? JSONDecoder().decode(LaptopLocation.self, from: d)
    }

    private var coordinate: CLLocationCoordinate2D? {
        guard let lat = location?.latitude, let lon = location?.longitude else { return nil }
        return CLLocationCoordinate2D(latitude: lat, longitude: lon)
    }

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 14) {
                Map(position: $position) {
                    if let c = coordinate {
                        if let acc = location?.accuracy, acc > 0 {
                            MapCircle(center: c, radius: acc).foregroundStyle(Theme.accent.opacity(0.18)).stroke(Theme.accent.opacity(0.6), lineWidth: 1)
                        }
                        Marker(model.pairing?.name ?? "Laptop", systemImage: "laptopcomputer", coordinate: c).tint(Theme.accent)
                    }
                }
                .frame(height: 300)
                .clipShape(RoundedRectangle(cornerRadius: 22, style: .continuous))
                .overlay(alignment: .topTrailing) {
                    if loading { ProgressView().padding(10).background(.ultraThinMaterial, in: Circle()).padding(10) }
                }

                CardBox {
                    if let l = location {
                        Label(l.latitude == nil ? "Location unknown" : (Fmt.ago(l.time) == "now" ? "Seen just now" : "Seen \(Fmt.ago(l.time)) ago"), systemImage: "location.fill")
                            .font(.system(size: 16, weight: .semibold, design: .rounded))
                        if let p = l.problem { Text(p).font(.system(size: 14, design: .rounded)).foregroundStyle(Theme.yellow) }
                        info("Wi-Fi", l.wifi)
                        info("Accuracy", l.accuracy.map { "about \(Int($0)) m" })
                        info("Battery", l.battery.map { "\($0)%" + ((l.charging ?? false) ? " · charging" : "") })
                        info("Internet address", l.publicIp)
                    } else {
                        Text("Your laptop's location shows here. It needs Location turned on for desktop apps on the laptop.")
                            .font(.system(size: 14, design: .rounded)).foregroundStyle(Theme.dim)
                    }
                    if let error { Text(error).font(.system(size: 14, design: .rounded)).foregroundStyle(Theme.red) }
                    HStack {
                        SecondaryButton(title: "Refresh", icon: "arrow.clockwise") { Task { await locate() } }
                        if let c = coordinate {
                            SecondaryButton(title: "Directions", icon: "arrow.triangle.turn.up.right.diamond") {
                                let item = MKMapItem(placemark: MKPlacemark(coordinate: c))
                                item.name = model.pairing?.name ?? "Laptop"
                                item.openInMaps(launchOptions: [MKLaunchOptionsDirectionsModeKey: MKLaunchOptionsDirectionsModeWalking])
                            }
                        }
                    }
                }

                SectionTitle(text: "Make it ring")
                PrimaryButton(title: ringing ? "Stop the sound" : "Play a loud sound", icon: ringing ? "speaker.slash.fill" : "speaker.wave.3.fill") {
                    let on = !ringing
                    ringing = on
                    model.act(on ? "Ringing at full volume" : "Stopped") { try await $0.alarm(on) }
                }

                SectionTitle(text: "Lost mode")
                CardBox {
                    Text("Locks the laptop and shows your message on its screen and on the sign-in screen.")
                        .font(.system(size: 14, design: .rounded)).foregroundStyle(Theme.dim)
                    TextField("Message, e.g. Please call 0803 000 0000", text: $lostMessage, axis: .vertical)
                        .lineLimit(1...3)
                        .padding(12).background(Theme.bg, in: RoundedRectangle(cornerRadius: 14, style: .continuous))
                    HStack {
                        SecondaryButton(title: "Turn on", icon: "lock.fill") {
                            UserDefaults.standard.set(lostMessage, forKey: "lost.message")
                            let m = lostMessage
                            model.act("Lost mode is on") { try await $0.lostMode(true, message: m) }
                        }
                        SecondaryButton(title: "Turn off", icon: "lock.open") { model.act("Lost mode is off") { try await $0.lostMode(false, message: nil) } }
                    }
                }
            }
            .padding(16)
        }
        .background(Theme.bg)
        .navigationTitle("Find my laptop")
        .navigationBarTitleDisplayMode(.inline)
        .task { await locate() }
    }

    private func info(_ label: String, _ value: String?) -> some View {
        Group {
            if let value {
                HStack {
                    Text(label).foregroundStyle(Theme.dim)
                    Spacer()
                    Text(value).multilineTextAlignment(.trailing)
                }
                .font(.system(size: 14, design: .rounded))
            }
        }
    }

    private func locate() async {
        guard let c = model.client else { return }
        loading = true; error = nil
        do {
            let l = try await c.locate()
            location = l
            if let d = try? JSONEncoder().encode(l) { UserDefaults.standard.set(d, forKey: "lastLocation.v1") }
            if let lat = l.latitude, let lon = l.longitude {
                withAnimation { position = .region(MKCoordinateRegion(center: .init(latitude: lat, longitude: lon), latitudinalMeters: max(600, (l.accuracy ?? 100) * 4), longitudinalMeters: max(600, (l.accuracy ?? 100) * 4))) }
            }
        } catch {
            self.error = "Can't reach the laptop right now" + (location != nil ? ", showing where it was last seen." : ".")
            if let lat = location?.latitude, let lon = location?.longitude {
                position = .region(MKCoordinateRegion(center: .init(latitude: lat, longitude: lon), latitudinalMeters: 800, longitudinalMeters: 800))
            }
        }
        loading = false
    }
}
