# Drive TWS through the Java Access Bridge, not the screen

tws-login-win first read TWS's screen: OCR for labels, pixel counts for field contents, positions measured on one
1500x867 capture and scaled to the window, and real mouse and keyboard input. That proved tied to one desktop: TWS
lays its controls out in pixels that depend on display scaling, not window size; the form shifted by about 10 px
between screens and runs; OCR could not read the typed code; and real input needed TWS in front, which Windows
sometimes refused. On 2026-10-28 the Java Access Bridge that ships with TWS's Java runtime exposed every control the
login needs, by role and name, with state, text length and live screen bounds, and accepted presses and text sets
on the controls themselves (Log In, Continue Login, the user name field). So the tool now uses only the bridge, and
all OCR, pixel reading, fixed positions and synthetic input were removed.

## Consequences

- Each machine needs `jabswitch -enable` once per Windows user, and TWS restarted after it. Without it the tool
  stops with that instruction; there is deliberately no screen-reading fallback, to keep one way of doing things.
- The bridge loads into TWS's JVM: an accessibility setting for every Java app that user starts.
- Field text becomes readable (the user name and the code are read back exactly); it must never be logged. The
  password field reports only its length.
