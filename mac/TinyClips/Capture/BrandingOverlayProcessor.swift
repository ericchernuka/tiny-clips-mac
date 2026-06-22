import AppKit
import AVFoundation
import CoreGraphics
import CoreText
import QuartzCore

// MARK: - Branding Overlay Processor

/// Renders a "Captured on Tiny Clips" watermark in the bottom-right corner of
/// screenshots, video recordings, and GIFs.
enum BrandingOverlayProcessor {
    private static let overlayText = "Captured on Tiny Clips"

    struct WebcamOverlayOptions: Sendable {
        let videoURL: URL
        let shape: String
        let corner: String
        let size: String
        let cornerRadiusOverride: CGFloat?
    }

    enum CompositionError: LocalizedError {
        case sourceVideoTrackMissing
        case videoCompositionTrackCreationFailed
        case webcamCompositionTrackCreationFailed
        case webcamVideoTrackMissing(URL)
        case exportSessionCreationFailed

        var errorDescription: String? {
            switch self {
            case .sourceVideoTrackMissing:
                return "Could not load the source video track for export composition."
            case .videoCompositionTrackCreationFailed:
                return "Could not create a mutable video track for export composition."
            case .webcamCompositionTrackCreationFailed:
                return "Could not create a mutable webcam track for export composition."
            case let .webcamVideoTrackMissing(url):
                return "Webcam overlay video is missing or invalid: \(url.lastPathComponent)."
            case .exportSessionCreationFailed:
                return "Could not create the video export session."
            }
        }
    }

    private enum WebcamOverlayShape: String {
        case circle
        case rounded
        case rectangle

        init(rawValue: String) {
            switch rawValue.lowercased() {
            case "rounded", "roundedrectangle":
                self = .rounded
            case "rectangle":
                self = .rectangle
            default:
                self = .circle
            }
        }
    }

    private enum WebcamOverlayCorner: String {
        case topLeft
        case topRight
        case bottomLeft
        case bottomRight

        init(rawValue: String) {
            switch rawValue.lowercased() {
            case "topleft":
                self = .topLeft
            case "topright":
                self = .topRight
            case "bottomleft":
                self = .bottomLeft
            default:
                self = .bottomRight
            }
        }
    }

    private enum WebcamOverlaySizePreset: String {
        case small
        case medium
        case large

        init(rawValue: String) {
            switch rawValue.lowercased() {
            case "small":
                self = .small
            case "large":
                self = .large
            default:
                self = .medium
            }
        }
    }

    // MARK: - Screenshot / Image

    /// Composites the branding overlay onto a CGImage and returns the result.
    static func applyToImage(_ image: CGImage) -> CGImage {
        let width = image.width
        let height = image.height

        guard let context = CGContext(
            data: nil,
            width: width,
            height: height,
            bitsPerComponent: 8,
            bytesPerRow: 0,
            space: CGColorSpaceCreateDeviceRGB(),
            bitmapInfo: CGImageAlphaInfo.premultipliedFirst.rawValue | CGBitmapInfo.byteOrder32Little.rawValue
        ) else { return image }

        context.draw(image, in: CGRect(x: 0, y: 0, width: width, height: height))
        drawTextOverlay(in: context, width: width, height: height)
        return context.makeImage() ?? image
    }

    // MARK: - GIF

    /// Composites the branding overlay onto every frame of a GIF.
    static func applyToGifData(_ gifData: GifCaptureData) -> GifCaptureData {
        guard !gifData.frames.isEmpty else { return gifData }
        let processedFrames = gifData.frames.map { applyToImage($0) }
        return GifCaptureData(frames: processedFrames, frameDelay: gifData.frameDelay, maxWidth: gifData.maxWidth)
    }

    // MARK: - Video

    /// Burns the branding overlay into a video file using AVVideoComposition and
    /// CoreAnimation layers, writing the result to `outputURL`.
    static func overlayOnVideo(
        sourceURL: URL,
        outputURL: URL,
        includeBranding: Bool = true,
        webcamOverlay: WebcamOverlayOptions? = nil,
        onProgress: ((Double) -> Void)? = nil
    ) async throws -> URL {
        let asset = AVURLAsset(url: sourceURL)
        onProgress?(0.1)
        guard let videoTrack = try await asset.loadTracks(withMediaType: .video).first else {
            throw CompositionError.sourceVideoTrackMissing
        }

        let assetDuration = try await asset.load(.duration)
        let preferredTransform = try await videoTrack.load(.preferredTransform)
        onProgress?(0.25)

        let composition = AVMutableComposition()
        guard let compositionVideoTrack = composition.addMutableTrack(
            withMediaType: .video,
            preferredTrackID: kCMPersistentTrackID_Invalid
        ) else { throw CompositionError.videoCompositionTrackCreationFailed }

        try compositionVideoTrack.insertTimeRange(
            CMTimeRange(start: .zero, duration: assetDuration),
            of: videoTrack,
            at: .zero
        )
        compositionVideoTrack.preferredTransform = preferredTransform

        for audioTrack in try await asset.loadTracks(withMediaType: .audio) {
            if let compositionAudioTrack = composition.addMutableTrack(
                withMediaType: .audio,
                preferredTrackID: kCMPersistentTrackID_Invalid
            ) {
                try compositionAudioTrack.insertTimeRange(
                    CMTimeRange(start: .zero, duration: assetDuration),
                    of: audioTrack,
                    at: .zero
                )
            }
        }

        let naturalSize = try await videoTrack.load(.naturalSize)
        let transformedSize = naturalSize.applying(preferredTransform)
        let renderSize = CGSize(width: abs(transformedSize.width), height: abs(transformedSize.height))

        let videoComposition = AVMutableVideoComposition()
        videoComposition.renderSize = renderSize
        let nominalFrameRate = try await videoTrack.load(.nominalFrameRate)
        let sourceTimescale = max(30, Int32(nominalFrameRate.rounded(.up)))
        videoComposition.frameDuration = CMTime(value: 1, timescale: sourceTimescale)

        let instruction = AVMutableVideoCompositionInstruction()
        instruction.timeRange = CMTimeRange(start: .zero, duration: assetDuration)
        let screenLayerInstruction = AVMutableVideoCompositionLayerInstruction(assetTrack: compositionVideoTrack)
        screenLayerInstruction.setTransform(preferredTransform, at: .zero)

        var layerInstructions: [AVMutableVideoCompositionLayerInstruction] = [screenLayerInstruction]

        let parentLayer = CALayer()
        parentLayer.frame = CGRect(origin: .zero, size: renderSize)
        parentLayer.isGeometryFlipped = true

        let screenVideoLayer = CALayer()
        screenVideoLayer.frame = CGRect(origin: .zero, size: renderSize)
        parentLayer.addSublayer(screenVideoLayer)

        var compositionVideoLayers: [CALayer] = [screenVideoLayer]

        if let webcamOverlay, FileManager.default.fileExists(atPath: webcamOverlay.videoURL.path) {
            let webcamAsset = AVURLAsset(url: webcamOverlay.videoURL)
            guard let webcamTrack = try await webcamAsset.loadTracks(withMediaType: .video).first else {
                throw CompositionError.webcamVideoTrackMissing(webcamOverlay.videoURL)
            }

            let webcamDuration = try await webcamAsset.load(.duration)
            let webcamPreferredTransform = try await webcamTrack.load(.preferredTransform)
            let webcamNaturalSize = try await webcamTrack.load(.naturalSize)
            let webcamOrientedSize = orientedSize(
                for: webcamNaturalSize,
                preferredTransform: webcamPreferredTransform
            )

            guard let compositionWebcamTrack = composition.addMutableTrack(
                withMediaType: .video,
                preferredTrackID: kCMPersistentTrackID_Invalid
            ) else {
                throw CompositionError.webcamCompositionTrackCreationFailed
            }

            let webcamTimeRange = CMTimeRange(start: .zero, duration: min(assetDuration, webcamDuration))
            try compositionWebcamTrack.insertTimeRange(
                webcamTimeRange,
                of: webcamTrack,
                at: .zero
            )

            let overlayFrame = webcamOverlayFrame(
                renderSize: renderSize,
                webcamSize: webcamOrientedSize,
                preset: WebcamOverlaySizePreset(rawValue: webcamOverlay.size),
                corner: WebcamOverlayCorner(rawValue: webcamOverlay.corner)
            )

            let normalizedWebcamTransform = normalizedTransform(
                for: webcamPreferredTransform,
                naturalSize: webcamNaturalSize
            )

            let scale = max(
                overlayFrame.width / max(webcamOrientedSize.width, 1),
                overlayFrame.height / max(webcamOrientedSize.height, 1)
            )
            let scaledSize = CGSize(
                width: webcamOrientedSize.width * scale,
                height: webcamOrientedSize.height * scale
            )
            let webcamOffset = CGPoint(
                x: overlayFrame.midX - (scaledSize.width / 2),
                y: overlayFrame.midY - (scaledSize.height / 2)
            )

            let webcamTransform = normalizedWebcamTransform
                .concatenating(CGAffineTransform(scaleX: scale, y: scale))
                .concatenating(CGAffineTransform(translationX: webcamOffset.x, y: webcamOffset.y))

            let webcamLayerInstruction = AVMutableVideoCompositionLayerInstruction(assetTrack: compositionWebcamTrack)
            webcamLayerInstruction.setTransform(webcamTransform, at: .zero)
            layerInstructions.insert(webcamLayerInstruction, at: 0)

            let webcamVideoLayer = CALayer()
            webcamVideoLayer.frame = overlayFrame
            webcamVideoLayer.masksToBounds = true

            if let maskLayer = webcamMaskLayer(
                shape: WebcamOverlayShape(rawValue: webcamOverlay.shape),
                bounds: webcamVideoLayer.bounds,
                cornerRadiusOverride: webcamOverlay.cornerRadiusOverride
            ) {
                webcamVideoLayer.mask = maskLayer
            }

            parentLayer.addSublayer(webcamVideoLayer)
            compositionVideoLayers.insert(webcamVideoLayer, at: 0)
        }

        instruction.layerInstructions = layerInstructions
        videoComposition.instructions = [instruction]

        if includeBranding {
            addBrandingLayer(to: parentLayer, renderSize: renderSize)
        }

        videoComposition.animationTool = AVVideoCompositionCoreAnimationTool(
            postProcessingAsVideoLayers: compositionVideoLayers,
            in: parentLayer
        )

        try? FileManager.default.removeItem(at: outputURL)

        guard let exportSession = AVAssetExportSession(asset: composition, presetName: AVAssetExportPresetHighestQuality) else {
            throw CompositionError.exportSessionCreationFailed
        }
        exportSession.outputURL = outputURL
        exportSession.outputFileType = .mp4
        exportSession.videoComposition = videoComposition
        exportSession.shouldOptimizeForNetworkUse = true

        onProgress?(0.8)
        try await exportSession.export(to: outputURL, as: .mp4)

        if FileManager.default.fileExists(atPath: outputURL.path) {
            try? FileManager.default.removeItem(at: sourceURL)
        }
        onProgress?(1.0)
        return outputURL
    }

    // MARK: - Private helpers

    /// Adds the branding badge as a single image-backed CALayer.
    ///
    /// We render the entire pill (background + text) into a CGImage rather than
    /// using CATextLayer, because AVVideoCompositionCoreAnimationTool frequently
    /// fails to render CATextLayer text reliably.
    ///
    /// `parentLayer.isGeometryFlipped = true`, so (0,0) is the top-left corner and
    /// (renderSize.width, renderSize.height) is the bottom-right corner.
    private static func addBrandingLayer(to parentLayer: CALayer, renderSize: CGSize) {
        let scale: CGFloat = 2.0
        let fontSize = badgeFontSize(for: renderSize.height)
        let ctFont = makeBadgeFont(size: fontSize)
        let textSize = measureBadgeText(font: ctFont)
        let (bgWidth, bgHeight, _, margin) = badgePillSize(textSize: textSize, fontSize: fontSize)

        guard let badgeImage = renderBadgeImage(width: bgWidth, height: bgHeight, fontSize: fontSize, scale: scale) else {
            return
        }

        let bgX = renderSize.width - bgWidth - margin
        let bgY = renderSize.height - bgHeight - margin

        let badgeLayer = CALayer()
        badgeLayer.frame = CGRect(x: bgX, y: bgY, width: bgWidth, height: bgHeight)
        badgeLayer.contents = badgeImage
        badgeLayer.contentsScale = scale
        badgeLayer.contentsGravity = .resize
        parentLayer.addSublayer(badgeLayer)
    }

    /// Renders the full pill+text badge to a CGImage at `scale` density.
    private static func renderBadgeImage(width: CGFloat, height: CGFloat, fontSize: CGFloat, scale: CGFloat) -> CGImage? {
        let pixelWidth = Int(ceil(width * scale))
        let pixelHeight = Int(ceil(height * scale))
        guard pixelWidth > 0, pixelHeight > 0 else { return nil }

        guard let context = CGContext(
            data: nil,
            width: pixelWidth,
            height: pixelHeight,
            bitsPerComponent: 8,
            bytesPerRow: 0,
            space: CGColorSpaceCreateDeviceRGB(),
            bitmapInfo: CGImageAlphaInfo.premultipliedFirst.rawValue | CGBitmapInfo.byteOrder32Little.rawValue
        ) else { return nil }

        context.scaleBy(x: scale, y: scale)
        drawBadge(in: context, rect: CGRect(x: 0, y: 0, width: width, height: height), fontSize: fontSize)
        return context.makeImage()
    }

    /// Draws the branding badge directly into a CGContext using Core Text
    /// (thread-safe, unlike NSString/TextKit which silently no-ops off main).
    ///
    /// CGContext uses a bottom-left origin (y=0 at bottom), so the badge is placed
    /// with a small margin from the right and bottom edges.
    private static func drawTextOverlay(in context: CGContext, width: Int, height: Int) {
        let fontSize = badgeFontSize(for: CGFloat(height))
        let ctFont = makeBadgeFont(size: fontSize)
        let textSize = measureBadgeText(font: ctFont)
        let (bgWidth, bgHeight, _, margin) = badgePillSize(textSize: textSize, fontSize: fontSize)

        let bgRect = CGRect(x: CGFloat(width) - bgWidth - margin, y: margin, width: bgWidth, height: bgHeight)
        drawBadge(in: context, rect: bgRect, fontSize: fontSize)
    }

    /// Shared primitive: draws the pill background and centered text into `rect`.
    private static func drawBadge(in context: CGContext, rect: CGRect, fontSize: CGFloat) {
        let ctFont = makeBadgeFont(size: fontSize)
        let (_, _, paddingH, _) = badgePillSize(textSize: measureBadgeText(font: ctFont), fontSize: fontSize)

        // Background pill.
        let cornerRadius = rect.height / 3
        let path = CGPath(roundedRect: rect, cornerWidth: cornerRadius, cornerHeight: cornerRadius, transform: nil)
        context.setFillColor(NSColor.black.withAlphaComponent(0.5).cgColor)
        context.addPath(path)
        context.fillPath()

        // Text.
        let attributes: [NSAttributedString.Key: Any] = [
            kCTFontAttributeName as NSAttributedString.Key: ctFont,
            kCTForegroundColorAttributeName as NSAttributedString.Key: NSColor.white.cgColor,
        ]
        let attrString = NSAttributedString(string: overlayText, attributes: attributes)
        let line = CTLineCreateWithAttributedString(attrString)

        var ascent: CGFloat = 0
        var descent: CGFloat = 0
        var leading: CGFloat = 0
        _ = CTLineGetTypographicBounds(line, &ascent, &descent, &leading)

        let textX = rect.minX + paddingH
        let baselineY = rect.minY + (rect.height - (ascent + descent)) / 2 + descent

        context.saveGState()
        context.textMatrix = .identity
        context.textPosition = CGPoint(x: textX, y: baselineY)
        CTLineDraw(line, context)
        context.restoreGState()
    }

    // MARK: - Badge geometry helpers

    /// Font size scaled proportionally to image height, clamped to a sensible range.
    private static func badgeFontSize(for imageHeight: CGFloat) -> CGFloat {
        max(12.0, min(28.0, imageHeight / 50.0))
    }

    private static func makeBadgeFont(size: CGFloat) -> CTFont {
        CTFontCreateUIFontForLanguage(.system, size, nil)
            ?? CTFontCreateWithName("Helvetica" as CFString, size, nil)
    }

    private static func measureBadgeText(font: CTFont) -> CGSize {
        let attrString = NSAttributedString(string: overlayText, attributes: [
            kCTFontAttributeName as NSAttributedString.Key: font,
        ])
        let line = CTLineCreateWithAttributedString(attrString)
        var ascent: CGFloat = 0
        var descent: CGFloat = 0
        var leading: CGFloat = 0
        let width = CGFloat(CTLineGetTypographicBounds(line, &ascent, &descent, &leading))
        return CGSize(width: ceil(width), height: ceil(ascent + descent))
    }

    private static func badgePillSize(
        textSize: CGSize,
        fontSize: CGFloat
    ) -> (width: CGFloat, height: CGFloat, paddingH: CGFloat, margin: CGFloat) {
        let paddingH = fontSize * 0.7
        let paddingV = fontSize * 0.45
        let margin = fontSize
        return (textSize.width + paddingH * 2, textSize.height + paddingV * 2, paddingH, margin)
    }

    private static func webcamOverlayFrame(
        renderSize: CGSize,
        webcamSize: CGSize,
        preset: WebcamOverlaySizePreset,
        corner: WebcamOverlayCorner
    ) -> CGRect {
        let minDimension = min(renderSize.width, renderSize.height)
        let targetWidth = minDimension * webcamScale(for: preset)
        let aspectRatio = webcamSize.height / max(webcamSize.width, 1)
        let maxHeight = renderSize.height * 0.45
        let width = min(targetWidth, renderSize.width * 0.45)
        let height = min(width * aspectRatio, maxHeight)
        let margin = max(16, minDimension * 0.03)

        let x: CGFloat
        let y: CGFloat
        switch corner {
        case .topLeft:
            x = margin
            y = margin
        case .topRight:
            x = renderSize.width - width - margin
            y = margin
        case .bottomLeft:
            x = margin
            y = renderSize.height - height - margin
        case .bottomRight:
            x = renderSize.width - width - margin
            y = renderSize.height - height - margin
        }

        return CGRect(x: x, y: y, width: width, height: height)
    }

    private static func webcamScale(for preset: WebcamOverlaySizePreset) -> CGFloat {
        switch preset {
        case .small: return 0.18
        case .medium: return 0.24
        case .large: return 0.30
        }
    }

    private static func webcamMaskLayer(
        shape: WebcamOverlayShape,
        bounds: CGRect,
        cornerRadiusOverride: CGFloat?
    ) -> CALayer? {
        switch shape {
        case .rectangle:
            return nil
        case .circle:
            let mask = CAShapeLayer()
            mask.frame = bounds
            mask.path = CGPath(ellipseIn: bounds, transform: nil)
            return mask
        case .rounded:
            let mask = CAShapeLayer()
            mask.frame = bounds
            let radius = cornerRadiusOverride ?? (min(bounds.width, bounds.height) * 0.12)
            mask.path = CGPath(
                roundedRect: bounds,
                cornerWidth: max(0, min(radius, min(bounds.width, bounds.height) / 2)),
                cornerHeight: max(0, min(radius, min(bounds.width, bounds.height) / 2)),
                transform: nil
            )
            return mask
        }
    }

    private static func orientedSize(
        for naturalSize: CGSize,
        preferredTransform: CGAffineTransform
    ) -> CGSize {
        let rect = CGRect(origin: .zero, size: naturalSize).applying(preferredTransform)
        return CGSize(width: abs(rect.width), height: abs(rect.height))
    }

    private static func normalizedTransform(
        for transform: CGAffineTransform,
        naturalSize: CGSize
    ) -> CGAffineTransform {
        let transformedRect = CGRect(origin: .zero, size: naturalSize).applying(transform)
        return transform.translatedBy(x: -transformedRect.minX, y: -transformedRect.minY)
    }
}
