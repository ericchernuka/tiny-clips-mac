import AppKit
import SwiftUI

class CountdownWindow: NSPanel {
    private var completion: (() -> Void)?
    private var countdownTimer: Timer?
    private let countdownState: CountdownState
    private let hostingView: NSHostingView<CountdownView>
    private var remaining: Int
    private var didComplete = false

    init(duration: Int, completion: @escaping () -> Void) {
        let initialRemaining = max(1, duration)
        self.remaining = initialRemaining
        self.countdownState = CountdownState(remaining: initialRemaining)
        self.hostingView = NSHostingView(rootView: CountdownView(state: countdownState))
        self.completion = completion
        super.init(
            contentRect: NSRect(x: 0, y: 0, width: 120, height: 120),
            styleMask: [.borderless, .nonactivatingPanel],
            backing: .buffered,
            defer: false
        )
        self.isReleasedWhenClosed = false
        self.level = .floating
        self.isOpaque = false
        self.backgroundColor = .clear
        self.hasShadow = true
        self.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]

        hostingView.frame = NSRect(x: 0, y: 0, width: 120, height: 120)
        self.contentView = hostingView
        updateDisplay()
    }

    func show() {
        if let screen = NSScreen.main {
            let x = screen.frame.midX - frame.width / 2
            let y = screen.frame.midY - frame.height / 2
            setFrameOrigin(NSPoint(x: x, y: y))
        }
        alphaValue = 0
        orderFront(nil)
        NSAnimationContext.runAnimationGroup { context in
            context.duration = 0.18
            animator().alphaValue = 1
        }
        startCountdown()
    }

    private func startCountdown() {
        countdownTimer = Timer.scheduledTimer(withTimeInterval: 1, repeats: true) { [weak self] timer in
            guard let self else { timer.invalidate(); return }
            self.remaining -= 1
            if self.remaining <= 0 {
                timer.invalidate()
                self.countdownTimer = nil
                NSAnimationContext.runAnimationGroup { context in
                    context.duration = 0.16
                    self.animator().alphaValue = 0
                } completionHandler: {
                    guard !self.didComplete else { return }
                    self.didComplete = true
                    let completion = self.completion
                    self.completion = nil
                    self.orderOut(nil)
                    completion?()
                }
            } else {
                self.updateDisplay()
            }
        }
    }

    private func updateDisplay() {
        countdownState.remaining = remaining
    }

    func cancel() {
        didComplete = true
        countdownTimer?.invalidate()
        countdownTimer = nil
        completion = nil
        orderOut(nil)
    }
}

private final class CountdownState: ObservableObject {
    @Published var remaining: Int

    init(remaining: Int) {
        self.remaining = remaining
    }
}

private struct CountdownView: View {
    @ObservedObject var state: CountdownState

    var body: some View {
        let remaining = state.remaining

        ZStack {
            Circle()
                .fill(.black.opacity(0.75))
                .frame(width: 100, height: 100)
                .overlay {
                    Circle()
                        .strokeBorder(.white.opacity(0.3), lineWidth: 2)
                }

            Text("\(remaining)")
                .font(.system(size: 48, weight: .bold, design: .rounded))
                .foregroundStyle(.white)
                .contentTransition(.numericText())
                .scaleEffect(remaining == 1 ? 1.25 : 1)
                .opacity(remaining == 1 ? 1 : 0.92)
                .animation(.spring(response: 0.28, dampingFraction: 0.72), value: remaining)
                .accessibilityLabel("Countdown")
                .accessibilityValue("\(remaining) seconds")
        }
        .frame(width: 120, height: 120)
        .transition(.opacity.combined(with: .scale(scale: 0.92)))
    }
}
