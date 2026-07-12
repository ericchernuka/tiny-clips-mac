import Foundation
import SwiftUI
import AppKit

extension Binding where Value == Int {
	var doubleValue: Binding<Double> {
		Binding<Double>(
			get: { Double(wrappedValue) },
			set: { wrappedValue = Int($0) }
		)
	}
}

struct QuickBugReportContext {
    let platform: String
    let version: String
    let build: String
    let distribution: String
    let osVersion: String
}

enum QuickBugReportURLBuilder {
    static func makeURL(title: String, happened: String, context: QuickBugReportContext) -> URL {
        var components = URLComponents(string: "https://github.com/jamesmontemagno/tiny-clips/issues/new")!
        components.queryItems = [
            URLQueryItem(name: "template", value: "quick_bug_report.yml"),
            URLQueryItem(name: "labels", value: "bug"),
            URLQueryItem(name: "title", value: "[Bug]: \(title)"),
            URLQueryItem(name: "happened", value: happened),
            URLQueryItem(name: "platform", value: context.platform),
            URLQueryItem(name: "version", value: context.version),
            URLQueryItem(name: "build", value: context.build),
            URLQueryItem(name: "distribution", value: context.distribution),
            URLQueryItem(name: "os", value: context.osVersion)
        ]
        return components.url!
    }
}

struct QuickBugReportFormView: View {
    let context: QuickBugReportContext
    let onSubmit: (_ title: String, _ happened: String) -> Void

    @Environment(\.dismiss) private var dismiss
    @State private var title = ""
    @State private var happened = ""

    private var canSubmit: Bool {
        !title.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty &&
        !happened.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("File a Bug")
                .font(.title3)
                .bold()

            TextField("Bug title", text: $title)

            VStack(alignment: .leading, spacing: 6) {
                Text("What happened?")
                    .font(.subheadline)
                TextEditor(text: $happened)
                    .frame(minHeight: 140)
                    .overlay(
                        RoundedRectangle(cornerRadius: 6)
                            .stroke(Color(nsColor: .separatorColor), lineWidth: 1)
                    )
            }

            Text("App info will be auto-filled: \(context.platform), v\(context.version) (\(context.build)), \(context.distribution), \(context.osVersion)")
                .font(.caption)
                .foregroundStyle(.secondary)

            HStack {
                Spacer()
                Button("Cancel", role: .cancel) {
                    dismiss()
                }
                Button("File on GitHub…") {
                    onSubmit(
                        title.trimmingCharacters(in: .whitespacesAndNewlines),
                        happened.trimmingCharacters(in: .whitespacesAndNewlines)
                    )
                    dismiss()
                }
                .disabled(!canSubmit)
            }
        }
        .padding(16)
        .frame(minWidth: 520)
    }
}
