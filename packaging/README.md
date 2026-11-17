# Publishing WorkFree

All three package managers install the binaries attached to a GitHub release, so the order is:

1. Tag and push: `git tag v0.1.0 && git push origin v0.1.0`. The Release workflow builds every platform and attaches the archives plus `checksums.txt`.
2. Copy the version and the SHA-256 values from `checksums.txt` into the templates in this folder (look for `VERSION` and `SHA256_...`).
3. Submit each one as described below.

Each release archive contains both programs: `workfree` (command line and terminal screen) and `workfree-gui` (desktop window), plus the native libraries the desktop app loads from its own folder. Keep them together when repackaging.

## winget (Windows Package Manager)

Templates: `winget/`. Package identifier `FlowDevs.WorkFree`, portable zip install, commands `workfree` and `workfree-gui`.

The simplest route is `wingetcreate new <url-of-workfree-win-x64.zip>`, which generates these files and opens the pull request against `microsoft/winget-pkgs` for you. For later versions: `wingetcreate update FlowDevs.WorkFree --version X.Y.Z --urls <url> --submit`. Microsoft reviews the first submission by hand.

## Chocolatey

Templates: `chocolatey/`. Needs a chocolatey.org account and API key.

```
cd packaging/chocolatey
choco pack
choco push workfree.VERSION.nupkg --source https://push.chocolatey.org/ --api-key <key>
```

New packages go through Chocolatey's moderation queue before they are listed.

## Homebrew

Template: `homebrew/workfree.rb`. Homebrew core does not take brand-new projects, so publish through a tap: create the public repo `flowdevs-io/homebrew-tap`, put the formula at `Formula/workfree.rb`, and users install with:

```
brew install flowdevs-io/tap/workfree
```

The macOS binaries are not code-signed or notarized. Homebrew installs do not get the quarantine flag, so they run; a binary downloaded by hand in a browser will be blocked by Gatekeeper until it is signed with an Apple Developer ID.
