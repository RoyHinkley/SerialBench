# SerialPortTerminal project notes

> Temporary working handoff. Keep only context needed to continue development and decisions/deferred work that must not be forgotten. Do not use this as project history. Delete it when the application is release-ready and move any remaining work to GitHub issues.

## Purpose and architecture

SerialPortTerminal is both an interactive serial diagnostic/investigation tool and a proving ground for reusable lower-level serial communications machinery. Diagnostic evidence is product functionality: preserve exact TX/RX bytes, CRC/framing evidence, timing, connection events, pin changes, buffer problems, counters, recovery, and related diagnostics rather than hiding them behind application-level abstractions.

Keep the serial engine independent of Avalonia and application policy:

```text
SerialDevice
    transport / receive acquisition / framing / CRC
    pacing / recovery / low-level diagnostics
        |
        +--> TerminalSession --> SerialPortTerminal UI
        |
        +--> reusable by eventual applications/controllers
```

Adapt useful AeonHacs serial machinery rather than depending on AeonHacs.Core. Do not import broad Hacs lifecycle/state/global infrastructure or the old catch-all `Utility` class. Preserve field-proven receive/framing behavior until understood and characterized by tests.

`TerminalConfiguration` is the authoritative live configuration. The UI binds to it; serial sessions, diagnostics, and persistence consume it rather than interrogating controls. Settings/configuration remain portable beside the application.

## Transport lifecycle and recovery — settled

Keep three concepts independent:

1. **Connection intent** — whether the caller/user wants the configured endpoint connected.
2. **Transport lifecycle** — whether an actual OS serial transport is connecting, connected, unavailable, disconnecting, or disposed.
3. **Communications health** — CRC failures, malformed/incomplete responses, timeouts, garbled data, buffer anomalies, isolated read/write failures, etc.

Bad communications do **not** imply loss of transport. Preserve/count/report them and continue whenever useful communication may still be possible.

Core ownership invariant:

> **Connection intent persists; ownership of a failed transport does not.**

`ConnectionRequested` represents intent. `SerialConnectionState` represents transport lifecycle. `Ready` requires `Connected` plus an open owned transport. Recovery is an activity while unavailable, not a lifecycle state.

When transport unavailability is established, stop/join workers, unsubscribe events, close/dispose the old transport, and release it while retaining intent/configuration. Every recovery attempt creates a fresh transport; failed partial acquisition is fully released before another attempt. Automatic recovery is not `Reset()` and must not increment `Resets`.

`SerialRecoveryPolicy` controls retry interval, maximum attempts, and maximum duration. Repeated identical recovery failures are condensed diagnostically without slowing retries; materially different failures restart detailed reporting. Successful recovery is a prominent baseline diagnostic event.

Explicit `Disconnect()` cancels intent/recovery. `Disconnect()` is harmless when repeated. `Dispose()` is idempotent; operational calls after disposal intentionally throw `ObjectDisposedException`. Transport cleanup is best-effort so one cleanup exception does not prevent later cleanup steps.

The transport seam is intentional:

```text
SerialDevice
     |
     v
ISerialTransport
     |
     +-- SerialPortTransport --> System.IO.Ports.SerialPort
     +-- FakeSerialTransport --> deterministic lifecycle/fault tests
```

`ISerialTransportFactory` supplies independently owned instances for deterministic fault injection and fresh-instance reacquisition tests.

Lifecycle/recovery has passed the deterministic test suite and real Windows USB testing: COM3 was unplugged while connected, detected unavailable, released, retried while absent, reacquired automatically after reinsertion, and exchanged valid traffic afterward without user reconnect. This checkpoint is complete.

## CRC and framing — do not casually simplify

Aeon field framing is:

```text
<message><two-byte CRC><termchar>
```

For Aeon protocols the term character is ETX (`0x03`). CRC excludes ETX; CRC bytes precede ETX, low byte then high byte for standard Aeon settings. ETX is illegal in payload data, but either CRC byte may legitimately equal ETX. Therefore an ETX encountered after payload data cannot immediately be classified as the terminator; the delayed classification in `ProcessRx` is deliberate protocol behavior.

The term-character parser is characterized by tests covering ordinary valid messages, either CRC byte equal to ETX, arbitrary fragmentation, back-to-back responses, realistic multiline payloads, invalid payload ETX recovery, and CRC failure followed by a valid message. Do not rewrite `ProcessRx` without preserving these cases.

`ReceivedData` is authoritative byte-oriented frame evidence; presentation must not reconstruct discarded information. CRC acceptance and CRC presentation are separate concerns. The selectable no-term-character mode is silence-framed; truly unframed streaming mode does not yet exist.

Compatible live protocol changes are staged using immutable `SerialProtocolSettings` and adopted only at a receive-message boundary. Changes that switch receive strategy require reconnect. Transmit must not force pending RX settings into effect mid-frame.

Deferred protocol/concurrency scrutiny:

- improve deterministic synchronization in protocol-staging tests and review memory visibility of active/pending snapshots;
- eventually construct each TX codeword from one captured immutable protocol snapshot rather than shared mutable CRC state;
- resolve ring-buffer full/empty ambiguity and exact usable capacity around the 4096-byte buffer/read size.

## Diagnostics, logging, and UI semantics

Communications shows the logical conversation; Transport Diagnostics shows evidence of how it was transported. The disk log is the authoritative ordered diagnostic record; the UI is a live view/tail of that stream.

Automatic session logging is independent of diagnostic detail. Baseline connection state, errors, commands, responses, recovery, etc. remain logged even when detailed transport events are disabled. Signal logging is separately selectable. One automatic log spans the application/investigation run; retention applies only to automatic-name matches and must never silently delete unrelated/user-renamed files.

Send/Received presentation rules:

- use **Send** and **Received**, not command/response assumptions;
- always show unsolicited received traffic;
- display choices never alter authoritative received data;
- Text mode is escaped-safe; Bytes mode is byte-oriented;
- recognized framed messages begin distinct display entries independent of payload CR/LF;
- do not yank a deliberately scrolled-up view back to the latest entry.

The current `TextBox.Text +=` live views are O(N²). Replace them with append-friendly bounded presentation while retaining the complete disk log.

The permanent left configuration pane is provisional. A generously sized flyout/drawer is favored later so configuration can overlay the workspace rather than shrinking Communications/Transport Diagnostics.

## Serial signals / RTS

RTS, CTS, DTR, DSR, DCD, and RI are read-only status indicators. Connection configuration owns RTS behavior; do not add a second manual RTS control in the signal bank. Supported modes are Disabled, Enabled, and Toggle. Continuously asserted RTS must remain supported because some field devices use it as a parasitic power source.

Deferred: verify `RTS_CONTROL_TOGGLE` with current .NET/Windows and representative adapters. Prefer a supported Win32 route if needed; do not restore reflection into private `SerialPort` internals.

## Legacy replacement decisions still open

The old WinForms implementation remains under tag `legacy-winforms`. Before declaring it fully superseded, deliberately decide rather than accidentally omit:

- `Reset()` semantics: legacy Reset cleared counters/display and recreated the serial device;
- whether to restore compact command/response/bytes-read counters;
- whether standalone calculated TX/RX CRC displays still add value;
- whether to restore a separate user-controlled normalized data-recording file distinct from transport diagnostics;
- whether Escape should clear the command editor/history position.

Physical connection settings remain locked while connected; compatible protocol/timing settings may apply live and receive-strategy changes require reconnect.

## Deployment and identity

The tracked Windows x64 self-contained publish profile is `Properties/PublishProfiles/FolderProfile.pubxml`; equivalent command:

```text
dotnet publish SerialPortTerminal.csproj -p:PublishProfile=FolderProfile
```

Portable deployment is preferred for the immediate replacement milestone. Validate the package on a clean Windows machine before calling it a release/installer. Installing under Program Files would require reconsidering beside-the-executable writable settings/logs.

The lifecycle/recovery refactor is now complete, so the deliberately deferred application/project naming discussion can proceed. If renaming, perform the identity change coherently rather than piecemeal and remove spaces from coding filenames (notably `Serial Port Terminal.sln`).

## Near-term work

1. Reconsider the application/project name and, if changing it, perform one coherent identity/filename cleanup.
2. Decide the remaining legacy replacement items above.
3. Verify RTS Toggle if a target field setup requires it.
4. Improve usability: scalable/bounded live views, follow-tail behavior, configuration flyout/profiles, help/tooltips, and related conveniences.
5. Validate the self-contained portable package on a clean Windows machine.
6. As release readiness approaches, move remaining deferred work to GitHub issues and delete this file.

## Development rules

- Work directly on `main` unless explicitly directed otherwise; use small coherent commits.
- Re-fetch current files immediately before writing; do not edit from stale repository snapshots.
- Keep serial/application behavior testable without Avalonia.
- Preserve characterized behavior while modernizing surrounding structure.
- Correctness and resource ownership take priority over convenience or cosmetic cleanup.
- Keep this file concise: current architecture constraints, sharp edges, and deferred decisions only; completed implementation history belongs in Git, not here.
