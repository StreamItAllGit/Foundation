# Contributing to Foundation

Thank you for contributing to **Foundation** (`Beta`). All active `0.x` releases are pre-1.0 Beta builds under the [MIT License](LICENSE).

---

## Clean-Room & Security Ground Rules

1. **Zero Proprietary Assets or Secrets:** Never commit copyrighted assets, BIOS dumps, private keys, API tokens, webhook URLs, or personal workstation file paths (`C:\Users\...`).
2. **Confidential Vulnerability Reports:** Do not open public issues for security vulnerabilities. Follow [SECURITY.md](SECURITY.md).
3. **Zero-Warning Policy:** C# changes must compile cleanly with `-p:TreatWarningsAsErrors=true`.
4. **Isolated Tests Only:** Automated tests must execute strictly in temporary directories (`APP_ISOLATED_TEST=1` / `--data-dir`) and never mutate live user installations or host Windows Registry hives.

---

## Development Setup & Verification

### Prerequisites
- **.NET 8 SDK** (`net8.0` cross-platform libraries + `net8.0-windows` WinForms/Installer/Automation targets)
- **Python 3.12+** (`unittest` works with stdlib only; `pytest` optional)
- **Node.js 20+** (when modifying TypeScript/React components in `Dev/src/Foundation.UI/Web`)

### Running Tests Locally

```powershell
# C# unit and integration tests (zero warnings required)
$env:FOUNDATION_SUPPRESS_MODALS = "1"
dotnet test Dev/Foundation.sln -c Release -p:TreatWarningsAsErrors=true

# Python foundation_core tests
python -m unittest discover -s Dev/tests/foundation_core -v
```

---

## Code & Documentation Guidelines

- Keep modules focused and cohesive (avoid monolithic files; aim for `<= 250`–`400` code lines per file).
- Preserve deterministic, clean-room behavior across C# (`Foundation.Core`) and Python (`foundation_core`) parity modules.
- Update relevant technical documentation in [Dev/docs/](Dev/docs/ARCHITECTURE.md) and root release notes ([CHANGELOG.md](CHANGELOG.md), [FEATURES.md](FEATURES.md)) when adding or changing public APIs.

---

## Pull Request Checklist

- [ ] Code compiles with zero warnings under `dotnet test Dev/Foundation.sln -c Release -p:TreatWarningsAsErrors=true`
- [ ] All `.NET` and Python `foundation_core` tests pass locally
- [ ] Zero hardcoded local user paths, tokens, or credentials introduced
- [ ] Relevant XML/doc comments and `Dev/docs/` API reference entries are updated
