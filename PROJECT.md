# SerialPortTerminal project notes

> Temporary live coordination document. Capture unresolved decisions and deferred ideas so they are not lost accidentally. Items may be removed deliberately when we decide not to pursue them. Delete this file once mature documentation and GitHub issues can carry the remaining work.

## Purpose

SerialPortTerminal is both an interactive serial diagnostic/investigation tool and a proving ground for lower-level serial communications machinery used by applications.

The reusable serial layer must preserve evidence an ordinary application might hide: exact TX/RX bytes, CRC bytes and failures, framing behavior, timing, connection/disconnection events, pin changes, buffer problems, counters, and related diagnostics. Diagnostic capability is product functionality, not merely developer tracing.

The immediate practical motivation is troubleshooting a Eurotherm controller. The previous SPT lacked binary byte logging and CRC-byte visibility present in newer AeonHacs code.

The project is approaching a conceptual break from its WinForms predecessor. Once the current `SerialDevice` lifecycle/recovery refactor is complete, reconsider the application/project name deliberately. Until then, defer naming changes so the name reflects the finished abstraction rather than an anticipated one. The eventual cleanup should also favor coding filenames without spaces (for example, a solution filename such as `SerialPortTerminal.sln`) because quoting/escaping behavior still varies across shells, scripts, build tools, Windows, and Linux. Do not perform this rename piecemeal during the lifecycle work.

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

The lifecycle refactor must keep three concepts independent:

1. **Connection intent** — whether the caller/user wants the configured endpoint to remain connected.
2. **Transport lifecycle** — whether an actual OS serial transport is connecting, available, unavailable, disconnecting, or disposed.
3. **Communications health** — CRC failures, malformed/incomplete responses, framing errors, timeouts, garbled data, buffer anomalies, read/write failures, and related evidence while attempting communication.

Bad communications do **not** imply loss of transport. A CRC failure, malformed message, unexpected byte, incomplete response, timeout, buffer anomaly, or isolated I/O exception must not automatically disconnect the device. Preserve/count/timestamp/report such evidence and continue whenever useful communication may still be possible. Repeated transient faults are themselves valuable diagnostic evidence.

`SerialConnectionState` represents transport availability, not communications quality. Its current states are `Disconnected`, `Connecting`, `Connected`, `Disconnecting`, `Unavailable`, and `Disposed`. Recovery is an activity while `Unavailable`, not a peer lifecycle state. A separate `ConnectionRequested` indication should expose connection intent; recovery activity/counters/timestamps should be exposed independently as they are implemented.

The core ownership invariant is:

> **Connection intent persists; ownership of a failed transport does not.**

When sufficient evidence establishes that a transport is unavailable, quiesce its workers, unsubscribe events, close/dispose the transport, and release every OS resource associated with that transport instance. Keep the connection intent and configuration. Recovery attempts must create fresh transport instances. Any recovery attempt that partially opens and then fails initialization must close/dispose that temporary instance before another attempt. While connection is requested and state is `Unavailable`, the implementation should normally own no open serial-port handle.

USB hot disconnect/reconnect is a normal field condition. If the user requested COMx remain connected and the device disappears, retain that intent and periodically attempt to reacquire it. Prefer portable periodic reopen attempts for correctness; OS-specific arrival/removal notification may later be an optimization or UI aid, not the foundation of recovery. Automatic hot-plug recovery is not `Reset()` and must not increment `Resets`.

Recovery policy separates retry frequency from retry lifetime and from retry-log frequency. `SerialRecoveryPolicy` currently models `RetryInterval`, `MaximumAttempts`, and `MaximumDuration`; negative/unset finite limits mean recovery may continue indefinitely. When both attempt and duration limits are finite, whichever is reached first stops automatic recovery. Repeated identical failures should eventually be condensed in the diagnostic log without slowing actual retry attempts; a materially different failure resets that condensation. Successful recovery is always a prominent baseline diagnostic event.

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

`ISerialTransportFactory` allows tests to supply successive transport instances, which is essential for proving that failed acquisition/recovery releases old resources and reacquisition uses a new instance.

### Current lifecycle implementation status and next checkpoint

The lifecycle design types and transport seam are present, but `SerialDevice` has **not yet been refactored to use the new lifecycle semantics fully**. At the current baseline it still uses inherited `connected`/`active` booleans, `Connect()` catches acquisition failures by calling the ordinary `Disconnect()` path, and `Disconnect()` closes/disposes the port before waiting for workers. `Dispose()` is not yet idempotent because it calls `Disconnect()` and then disposes synchronization events; a second disposal or later operation can touch already-disposed events. Worker references are not cleared after termination. These are known implementation gaps, not intended behavior.

The next coding checkpoint, before automatic recovery, is deliberately limited to:

1. centralize partial/full transport teardown and make resource ownership ordering correct;
2. stop workers before releasing the transport they may still be using, and never dispose synchronization primitives while a live worker can touch them;
3. make repeated `Disconnect()` harmless and `Dispose()` idempotent;
4. ensure failed `Open()` or failure after `Open()` releases the partial transport without emitting a fictitious normal disconnect lifecycle;
5. introduce `ConnectionRequested` and make `SerialConnectionState` authoritative for lifecycle state;
6. define explicit post-disposal behavior (ordinary operations should fail predictably rather than through accidental `ObjectDisposedException` from internal wait handles; `Disconnect()` after disposal should be harmless);
7. extend deterministic fake-transport tests for successful connect/disconnect, failed open, failure after open, close/dispose exceptions, repeated disconnect/dispose, lifecycle state/intent, and post-disposal operations;
8. only after that checkpoint builds/tests cleanly, add automatic unavailable/reacquisition behavior and its race/resource-release tests.

Do not ask for user testing after every small commit. Make small coherent commits on `main`, then stop at the meaningful checkpoint above for a single pull/build/test pass. Before every write, re-read current `main`/affected files and base the change on the current tree; do not recreate a tree from stale state. A previous stale-tree write accidentally restored an obsolete icon and is specifically not to be repeated. The intended current icon filename is `SerialPortTerminal.ico` (no spaces).

`Resets` remains a useful reusable transport-reliability diagnostic because upstream applications use reset counts to decide when repeated communications recovery warrants alerting a user. Do not remove or reinterpret it merely because the current terminal UI does not expose it. Automatic transport reacquisition is not a reset.

## Configuration ownership and persistence

`TerminalConfiguration` is the authoritative live configuration. The UI binds to it; serial sessions, diagnostic logging, and persistence consume it directly rather than interrogating controls.

The configuration is deliberately serializable as a plain JSON object. The same representation should support:

- automatic restoration of the last session configuration;
- named saved device profiles later;
- Save As / Load / Delete / Revert profile operations later;
- easy inspection, comparison, exchange, and bug-report attachment without a proprietary format.

The last configuration is currently restored at startup and saved at clean shutdown as `SerialPortTerminal.configuration.json` beside the application. A missing previously selected COM port does not invalidate or overwrite the rest of the restored configuration.

Configuration changes are semantic events carrying old/new values. Once a diagnostic session log exists, changes are recorded there. Log creation records an initial configuration snapshot, and every connection attempt records the effective connection/protocol configuration so a diagnostic log remains interpretable even when settings were established before logging began.

Keep these concerns conceptually distinct even though the current configuration object is their common interface:

1. Application preferences — log folder/prefix/retention, UI behavior.
2. Serial connection — port, baud, parity, data bits, stop bits, handshake, RTS.
3. Protocol — CRC, framing/termination, pacing, receive-silence behavior, CRC-error acceptance policy.
4. Received display — escaped-safe text or bytes, whether to show CRC bytes, and future filters.
5. Diagnostic detail/presentation — distinct from Received display and from whether the automatic log exists.

### Deferred configuration conveniences

Prioritize these only after replacement-critical communications behavior is sound:

- named device profiles containing complete useful setups;
- dirty-state indication for a loaded profile and one-click revert;
- import/export naturally using the same JSON representation;
- persistent command history, preferably profile-specific once profiles exist;
- named/preset commands per profile;
- timestamped user annotations/bookmarks in the diagnostic stream (for physical actions such as swapping leads, changing an instrument address, or marking a reproduced failure);
- concise discoverable help for configuration controls, using tooltips and/or small information affordances rather than permanently expanding labels. Explain semantics and operational consequences where useful, especially CRC/framing options, `MS`/`MSB` bit and byte ordering, handshake/RTS ownership, pacing/silence settings, Received display choices, and diagnostic options.

The permanent left configuration panel is provisional. A generously sized flyout/drawer is favored once configuration behavior matures so it can overlay the main workspace without shrinking Communications or Transport Diagnostics.

## Communications model

The UI distinguishes the **conversation** from evidence of **how the conversation was transported**.

### Send / Received

- Use **Send** and **Received** rather than assuming every received message answers a command.
- Send is above Received; Enter sends; Up/Down recalls command history.
- Always show received traffic, including unsolicited/datalogging reports.
- Display choices never alter authoritative received data.
- Received display modes are escaped-safe Text and Bytes. Text keeps printable ASCII readable while rendering control/unsafe bytes as escapes such as `\r`, `\n`, `\t`, and `\xHH`.
- CRC acceptance and CRC presentation are separate concerns. A diagnostic user may choose to receive/display a message whose CRC failed, and independently choose whether the Received presentation includes the normally stripped CRC bytes.
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

For truly unframed/streaming traffic, OS/serial read boundaries are **not** message boundaries. Multiple reads belong to one continuing display entry until a genuine higher-level boundary such as disconnect/reset/session boundary or future explicit policy. The current selectable no-term-character mode is explicitly silence-framed, so silence is a deliberate message boundary there; a separate truly unframed mode does not yet exist.

## Transport diagnostics and logging

Transport Diagnostics explains how traffic transpired: raw TX/RX bytes, chunks, timing, CRC details/errors, connection and pin events, buffer conditions, recovery, configuration changes, etc.

The **disk log is the authoritative transport-diagnostic record**. The UI is a live view/tail of the same ordered stream, not a separately generated representation.

`DiagnosticLog` uses an ordered producer/consumer: producers timestamp/enqueue quickly; a worker writes to disk and publishes UI entries in order. Shutdown drains pending entries. Logging should not materially perturb the timing being diagnosed.

Normal successful CRC validation and absence of CRC are not noteworthy conditions in response log lines. Report CRC status there only when an error is detected. Attempts to send without a connected device are timestamped diagnostic events, not unstructured UI text.

### Baseline vs detailed diagnostics

Automatic session logging is independent of diagnostic detail. The log continues when detailed transport events are disabled. Baseline events such as session/connection state, errors, commands and received messages should remain recorded. The UI checkbox means **Include detailed transport events**, not “enable logging.” `LogEverything` should eventually be replaced or clarified so this distinction is explicit in the model rather than accidental policy.

Signal logging is separately selectable so pin transitions can be captured without enabling every detailed transport event.

### Automatic session logs

- Lazily create the log on the first connection attempt or another event important enough to persist, such as a rejected send.
- One log spans the application/investigation run, including disconnect/reconnect cycles.
- Default directory: `Logs` relative to the application directory; configurable relative or absolute path later.
- Default automatic prefix: `spt-`; configurable later.
- Lexicographically sortable filename, e.g. `spt-2026-10-06_192735.log`.
- Default retention: 25 automatic logs; configurable later.
- Retention applies only to automatic-name matches; never silently delete unrelated/user-renamed files or the current log.
- Show the active filename in Transport Diagnostics.
- Future: allow editing the filename to rename the actual backing file, with validation/collision handling. A renamed file naturally falls outside automatic retention; no separate Keep metadata is initially necessary.
- Possible conveniences later: Open Log / Open Log Folder / export. Automatic capture remains primary.

Current application settings defaults:

```text
LogFolder       = "Logs"
LogFilePrefix   = "spt-"
LogFilesToKeep  = 25
```

Application settings and terminal configuration are stored portably beside the application rather than under AppData.

## CRC and framing: critical legacy requirement

The authoritative Aeon serial protocol notes and controller protocol documents establish the field framing as:

```text
<message><two-byte CRC><termchar>
```

For the Aeon protocols the term character is ETX (`0x03`). CRC is calculated over the message and excludes the terminating ETX. The CRC bytes are transmitted before ETX (low byte then high byte for the standard Aeon settings).

Except as the terminating character, ETX is **not legal in the message payload**. Either CRC byte may legitimately equal ETX. This is the historical sharp edge that makes the parser look unusual: an ETX byte encountered after payload data cannot immediately be classified as the real terminator because it may be CRC byte 1 or CRC byte 2. Do not simplify this delayed classification without preserving that behavior.

The inherited term-character parser is now characterized by synthetic tests covering:

- ordinary valid terminated messages;
- either CRC byte equal to ETX;
- arbitrary fragmentation at every byte boundary;
- back-to-back responses;
- realistic multiline textual payload containing spaces and CR/LF;
- invalid payload ETX rejection followed by successful parser recovery;
- CRC failure without poisoning a following valid message.

The parser is therefore considered a characterized migration baseline. Do not casually rewrite `ProcessRx`; future cleanup should proceed against these tests rather than from aesthetic assumptions about the existing algorithm.

CRC-error acceptance is protocol/application policy. Ordinary AeonHacs applications should normally reject messages whose CRC fails, but SPT defaults to retaining/displaying them for diagnosis and offers **Suppress messages with CRC errors** independently of whether CRC bytes are shown.

“No term character” currently selects the inherited silence-framed receive strategy. A separate truly unframed/streaming mode may be useful later but is not presently represented.

## Protocol changes while connected

Protocol configuration is represented by an immutable `SerialProtocolSettings` snapshot. Compatible changes are staged and adopted at a receive-message boundary so a partially received frame is never interpreted using two CRC/framing configurations.

Changes that switch receive strategy (for example term-character framing versus silence framing, or other currently incompatible strategy changes) require reconnect rather than mutating the active receive machinery underneath a frame.

Transmit must not force adoption of a pending receive protocol snapshot: doing so could swap RX interpretation in the middle of a frame. This behavior is characterized by tests. Future scrutiny should include deterministic staging-test synchronization, memory visibility of active/pending snapshot references, and eventually constructing each TX codeword from one captured immutable protocol snapshot rather than relying on shared mutable CRC state.

## Receive-path preservation / AeonHacs incorporation

Current `SerialDevice` retains the broad current AeonHacs strategy: lightweight `DataReceived` signaling, receive-event coalescing until short silence, block reads into a ring buffer, separate parsing worker, incremental CRC, transmit pacing machinery, counters/timing, and optional silence framing. These came from field use and should not be replaced merely because simpler code looks nicer.

Useful AeonHacs improvements already incorporated/adapted include binary-safe byte formatting/logging, current receive/coalescing structure, incremental CRC machinery, message/byte pacing in the engine, CRC-error forwarding policy, signal state/events, and transport counters/timing. SPT deliberately replaces Hacs globals/Notify/LogFile/Utility dependencies with local boundaries.

Timing configuration now exposes and persists:

- milliseconds between messages (`-1` disables pacing);
- milliseconds between bytes (`-1` disables pacing);
- maximum milliseconds of receive silence used by acquisition/coalescing and silence framing.

These timing values apply on connection and can be changed live while connected.

Still to assess where useful:

- `RTS_CONTROL_TOGGLE` implementation on current .NET/Windows;
- richer communications-health counters/timestamps after lifecycle correctness is established.

Review ring-buffer full/empty ambiguity and exact capacity around a 4096-byte read/wrap.

## Serial signals / RTS

The UI now presents RTS, CTS, DTR, DSR, DCD and RI as read-only status indicators. Signal transitions are available to Transport Diagnostics and can be logged independently of other detailed transport events.

Connection configuration owns RTS behavior. Do not add a second manual control in the signal bank. RTS modes are:

- Disabled — force RTS inactive;
- Enabled — force RTS active;
- Toggle — driver/UART-controlled transmit assertion where supported.

Enabled/Disabled are therefore configuration choices/overrides, while Toggle is transmitter-driven control. Continuously asserted RTS is used by some field devices as a parasitic power source and must remain supported.

`RTS_CONTROL_TOGGLE` is separate driver-controlled transmit assertion useful for some RS-485 arrangements. Verify whether/how the modern implementation can request it reliably with current `System.IO.Ports` and representative adapters. Prefer a supported Win32 route if practical; do not restore reflection into private `SerialPort` internals merely to reproduce the old mechanism.

## Legacy functional parity / replacement readiness

The modernization is close enough to treat **replacement readiness** as the critical milestone. The old WinForms implementation remains available under tag `legacy-winforms`.

### Already at parity or better

- ordinary serial-port configuration and connect/disconnect;
- interactive command sending and Up/Down command history;
- exact byte-native sending with strict `\xHH`, common escapes, and octal input;
- received traffic display with escaped-safe Text and Bytes representations;
- CRC enable/configuration, CRC-error suppression policy, error metadata, and optional CRC-byte visibility;
- characterized Aeon term-character/CRC framing and silence-framed receive behavior;
- byte and message pacing plus receive-silence controls, persisted and live-applicable;
- serial signal status and optional signal-transition logging;
- substantially richer ordered transport diagnostics and automatic persistent session logs;
- binary TX/RX byte logging from newer Hacs behavior;
- modern separation of reusable serial engine, terminal/session behavior, configuration, and Avalonia UI;
- portable restoration of the previous terminal configuration.

### Legacy behavior still missing or needing an explicit decision

- legacy Reset cleared counters/display and recreated the serial device; modern equivalent behavior has not yet been provided/decided;
- legacy showed command/response/bytes-read counters; modern diagnostics contain much of the evidence but there is no equivalent compact status display;
- legacy displayed calculated TX/RX CRC values separately; modern diagnostics/CRC-byte/error display may make those redundant, but decide consciously rather than losing them accidentally;
- legacy had a separate user-controlled normalized data-recording file (`Start Logging` / `Stop Logging`) distinct from transport diagnostics. Decide whether this workflow is still useful; do not confuse it with the richer automatic diagnostic log;
- physical connection settings are deliberately locked while connected. Compatible protocol and timing settings can now apply live; receive-strategy changes require reconnect. This differs from legacy automatic reconfiguration/reconnection and is a conscious behavior change;
- legacy Escape cleared the command editor/history position; modern command-entry ergonomics do not yet duplicate that exact behavior.

### Replacement-critical technical gaps

Before declaring the legacy version superseded for general field use:

1. Complete and test the explicit connection-intent/transport lifecycle refactor described above, including safe/idempotent teardown.
2. Add automatic hot-plug reacquisition while preserving communications faults as evidence rather than treating them as immediate disconnects.
3. Decide the few legacy functions above rather than accidentally dropping them.
4. Verify any target-instrument-specific RTS toggle requirement before relying on that mode in the field.

CRC/framing characterization and pacing/silence exposure are complete enough to leave the replacement-critical list. A separate truly unframed/streaming receive mode should be designed only if a real use case requires it; the existing no-term-character mode is intentionally silence-framed.

## UI / implementation principles

Avalonia is the UI framework. Serial/application behavior must remain testable without the window. Preserve functional organization, not the old WinForms pixel geometry; use proper layout rather than magic offsets.

The current configuration pane is provisional and intentionally given enough width for clear controls. A flyout/drawer is preferred later and may overlay the main workspace rather than resize it.

Connection selectors are right-edge aligned within their value column. Numeric/hex protocol fields use right-aligned monospaced presentation with `Cascadia Mono` preferred and `Consolas`/generic monospace fallback. Serial signals are read-only LED-style indicators; configuration such as RTS mode remains in Connection rather than becoming an ad-hoc signal control.

Text views still use `TextBox.Text +=`, which becomes O(N^2); replace with an append-friendly/bounded presentation. The disk diagnostic log remains complete even if the UI retains only a bounded live tail.

Display/detail controls should apply live where sensible rather than silently taking effect only on the next connection.

Configuration help is deliberately deferred until after the immediate field-test/replacement work. When added, specialized controls should have concise, discoverable explanations (tooltips and/or small information affordances) covering both meaning and operational consequences where those are not obvious. Do not assume users know that `MS`/`MSB` means **most significant**; explain bit order and byte order explicitly. Use the same pattern for CRC/framing, handshake/RTS, pacing/silence, Received display, and diagnostic controls rather than relying on terse labels alone.

## Deployment / installation

A Windows x64 self-contained publish profile is provided as the first deployment mechanism. It favors a portable installation: no separate .NET runtime prerequisite, and configuration/logs remain alongside the application as intended.

The tracked publish profile is `Properties/PublishProfiles/FolderProfile.pubxml`; redundant/user-specific publish-profile files were removed. An equivalent `dotnet publish SerialPortTerminal.csproj -p:PublishProfile=FolderProfile` invocation is also valid. The current profile publishes beneath `bin\Publish`. Validate the produced package on a clean Windows machine before calling it an installer/release.

For the immediate replacement milestone, a self-contained portable package is preferable to spending time on MSI/MSIX machinery. A conventional installer, shortcuts, file associations, signing, update mechanism, and architecture variants can be considered after the application is functionally trustworthy. If eventually installed under a protected location such as Program Files, the current beside-the-executable writable settings/log policy will need reconsideration or an explicitly user-writable data directory.

## Longer-term direction

These are possibilities to preserve, not commitments:

- If the serial portion settles into a genuinely general subsystem, consider extracting a small independent library shared by SPT and AeonHacs/applications rather than allowing either product to own a divergent canonical copy.
- Watch for clean general improvements developed here that should be taken back upstream to AeonHacs, without constraining SPT around AeonHacs compatibility during modernization.
- The current Receive/Process/Transmit thread model is a migration baseline, not necessarily the final concurrency architecture. After behavioral characterization, reconsider async/await or another cleaner model where it improves correctness/readability without losing block acquisition or timing behavior.
- Characterization tests are the bridge to deeper cleanup of acquisition, buffering, pacing, recovery, CRC/framing, and concurrency. Historical implementation details need not survive forever once required behavior is captured.
- Make eventual handoff possible without oral history: sharp-edge comments, tests demonstrating historical protocol peculiarities, clear reusable-engine/application-policy boundaries, and permanent architecture documentation where warranted.

## Current status

Implemented:

- modern .NET/C# Avalonia application; legacy WinForms preserved under tag `legacy-winforms`;
- currentized byte-native `SerialDevice`, incremental CRC, serial settings and data formatting;
- `TerminalSession` application boundary;
- authoritative byte-oriented `ReceivedData` with CRC bytes/status/error metadata;
- Communications and Transport Diagnostics workspaces;
- Send/Received separation, Enter-to-send, command history, strict binary-safe send parsing;
- asynchronous ordered `DiagnosticLog`;
- lazy automatic per-run disk logging with portable defaults and 25-file retention;
- active log filename display and diagnostic word-wrap control;
- CRC protocol controls, CRC-error suppression, Received CRC-byte display;
- characterized term-character and silence receive paths with fragmentation/recovery tests;
- immutable/staged protocol settings for safe compatible live updates;
- persisted/live timing and receive-silence controls;
- serial signal status and optional signal logging;
- diagnostic detail control explicitly separated from automatic logging;
- authoritative `TerminalConfiguration` bound by the UI, serializable to JSON, restored from the last session, and logged when changed;
- initial/effective configuration snapshots in diagnostic logs;
- self-contained Windows x64 publish profile;
- xUnit characterization suite covering send parsing, CRC, virtual responses, fragmentation, framing, recovery, protocol staging, and initial transport cleanup;
- `SerialConnectionState` and `SerialRecoveryPolicy` design types;
- `ISerialTransport` / `ISerialTransportFactory` production seam plus fake transport fault injection for deterministic lifecycle testing.

Lifecycle/recovery is **in progress**: the design/seam exists, but `SerialDevice` still has the inherited boolean lifecycle and teardown ordering described above. Do not mistake the presence of the state/policy types for completed integration.

Important limitations/deferred work are captured in the replacement-readiness and configuration sections above rather than maintained as a second competing list.

## Near-term priority

Priority is now driven first by **safe legacy replacement / Hacs parity**, then usability, then convenience:

1. Refactor `SerialDevice` teardown/ownership and disposal for safe, idempotent lifecycle behavior; integrate `ConnectionRequested` and `SerialConnectionState`; extend fake-transport tests. Stop for a build/test checkpoint when this coherent block is complete.
2. Add automatic transport-unavailable recovery/hot-plug reacquisition using fresh transport instances, configurable retry policy, resource-release invariants, and condensed repetitive diagnostics. Bad messages and isolated communications faults remain connected/observable rather than forcing recovery.
3. Revisit intentional `Reset()` semantics and remaining legacy parity items: compact counters, separate normalized data recording, standalone CRC displays, and Escape behavior.
4. Verify `RTS_CONTROL_TOGGLE` if a target field setup needs it.
5. Then improve usability: configuration flyout/profiles, scalable live views/follow-tail behavior, configuration help/tooltips, and other conveniences.
6. After the lifecycle/recovery refactor is complete, discuss and perform the deliberate project/application rename and coding-filename cleanup as one coherent identity change rather than piecemeal renaming.
