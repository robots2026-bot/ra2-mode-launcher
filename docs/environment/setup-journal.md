# Speed controller build journal

## 2026-10-09 — native cnc-ddraw live control

- Location: Windows, `C:\Users\FamilyWang\code\ra2-mode-launcher`.
- Scope: workspace source, tests and build artifacts only. The user explicitly prohibited approval requests while asleep. No elevated commands, installs or new game-directory writes were requested in this continuation.
- Baseline: deployed multiplayer launcher supports host-selected launch presets and disables live changes; single-player hotkeys still change original engine presets. This does **not** complete unified cnc-ddraw live pacing.
- Intended upstream: FunkyFr3sh/cnc-ddraw commit `39f14721c998c937118615c1c4eb02ce20a4e638`, matching the previously inspected runtime build.
- Installed compiler discovery: Visual Studio Community 2026 `18.9.12112.369`; MSVC directory `14.51.36231`. Presence alone does not prove a usable Windows SDK or native build.
- `git clone --no-checkout https://github.com/FunkyFr3sh/cnc-ddraw.git vendor/cnc-ddraw-speed` failed (exit 128 reported through shell exit 1): `getaddrinfo() thread failed to start`.
- `Invoke-WebRequest https://codeload.github.com/FunkyFr3sh/cnc-ddraw/zip/39f1472` failed: host could not be resolved. The PowerShell process later returned 0 because a following discovery command succeeded; the download itself did not succeed.
- These error summaries are reconstructed from tool results, not fabricated raw build logs. Network resolution under current permissions is unverified outside those failed commands; do not install a proxy or change system networking.
- Web reference inspection works and confirmed the upstream GUI-thread limiter, `fake_WndProc`, and `SPEEDLIMITER` definitions. It is not a complete source checkout or compiled artifact.

## Completion gates

1. Engine fixed to fastest for unified single-player pacing; target 1–1000 or unlimited, with explicit input and hotkeys.
2. A source-built cnc-ddraw control path applies changes in its own GUI thread and acknowledges the actual rate. Stock DLLs must fail the handshake clearly.
3. Multiplayer host-selected settings, all-peer validation and in-game lock remain effective; no unilateral live multiplayer changes.
4. HUD distinguishes target, measured game advancement, render FPS and display refresh; unavailable measurements stay explicit.
5. Native build, actual limiter timing, real game operation and deployment must each be verified separately. Managed mock tests prove none of these by themselves.
6. Existing clicker, queue capacity, normal close and room-slot behavior must remain intact.

Initial status above is superseded by the verified continuation below. Native implementation, managed integration and publishing are complete; actual gameplay and game-directory deployment remain unverified.

## Verified continuation

- Acquired 102 upstream files through the GitHub read connector; verified every Git blob SHA1 against pinned commit 39f14721c998c937118615c1c4eb02ce20a4e638. The manifest regenerates the ignored vendor tree before patching.
- Built x86 DLL with MSVC 14.51, v145, SDK 10.0.26100.0. Initial LNK1101 (MSPDB140 DLL mismatch) resolved by disabling debug information and whole-program optimization, without changing installed tools.
- Native fixture exercised the actual DirectDraw limiter and acknowledged window-message endpoint: 1, 30, 37, 60, 120, 1000 and unlimited; invalid cookie/rate and multiplayer/opt-out requests rejected. Under concurrent compiler load the 120 target measured 111.39 and 1000 measured 971.75; targets are ceilings, not guaranteed advancement rates. Managed-launch hidden 60 flip cap removed and tested.
- Managed smoke checks cover custom target conversion, native acknowledgement, component hash, launch configuration, room custom-speed restore, production clicker regressions. Protocol bumped to 5.
- HUD scope follows the latest user request: translucent speed / game FPS / display refresh only. No CPU, memory, elapsed time or placeholder sampling.
- Prepared backup-and-rollback update script. No game-directory deployment or real gameplay verification performed in this continuation. Native fixture timing does not establish RA2 simulation speed or real two-PC compatibility.

- Final Release publish passed. Update script tested against a workspace fixture: replacement hashes and original DLL backup verified. LAN protocol smoke blocked by SocketException 10013 at loopback connect; no elevated retry attempted.

## Deployment and publication — 2026-10-09

- User explicitly authorized deployment, cleanup, commit/push and continued publication to the existing public repository.
- Installed to D:\Software\RA2Mode with rollback backup RA2ModeLauncher\backups\speed-20261009-121706-622. Runtime and packaged ddraw.dll SHA256 B406554B812575DFC9D4FE7315403B8F21E2263063EE95FB7C6F897E11FFB1D5; installed launcher matches published SHA256 975E93A20D165DA1FE919B473F0AB2928BB543AC63F848FDD98A6A7429CCA9D2.
- Archived nine older launcher backup directories into previous-launcher-backups.zip and consolidated five old ddraw configuration snapshots under backups\legacy-config. Resources and saves preserved. Workspace installation/timing fixtures and obsolete diagnostic snapshot removed.
- After authorization, LAN protocol smoke rerun outside the restrictive sandbox passed all 23 checks, including protocol 5 handshake, old version rejection, speed preparation reset and multiplayer lock. No real game was started.
- Added pinned upstream path/blob manifest and download script for rebuilding on a clean checkout; third-party source and binaries excluded from Git. Download script parses correctly; current source was acquired using the GitHub connector, so the new archive-download network route has not been exercised here.
