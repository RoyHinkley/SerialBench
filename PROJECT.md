# SerialPortTerminal project notes

> Temporary live coordination document. Capture unresolved decisions and deferred ideas so they are not lost accidentally. Delete this file once mature documentation and GitHub issues can carry the remaining work.

## Purpose

SerialPortTerminal is both an interactive serial diagnostic/investigation tool and a proving ground for lower-level serial communications machinery used by applications.

The reusable serial layer must preserve evidence an ordinary application might hide: exact TX/RX bytes, CRC bytes and failures, framing behavior, timing, connection/disconnection events, pin changes, buffer problems, counters, and related diagnostics. Diagnostic capability is product functionality, not merely developer tracing.

The immediate practical motivation is troubleshooting a Eurotherm controller. The previous SPT lacked binary byte logging and CRC-byte visibility present in newer AeonHacs code.

The project is approaching a conceptual break from its WinForms predecessor. Once the current `SerialDevice` lifecycle/recovery refactor is complete, reconsider the application/project name deliberately. Until then, defer naming changes. The eventual cleanup should also favor coding filenames without spaces (for example `SerialPortTerminal.sln`). Do not perform this rename piecemeal during lifecycle work.

## Architecture direction

Keep the serial engine independent of Avalonia and application policy.

```text
SerialDevice
    transport / receive acquisition / framing / CRC
    pacing / recovery / low-level diagnostics
        |
        +--> TerminalSession --> SerialPortTerminal UI
        |
        +--> reusable by eventual applications/controllers
```

Adapt selected current AeonHacs serial machinery rather than depending on AeonHacs.Core. Do not import broad Hacs lifecycle/state/global infrastructure or the old catch-all `Utility` class.

Preserve field-proven receive/framing algorithms until understood and characterized by tests. Modernize their surroundings first. Functional changes to delicate protocol logic should be explicit rather than incidental cleanup.

### Transport lifecycle, connection intent, and communications health

Keep three concepts independent:

1. **Connection intent** — whether the caller/user wants the configured endpoint to remain connected.
2. **Transport lifecycle** — whether an actual OS serial transport is connecting, available, unavailable, disconnecting, or disposed.
3. **Communications health** — CRC failures, malformed/incomplete responses, framing errors, timeouts, garbled data, buffer anomalies, read/write failures, and related evidence while attempting communication.

Bad communications do **not** imply loss of transport. CRC failures, malformed messages, unexpected bytes, incomplete responses, timeouts, buffer anomalies, and isolated I/O exceptions must not automatically disconnect the device. Preserve/count/timestamp/report such evidence and continue whenever useful communication may still be possible.

`SerialConnectionState` represents transport availability, not communications quality. Its states are `Disconnected`, `Connecting`, `Connected`, `Disconnecting`, `Unavailable`, and `Disposed`. Recovery is an activity while `Unavailable`, not a peer lifecycle state. `ConnectionRequested` exposes connection intent; recovery activity/counters/timestamps should be exposed independently as they are implemented.

The core ownership invariant is:

> **Connection intent persists; ownership of a failed transport does not.**

When sufficient evidence establishes that a transport is unavailable, quiesce its workers, unsubscribe events, close/dispose the transport, and release every OS resource associated with that transport instance. Keep connection intent and configuration. Recovery attempts must create fresh transport instances. Any attempt that partially opens and then fails initialization must close/dispose that temporary instance before another attempt. While connection is requested and state is `Unavailable`, the implementation should normally own no open serial-port handle.

USB hot disconnect/reconnect is a normal field condition. If the user requested COMx remain connected and the device disappears, retain that intent and periodically attempt to reacquire it. Prefer portable periodic reopen attempts for correctness; OS-specific arrival/removal notification may later be an optimization or UI aid. Automatic hot-plug recovery is not `Reset()` and must not increment `Resets`.

Recovery policy separates retry frequency from retry lifetime and retry-log frequency. `SerialRecoveryPolicy` models `RetryInterval`, `MaximumAttempts`, and `MaximumDuration`; negative/unset finite limits mean recovery may continue indefinitely. When both attempt and duration limits are finite, whichever is reached first stops automatic recovery. Repeated identical failures should eventually be condensed in the diagnostic log without slowing actual retry attempts; a materially different failure resets that condensation. Successful recovery is always a prominent baseline diagnostic event.

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

`ISerialTransportFactory` supplies independently owned transport instances and allows deterministic fault injection and fresh-instance reacquisition tests.

### Lifecycle checkpoint completed; automatic recovery is next

The safe/idempotent teardown checkpoint is now implemented on `main`.

Settled behavior:

- `ConnectionRequested` is explicit connection intent and is independent of transport availability.
- `ConnectionState` is authoritative for transport lifecycle; `Ready` requires `Connected` plus an open owned transport.
- `Connect()` sets intent before acquisition. Failed `Open()` or later initialization failure leaves intent set, transitions to `Unavailable`, and releases the failed transport without emitting a normal `Disconnecting` event.
- A later explicit `Connect()` from `Unavailable` performs a fresh acquisition using a new transport instance. This is not yet automatic recovery.
- Teardown first stops and joins all workers, then unsubscribes transport events, closes, disposes, and releases the transport reference. Synchronization primitives are never disposed while workers can still touch them.
- Worker references are cleared after termination.
- Repeated `Disconnect()` is harmless. Disconnecting from `Unavailable` clears intent without trying to clean the already-released failed transport again.
- `Dispose()` is idempotent and ends in `Disposed`; `Disconnect()`/`Close()` after disposal are harmless.
- Operational calls after disposal (`Connect`, `Command`, `StageProtocolSettings`, `Reset`, `WaitForIdle`) throw an intentional `ObjectDisposedException` from `SerialDevice`, rather than failing accidentally through disposed internal wait handles.
- Transport close/dispose cleanup is best-effort: later cleanup steps still run when an earlier one throws, and cleanup exceptions are diagnostic evidence.
- Deterministic fake-transport tests cover successful lifecycle, failed open, failure after open, cleanup exceptions, repeated disconnect/dispose, state/intent semantics, post-disposal operations, fresh manual reacquisition, event unsubscription, and worker-before-transport teardown ordering using a blocked fake write.

Do **not** add automatic hot-plug recovery until this checkpoint has had the requested build/test pass. The next implementation block is automatic `Unavailable` reacquisition using fresh transports, `SerialRecoveryPolicy`, race/resource-release tests, and condensed repetitive diagnostics. Communications faults remain evidence and must not be conflated with transport loss.

`Resets` remains a useful reusable transport-reliability diagnostic because upstream applications use reset counts to decide when repeated communications recovery warrants alerting a user. Do not remove or reinterpret it. Automatic transport reacquisition is not a reset.

## Configuration ownership and persistence

`TerminalConfiguration` is the authoritative live configuration. The UI binds to it; serial sessions, diagnostic logging, and persistence consume it directly rather than interrogating controls.

The configuration is deliberately serializable as plain JSON. The same representation should support automatic restoration of the last session, named saved profiles later, Save As / Load / Delete / Revert operations later, and easy inspection/exchange/bug-report attachment.

The last configuration is restored at startup and saved at clean shutdown as `SerialPortTerminal.configuration.json` beside the application. A missing previously selected COM port does not invalidate or overwrite the rest of the restored configuration.

Configuration changes are semantic events carrying old/new values. Once a diagnostic session log exists, changes are recorded there. Log creation records an initial configuration snapshot, and every connection attempt records the effective connection/protocol configuration.

Keep these concerns conceptually distinct even though the current configuration object is their common interface:

1. Application preferences — log folder/prefix/retention, UI behavior.
2. Serial connection — port, baud, parity, data bits, stop bits, handshake, RTS.
3. Protocol — CRC, framing/termination, pacing, receive-silence behavior, CRC-error acceptance policy.
4. Received display — escaped-safe text or bytes, whether to show CRC bytes, and future filters.
5. Diagnostic detail/presentation — distinct from Received display and from whether the automatic log exists.

Deferred conveniences include named complete device profiles, dirty-state/revert, import/export, persistent profile-specific command history, named/preset commands, timestamped user annotations/bookmarks, and concise discoverable configuration help.

The permanent left configuration panel is provisional. A generously sized flyout/drawer is favored later so it can overlay the main workspace without shrinking Communications or Transport Diagnostics.

## Communications model

The UI distinguishes the **conversation** from evidence of **how the conversation was transported**.

### Send / Received

- Use **Send** and **Received** rather than assuming every received message answers a command.
- Send is above Received; Enter sends; Up/Down recalls command history.
- Always show received traffic, including unsolicited/datalogging reports.
- Display choices never alter authoritative received data.
- Received display modes are escaped-safe Text and Bytes. Text keeps printable ASCII readable while rendering control/unsafe bytes as escapes such as `\r`, `\n`, `\t`, and `\xHH`.
- CRC acceptance and CRC presentation are separate concerns.
- Normally follow the latest received entry, but do not yank the view downward after the user deliberately scrolls up.

### Authoritative received data

`ReceivedData` carries original recognized-frame bytes, presentation payload bytes, CRC bytes/order, received CRC, calculated/expected residue, and CRC validity instead of reducing the receive boundary to `string`.

```text
physical serial reads/chunks
        |
SerialDevice framing
        |
ReceivedData frame records
        |
logical Received messages / presentation grouping
```

For clearly framed traffic, every recognized logical message begins a new Received display entry even without CR/LF. UI separation is independent of payload whitespace; preserve CR/LF and blank lines actually present in the payload.

For truly unframed/streaming traffic, OS/serial read boundaries are **not** message boundaries. The current selectable no-term-character mode is explicitly silence-framed, so silence is a deliberate message boundary there; a separate truly unframed mode does not yet exist.

## Transport diagnostics and logging

Transport Diagnostics explains how traffic transpired: raw TX/RX bytes, chunks, timing, CRC details/errors, connection and pin events, buffer conditions, recovery, configuration changes, etc.

The **disk log is the authoritative transport-diagnostic record**. The UI is a live view/tail of the same ordered stream, not a separately generated representation.

`DiagnosticLog` uses an ordered producer/consumer: producers timestamp/enqueue quickly; a worker writes to disk and publishes UI entries in order. Shutdown drains pending entries. Logging should not materially perturb the timing being diagnosed.

Normal successful CRC validation and absence of CRC are not noteworthy conditions in response log lines. Report CRC status there only when an error is detected. Attempts to send without a connected device are timestamped diagnostic events.

### Baseline vs detailed diagnostics

Automatic session logging is independent of diagnostic detail. The log continues when detailed transport events are disabled. Baseline events such as session/connection state, errors, commands and received messages remain recorded. The UI checkbox means **Include detailed transport events**, not “enable logging.” Signal logging is separately selectable.

### Automatic session logs

- Lazily create the log on the first connection attempt or another event important enough to persist, such as a rejected send.
- One log spans the application/investigation run, including disconnect/reconnect cycles.
- Default directory: `Logs` relative to the application directory.
- Default automatic prefix: `spt-`.
- Lexicographically sortable filename, e.g. `spt-2026-10-06_192735.log`.
- Default retention: 25 automatic logs.
- Retention applies only to automatic-name matches; never silently delete unrelated/user-renamed files or the current log.
- Show the active filename in Transport Diagnostics.
- Future: validated backing-file rename, Open Log / Open Log Folder / export.

Current application settings defaults:

```text
LogFolder       = "Logs"
LogFilePrefix   = "spt-"
LogFilesToKeep  = 25
```

Application settings and terminal configuration are stored portably beside the application rather than under AppData.

## CRC and framing: critical legacy requirement

The authoritative Aeon serial protocol notes and controller protocol documents establish field framing as:

```text
<message><two-byte CRC><termchar>
```

For Aeon protocols the term character is ETX (`0x03`). CRC is calculated over the message and excludes ETX. CRC bytes are transmitted before ETX, low byte then high byte for the standard Aeon settings.

Except as the terminator, ETX is **not legal in the payload**. Either CRC byte may legitimately equal ETX. Therefore an ETX encountered after payload data cannot immediately be classified as the real terminator because it may be CRC byte 1 or CRC byte 2. Do not simplify this delayed classification without preserving that behavior.

The inherited term-character parser is characterized by synthetic tests covering ordinary valid messages, either CRC byte equal to ETX, arbitrary fragmentation at every byte boundary, back-to-back responses, realistic multiline textual payload, invalid payload ETX rejection followed by recovery, and CRC failure without poisoning the following valid message.

The parser is a characterized migration baseline. Do not casually rewrite `ProcessRx`; future cleanup must proceed against these tests.

CRC-error acceptance is protocol/application policy. Ordinary AeonHacs applications should normally reject bad CRC messages, but SPT defaults to retaining/displaying them for diagnosis and offers **Suppress messages with CRC errors** independently of whether CRC bytes are shown.

“No term character” selects the inherited silence-framed receive strategy. A separate truly unframed/streaming mode may be useful later but is not presently represented.

## Protocol changes while connected

Protocol configuration is represented by an immutable `SerialProtocolSettings` snapshot. Compatible changes are staged and adopted at a receive-message boundary so a partially received frame is never interpreted using two configurations.

Changes that switch receive strategy require reconnect. Transmit must not force adoption of a pending receive protocol snapshot because doing so could swap RX interpretation in the middle of a frame. This behavior is characterized by tests.

Future scrutiny should include deterministic staging-test synchronization, memory visibility of active/pending snapshot references, and eventually constructing each TX codeword from one captured immutable protocol snapshot rather than relying on shared mutable CRC state.

## Receive-path preservation / AeonHacs incorporation

Current `SerialDevice` retains the broad current AeonHacs strategy: lightweight `DataReceived` signaling, receive-event coalescing until short silence, block reads into a ring buffer, separate parsing worker, incremental CRC, transmit pacing machinery, counters/timing, and optional silence framing. These came from field use and should not be replaced merely because simpler code looks nicer.

Useful AeonHacs improvements already adapted include binary-safe byte formatting/logging, current receive/coalescing structure, incremental CRC machinery, message/byte pacing, CRC-error forwarding policy, signal state/events, and transport counters/timing. SPT replaces Hacs globals/Notify/LogFile/Utility dependencies with local boundaries.

Timing configuration exposes and persists milliseconds between messages (`-1` disables), milliseconds between bytes (`-1` disables), and maximum milliseconds of receive silence. These apply on connection and can be changed live while connected.

Still to assess: `RTS_CONTROL_TOGGLE` on current .NET/Windows; richer communications-health counters/timestamps after lifecycle correctness; ring-buffer full/empty ambiguity and exact capacity around a 4096-byte read/wrap.

## Serial signals / RTS

The UI presents RTS, CTS, DTR, DSR, DCD and RI as read-only status indicators. Signal transitions are available to Transport Diagnostics and can be logged independently of other detailed events.

Connection configuration owns RTS behavior. Do not add a second manual control in the signal bank. RTS modes are Disabled (force inactive), Enabled (force active), and Toggle (driver/UART-controlled transmit assertion where supported). Continuously asserted RTS is used by some field devices as a parasitic power source and must remain supported.

Verify `RTS_CONTROL_TOGGLE` with current `System.IO.Ports` and representative adapters. Prefer a supported Win32 route if practical; do not restore reflection into private `SerialPort` internals.

## Legacy functional parity / replacement readiness

The old WinForms implementation remains available under tag `legacy-winforms`.

Already at parity or better: ordinary serial configuration/connect/disconnect; command sending/history; exact byte-native sending with strict escapes/octal input; escaped-safe received Text/Bytes; CRC configuration/error policy/byte visibility; characterized Aeon framing and silence framing; pacing/silence controls; signal status/logging; richer ordered diagnostics and automatic persistent logs; binary TX/RX logging; reusable engine/session/UI separation; portable previous-configuration restoration.

Legacy behavior still missing or needing an explicit decision:

- legacy Reset cleared counters/display and recreated the serial device; modern equivalent behavior has not yet been decided;
- legacy showed compact command/response/bytes-read counters;
- legacy displayed calculated TX/RX CRC values separately;
- legacy had a separate user-controlled normalized data-recording file distinct from transport diagnostics;
- physical connection settings are locked while connected; compatible protocol/timing settings can apply live and receive-strategy changes require reconnect;
- legacy Escape cleared the command editor/history position.

Before declaring the legacy version superseded for general field use:

1. Build/test the completed explicit lifecycle checkpoint.
2. Add automatic hot-plug reacquisition while preserving communications faults as evidence rather than treating them as immediate disconnects.
3. Decide the remaining legacy functions above rather than accidentally dropping them.
4. Verify target-instrument-specific RTS toggle behavior if needed.

CRC/framing characterization and pacing/silence exposure are complete enough to leave the replacement-critical list.

## UI / implementation principles

Avalonia is the UI framework. Serial/application behavior must remain testable without the window. Preserve functional organization, not old WinForms pixel geometry; use proper layout rather than magic offsets.

The configuration pane is provisional and intentionally wide enough for clear controls. A flyout/drawer is preferred later and may overlay the main workspace.

Connection selectors are right-edge aligned within their value column. Numeric/hex protocol fields use right-aligned monospaced presentation with `Cascadia Mono` preferred and `Consolas`/generic monospace fallback. Serial signals are read-only LED-style indicators.

Text views still use `TextBox.Text +=`, which becomes O(N^2); replace with an append-friendly/bounded presentation. The disk diagnostic log remains complete even if the UI retains only a bounded live tail.

Display/detail controls should apply live where sensible. Configuration help is deferred until after immediate field-test/replacement work; when added, explain semantics and operational consequences, including that `MS`/`MSB` means most significant, and cover CRC/framing, handshake/RTS, pacing/silence, Received display, and diagnostic controls.

## Deployment / installation

A Windows x64 self-contained publish profile is the first deployment mechanism. It favors a portable installation: no separate .NET runtime prerequisite, and configuration/logs remain alongside the application as intended.

The tracked profile is `Properties/PublishProfiles/FolderProfile.pubxml`; an equivalent command is `dotnet publish SerialPortTerminal.csproj -p:PublishProfile=FolderProfile`. The current profile publishes beneath `bin\Publish`. Validate the package on a clean Windows machine before calling it an installer/release.

For the immediate replacement milestone, a self-contained portable package is preferable to MSI/MSIX work. Installer machinery, shortcuts, file associations, signing, updates, and architecture variants can come later. Installation under a protected location such as Program Files would require reconsidering the current beside-the-executable writable settings/log policy.

## Longer-term direction

Possibilities to preserve, not commitments:

- If the serial portion settles into a genuinely general subsystem, consider extracting a small independent library shared by SPT and AeonHacs/applications.
- Watch for clean general improvements developed here that should be taken back upstream to AeonHacs without constraining SPT around AeonHacs compatibility.
- The current Receive/Process/Transmit thread model is a migration baseline, not necessarily the final concurrency architecture. After behavioral characterization, reconsider async/await or another cleaner model where it improves correctness/readability without losing block acquisition or timing behavior.
- Characterization tests are the bridge to deeper cleanup of acquisition, buffering, pacing, recovery, CRC/framing, and concurrency.
- Make eventual handoff possible without oral history: sharp-edge comments, tests demonstrating historical protocol peculiarities, clear reusable-engine/application-policy boundaries, and permanent architecture documentation where warranted.

## Current status

Implemented:

- modern .NET/C# Avalonia application; legacy WinForms preserved under tag `legacy-winforms`;
- currentized byte-native `SerialDevice`, incremental CRC, serial settings and data formatting;
- `TerminalSession` application boundary;
- authoritative byte-oriented `ReceivedData` with CRC bytes/status/error metadata;
- Communications and Transport Diagnostics workspaces;
- Send/Received separation, Enter-to-send, command history, strict binary-safe send parsing;
- asynchronous ordered `DiagnosticLog` and lazy automatic per-run disk logging with portable defaults and retention;
- CRC controls, CRC-error suppression, Received CRC-byte display;
- characterized term-character and silence receive paths with fragmentation/recovery tests;
- immutable/staged protocol settings for safe compatible live updates;
- persisted/live timing and receive-silence controls;
- serial signal status and optional signal logging;
- diagnostic detail separated from automatic logging;
- authoritative serializable/restored/logged `TerminalConfiguration`;
- self-contained Windows x64 publish profile;
- `SerialConnectionState` and `SerialRecoveryPolicy` design types;
- `ISerialTransport` / `ISerialTransportFactory` production seam plus deterministic fake transport;
- explicit `ConnectionRequested` + `SerialConnectionState` lifecycle integration;
- worker-first, centralized transport teardown; partial-acquisition cleanup; harmless repeated disconnect; idempotent disposal; explicit post-disposal operation behavior;
- xUnit characterization covering send parsing, CRC, virtual responses, fragmentation, framing, protocol staging, transport cleanup, lifecycle intent/state, fresh reacquisition, and teardown ordering.

Automatic unavailable/reacquisition recovery is **not implemented yet**. This is intentional: it is the next block after the current lifecycle checkpoint is built/tested cleanly.

## Near-term priority

Priority is safe legacy replacement / Hacs parity first, then usability, then convenience:

1. Build/run the current lifecycle checkpoint and full test suite; exercise a basic connect/disconnect/reconnect in the UI.
2. Add automatic transport-unavailable recovery/hot-plug reacquisition using fresh transport instances, configurable retry policy, resource-release invariants, race tests, and condensed repetitive diagnostics. Bad messages and isolated communications faults remain connected/observable rather than forcing recovery.
3. Revisit intentional `Reset()` semantics and remaining legacy parity items: compact counters, separate normalized data recording, standalone CRC displays, and Escape behavior.
4. Verify `RTS_CONTROL_TOGGLE` if a target field setup needs it.
5. Improve usability: configuration flyout/profiles, scalable live views/follow-tail behavior, configuration help/tooltips, and other conveniences.
6. After lifecycle/recovery is complete, discuss and perform the deliberate project/application rename and coding-filename cleanup as one coherent identity change.
