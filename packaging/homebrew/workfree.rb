class Workfree < Formula
  desc "Find git worktrees, see how much disk they use, and clean them up"
  homepage "https://github.com/flowdevs-io/WorkFree"
  version "VERSION"
  license "MIT"

  on_macos do
    on_arm do
      url "https://github.com/flowdevs-io/WorkFree/releases/download/vVERSION/workfree-osx-arm64.tar.gz"
      sha256 "SHA256_OSX_ARM64"
    end
    on_intel do
      url "https://github.com/flowdevs-io/WorkFree/releases/download/vVERSION/workfree-osx-x64.tar.gz"
      sha256 "SHA256_OSX_X64"
    end
  end

  on_linux do
    on_arm do
      url "https://github.com/flowdevs-io/WorkFree/releases/download/vVERSION/workfree-linux-arm64.tar.gz"
      sha256 "SHA256_LINUX_ARM64"
    end
    on_intel do
      url "https://github.com/flowdevs-io/WorkFree/releases/download/vVERSION/workfree-linux-x64.tar.gz"
      sha256 "SHA256_LINUX_X64"
    end
  end

  def install
    # The desktop app loads native libraries from its own folder, so keep everything together.
    libexec.install Dir["*"]
    (bin/"workfree").write_env_script libexec/"workfree", {}
    (bin/"workfree-gui").write_env_script libexec/"workfree-gui", {}
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/workfree --version")
  end
end
