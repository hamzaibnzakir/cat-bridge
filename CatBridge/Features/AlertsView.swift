import SwiftUI
import UIKit
import CatBridgeCore

struct AlertsView: View {
    @Environment(AppModel.self) private var model
    @State private var tab = 0

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(alignment: .leading, spacing: 12) {
                    Picker("", selection: $tab) {
                        Text("On the laptop").tag(0)
                        Text("Sent to me").tag(1)
                    }
                    .pickerStyle(.segmented)
                    .padding(.top, 4)

                    if tab == 0 { laptop } else { inbox }
                }
                .padding(.horizontal, 16)
                .padding(.bottom, 30)
            }
            .refreshable { await model.refresh() }
            .background(Theme.bg)
            .navigationTitle("Alerts")
        }
    }

    @ViewBuilder private var laptop: some View {
        if model.alerts.isEmpty {
            empty("No notifications on the laptop", "bell.slash")
        } else {
            ForEach(model.alerts) { a in AlertRow(entry: a) }
        }
    }

    @ViewBuilder private var inbox: some View {
        if model.inbox.isEmpty {
            empty("Nothing from your laptop yet. Use \"Send clipboard to iPhone\" on the laptop.", "tray")
        } else {
            ForEach(model.inbox) { i in
                CardBox(padding: 14) {
                    HStack(alignment: .top) {
                        Image(systemName: i.kind == "link" ? "link" : "doc.on.clipboard")
                            .foregroundStyle(Theme.accent)
                            .frame(width: 34, height: 34).background(Theme.cardHi, in: RoundedRectangle(cornerRadius: 10, style: .continuous))
                        VStack(alignment: .leading, spacing: 4) {
                            Text(i.title).font(.system(size: 13, weight: .semibold, design: .rounded)).foregroundStyle(Theme.dim)
                            Text(i.text).font(.system(size: 15, design: .rounded)).lineLimit(6).textSelection(.enabled)
                        }
                        Spacer(minLength: 0)
                        Text(Fmt.ago(i.time)).font(.system(size: 12, design: .rounded)).foregroundStyle(Theme.faint)
                    }
                    HStack {
                        SecondaryButton(title: "Copy", icon: "doc.on.doc") { UIPasteboard.general.string = i.text; model.show("Copied") }
                        if let s = i.url, let u = URL(string: s) {
                            SecondaryButton(title: "Open", icon: "safari") { UIApplication.shared.open(u) }
                        }
                    }
                }
            }
        }
    }

    private func empty(_ text: String, _ icon: String) -> some View {
        VStack(spacing: 12) {
            Image(systemName: icon).font(.system(size: 30)).foregroundStyle(Theme.faint)
            Text(text).font(.system(size: 15, design: .rounded)).foregroundStyle(Theme.dim).multilineTextAlignment(.center)
        }
        .frame(maxWidth: .infinity)
        .padding(.vertical, 60)
    }
}

struct AlertRow: View {
    let entry: AlertEntry

    private var isShopify: Bool { (entry.app + entry.title).localizedCaseInsensitiveContains("shopify") }

    var body: some View {
        HStack(alignment: .top, spacing: 12) {
            Image(systemName: isShopify ? "bag.fill" : entry.fromPhone ? "iphone" : "bell.fill")
                .font(.system(size: 15, weight: .semibold))
                .foregroundStyle(.white)
                .frame(width: 38, height: 38)
                .background(isShopify ? Theme.shopify : Theme.cardHi, in: RoundedRectangle(cornerRadius: 11, style: .continuous))
            VStack(alignment: .leading, spacing: 3) {
                HStack {
                    Text(entry.app.replacingOccurrences(of: "iPhone · ", with: "").uppercased())
                        .font(.system(size: 11, weight: .semibold, design: .rounded)).foregroundStyle(Theme.dim).lineLimit(1)
                    if entry.fromPhone {
                        Text("FROM IPHONE").font(.system(size: 10, weight: .bold, design: .rounded)).foregroundStyle(Theme.accent)
                    }
                    Spacer()
                    Text(Fmt.ago(entry.time)).font(.system(size: 12, design: .rounded)).foregroundStyle(Theme.faint)
                }
                Text(entry.title).font(.system(size: 15, weight: .semibold, design: .rounded)).lineLimit(2)
                if !entry.body.isEmpty {
                    Text(entry.body).font(.system(size: 14, design: .rounded)).foregroundStyle(Theme.dim).lineLimit(4)
                }
            }
        }
        .padding(14)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Theme.card, in: RoundedRectangle(cornerRadius: 18, style: .continuous))
        .overlay(RoundedRectangle(cornerRadius: 18, style: .continuous).stroke(Theme.edge, lineWidth: 1))
    }
}
