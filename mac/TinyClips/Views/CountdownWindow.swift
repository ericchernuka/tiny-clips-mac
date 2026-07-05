import AppKit
import SwiftUI

class CountdownWindow: NSPanel {
    private var completion: (() -> Void)?
    private var countdownTimer: Timer?
    private var remaining: Int

    init(duration: Int, completion: @escaping () -> Void) {
        self.remaining = duration
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
                NSAnimationContext.runAnimationGroup { context in
                    context.duration = 0.16
                    self.animator().alphaValue = 0
                } completionHandler: {
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
        let hostingView = NSHostingView(rootView: CountdownView(remaining: remaining))
        hostingView.frame = NSRect(x: 0, y: 0, width: 120, height: 120)
        self.contentView = hostingView
    }

    func cancel() {
        countdownTimer?.invalidate()
        countdownTimer = nil
        completion = nil
        orderOut(nil)
    }
}

private struct CountdownView: View {
    let remaining: Int

    var body: some View {
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
