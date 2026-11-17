# Security Policy

## Supported Versions

| Version | Supported | Security Boundary Notes |
| :--- | :--- | :--- |
| `0.5.x` | Yes | Active mainline (`Foundation.Core`, `Foundation.Automation`, `Foundation.Installer`, `Foundation.Server`, `foundation_core`) |
| `0.4.x` | Security fixes only | Prior minor release |
| `< 0.4.0` | No | Upgrade to `v0.5.0+` |

## Built-In Security & Containment Invariants

1. **DPAPI Secret Storage (`Foundation.Core.DpapiSecretStore`):**
   Secrets are sealed at rest using Windows DPAPI (`CurrentUser` scope) and are never persisted as plaintext inside settings, manifests, or logs.
2. **Loopback Automation Isolation (`Foundation.Automation.AutomationLoopbackServer`):**
   - Disabled by default (`--automation-port` is strictly opt-in).
   - Binds exclusively to `127.0.0.1`.
   - Rejects missing or non-loopback `Host` headers (`127.0.0.1` / `localhost` only) and external `Origin` headers with `403 Forbidden`.
   - Requires constant-time `Authorization: Bearer <token>` authentication on every route, returning `401 Unauthorized` otherwise.
   - Dispatches virtual pointer/keyboard actions via window-scoped Win32 messages without stealing host foreground focus (`0` `SendInput` calls).
3. **4-Drive Sandbox Containment (`Foundation.Core.SandboxEnvironmentSeam`):**
   When `FOUNDATION_SANDBOX_ROOT` is active (and `APP_ISOLATED_TEST != 1`), path escapes toward host OS, user profile, or production directories fail closed with exit code `3`.
4. **Network & Archive Safety (`SafeNetworkTransfer`, `InstallerEngine`, `MediaContainerExtractor`):**
   Enforces `AssertPathContained` against zip-slip traversal, 2 GB bounded extraction caps, SHA-256 checksum verification, and HTTP `4xx` non-retry rules.

## Reporting a Vulnerability

Please **do not** open a public GitHub issue for suspected security vulnerabilities.

1. Report privately via GitHub **Security Advisories** (`Report a vulnerability` on the repository Security tab) or direct private maintainer contact.
2. Include the affected assembly/module (`Foundation.*` or `foundation_core`), version (`v0.5.x`), reproduction steps, and expected vs. actual behavior.
3. Maintainers acknowledge reports within **72 hours** and target coordinated remediation before public disclosure.
