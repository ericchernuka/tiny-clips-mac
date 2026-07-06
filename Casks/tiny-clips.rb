cask "tiny-clips" do
  auto_updates true
  version "1.5.2"
  sha256 "bbd777eb2a9ad38d07fc031c499172d5527eb3d8bd4a19d64d413c7dae64f578"

  url "https://github.com/jamesmontemagno/tiny-clips/releases/download/v#{version}-mac/TinyClips-v#{version}-mac.zip"
  name "TinyClips"
  desc "Menu bar app for screenshot, video, and GIF capture"
  homepage "https://github.com/jamesmontemagno/tiny-clips"

  app "TinyClips.app"

  postflight do
    system "xattr", "-dr", "com.apple.quarantine", "#{appdir}/TinyClips.app"
  end

  zap trash: [
    "~/Library/Preferences/com.tinyclips.app.plist",
  ]
end
