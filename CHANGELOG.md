# Changelog

## 0.1.0 (2026-11-05)

First public version, extracted from a private automation repository.

- `login`, `check-creds`, `inspect`, `login-windows`, `watch`; credentials from 1Password (with the one-time
  code) or an IBC-style ini file.
- **Only a TWS signed by Interactive Brokers gets credentials.** Windows of other processes are ignored, and the Java
  Access Bridge DLL must be validly signed. `--trust-unsigned` turns this off. See
  [ADR 0002](docs/adr/0002-trust-only-ibkr-signed-tws.md). The executable is the one Windows says the process
  runs from, held open while it is judged.
- `op` is looked for on `PATH` only (not the current or the tool's folder) and must be signed by AgileBits.
- The login's user name is replaced by `[user]` in every log line; fields that may hold typed text show their
  names by length only.
- An unexpected error ends as `ABORT: unexpected …` (exit code 1) instead of a crash with a stack trace.
- `IbPassword` in an ini file is taken exactly as written after `=`; it used to be trimmed, which broke passwords
  starting or ending with a space.
- `login-windows` writes its log to stderr; stdout holds only the `<pid> <screen>` lines.
- `--version` and `--help`; running with no command prints the usage (it used to run `inspect`).
- Released as two single-file `win-x64` exes, with `SHA256SUMS` and build provenance: `tws-login-win.exe`
  (self-contained) and `tws-login-win-net10.exe` (needs the .NET 10 runtime).
