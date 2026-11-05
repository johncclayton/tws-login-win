# Send credentials only to a TWS signed by Interactive Brokers

The tool used to find TWS by process name (`tws*.exe`) and window title (`Login`), and loaded the Java Access
Bridge DLL from whichever process owned the first window it read. Any program running as the user could name
itself `tws.exe`, show a look-alike Login form, and receive the user name, the password and a valid one-time code,
fetched from 1Password without the user being asked. Once the tool is public, that recipe is public too.

So a TWS process counts only when its executable has a valid Authenticode signature (WinVerifyTrust, chain to a
trusted root) whose signer certificate names the organisation `Interactive Brokers Group, Inc.` exactly. Other
processes' windows are filtered out of the desktop before anything reads them, so no part of the login, `inspect`,
`watch` or `login-windows` ever sees them. The bridge DLL is loaded from the trusted TWS's `jre\bin` only when it is
validly signed too, by anyone: TWS's Java runtime is signed by its vendor (Azul Systems as of TWS 1051), not IBKR.
Revocation is checked from the local cache only.

The executable judged is the one Windows says the process runs from (`QueryFullProcessImageName`, which follows a
rename), cross-checked against the file mapped at the image base, and held open while it is judged. Judging the
path the process reports about itself, or opening the file twice, would let a process rename itself and leave a
signed copy behind.

## Considered

- **An install folder** (`C:\Jts` or configured): no signature handling, but any program that can write there, or
  a TWS installed elsewhere, defeats or breaks it.
- **Pinning the certificate thumbprint**: stronger, but breaks every time IBKR renews its certificate.
- **Requiring Azul for the DLL**: breaks the day IBKR changes Java vendor, for no gain once the TWS is trusted.
- **Online revocation checks**: a slow or absent network would stop logins.

## Consequences

- An IBKR certificate under another organisation name, or an unsigned TWS build, is refused until the name is
  changed here; `--trust-unsigned` is the way out meanwhile, logged as a warning.
- Each TWS process is judged once per process start (id and start time), so a reused process id is judged again.
- It does not defend against code already running as the user that goes further than a look-alike window: changing
  the real TWS's files (C:\Jts is user-writable), answering through the Java Access Bridge, patching memory, or
  reading the 1Password token or the ini file directly. These are listed in docs/security-model.md.
