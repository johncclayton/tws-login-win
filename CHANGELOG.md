# Changelog

## 0.1.0 (unreleased)

First public version, extracted from a private automation repository.

- `login`, `check-creds`, `inspect`, `login-windows`, `watch`; credentials from 1Password (with the one-time
  code) or an IBC-style ini file.
- **Only a TWS signed by Interactive Brokers gets credentials.** Windows of other processes are ignored, and the Java
  Access Bridge DLL must be validly signed. `--trust-unsigned` turns this off. See
  [ADR 0002](docs/adr/0002-trust-only-ibkr-signed-tws.md).
- `IbPassword` in an ini file is taken exactly as written after `=`; it used to be trimmed, which broke passwords
  starting or ending with a space.
- `login-windows` writes its log to stderr; stdout holds only the `<pid> <screen>` lines.
- `--version` and `--help`; running with no command prints the usage (it used to run `inspect`).
- Released as one self-contained `win-x64` exe with `SHA256SUMS`.
