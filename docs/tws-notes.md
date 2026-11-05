# What TWS shows through the Java Access Bridge

Seen on TWS 1051 (English) through the Java Access Bridge, October 2026. Useful when TWS changes and the tool
needs to follow: `tws-login-win watch` prints the same view of any screen.

- **Windows.** The Login window is a `SunAwtFrame` titled `Login` (TWS also keeps an invisible second one); it is
  retitled "Second Factor Authentication" for the second-factor screens and back to "Login" while TWS loads.
  Dialogs are `SunAwtDialog`. UI Automation sees no controls; the Java Access Bridge sees all of them.
- **Login form.** Tabs are toggle buttons "Live Trading" / "Paper Trading" (`checked` when selected); the user name
  is an editable single-line `text`, the password a `password text` below it; the banner is a label
  " SIMULATED TRADING …"; Log In is a push button "Log In" / "Paper Log In", without the `enabled` state until both
  fields hold text. Hidden parts (a "Demo Type" combo box, an on-screen keyboard) report no bounds and are ignored.
- **The form moves** by about 10 px between screens and runs, unpredictably; only the bridge's live positions
  follow it.
- **Field text.** The bridge reports a password field's length (an empty one reports none) but its text is never
  read. Other fields' text can be read.
- **Code screen:** label "Enter Mobile Authenticator app code", the code box (the only editable field, focused),
  buttons OK, Cancel and "Enter Read Only". **Device choice:** a list "IB Key" / "Mobile Authenticator app", the
  current one `selected`; buttons OK, Cancel, Help. Cancel there returns to the Login form with the password removed.
- **A wrong code** shows "Login Error": "Login failed - incorrect security code" [OK], and the Login window goes back
  to the Login form with the password removed.
- **"Existing session detected":** "Another session with the same user name already exists. Would you like to login
  and disconnect the other session?" [Continue Login] (focused) [Cancel].
- **"Re-login is required":** "Your connection was lost. Would you like to re-login?" [Re-login] [Cancel]; seen
  right after Continue Login while the account was logged in on a phone.
- **Verified with the tool:** a paper and a live login, each the first time, on two machines with different
  display scaling, 2026-10-28: paper warning accepted, one-time code set, read back and accepted.
- **Not yet seen through the tool:** what follows Re-login. Should it be the Login form, the tool aborts rather than
  fill it again.
- **Beeps.** Delete or Backspace in an empty field beeps; setting text through the bridge never sends either.
- **Timing.** A paper Log In shows the main window after about 9-12 s and the paper warning about 1 s after it; a
  live one about 20 s, second factor included.
