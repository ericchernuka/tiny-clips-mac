cask "tiny-clips" do
  auto_updates true
  version "1.4.0.4"
  sha256 "0d5452b9f59464cf3c4eb284017c931bccd504aa2823c3f65d31f7a5c444da07"

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
