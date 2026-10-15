# SerialPortTerminal project notes

> Temporary working handoff. Keep only context needed to continue development and decisions/deferred work that must not be forgotten. Do not use this as project history. Delete it when the application is release-ready and move remaining work to GitHub issues.

## Current checkpoint

The existing Serial Port Terminal behavior is implemented on the decomposed serial architecture. Lifecycle/recovery, the receive pipeline, CRC/framing, live protocol staging, TX, diagnostics, and the application boundary are in place and characterized by tests. Real Windows USB loopback and unplug/replug recovery have also been exercised successfully.

**Stop here before structural identity changes.** The next step is a design discussion that deliberately converges:

- the reusable communications-library boundary;
- the library name;
- the application name;
- converting the repository from a fork to a standalone project;
- the resulting project/solution/namespace layout.

Do not extract the library, rename projects, or detach the fork before that discussion.

## Architecture

SerialPortTerminal is both an interactive serial diagnostic/investigation tool and the proving ground for reusable lower-level serial communications machinery. Diagnostic evidence is product functionality: preserve exact TX/RX bytes, CRC/framing evidence, timing, connection events, pin changes, counters, recovery, and related diagnostics rather than hiding them behind application abstractions.

The receive path is:

```text
transport notification
    -> ReceivePump
    -> SerialReceiveAcquirer
    -> IMessageDecoder
       -> IMessageFramer
       -> IMessageIntegrity
    -> MessageDispatchQueue
    -> SerialDevice / consumer
```

Transport notification only signals work. Acquisition and deterministic decoding are isolated from arbitrary consumer/application work. Transport chunk boundaries have no protocol meaning; decoding must be invariant to how the same byte stream is partitioned into acquisition chunks.

`SerialDevice` currently coordinates transport ownership, TX pacing, recovery, protocol settings, receive-pipeline composition, signals, and low-level diagnostics. `TerminalSession` is the application boundary above it. The serial engine remains independent of Avalonia.

`TerminalConfiguration` is the authoritative live application configuration. The UI binds to it; sessions, diagnostics, and persistence consume it rather than interrogating controls.

## Transport lifecycle and recovery — settled

Keep three concepts independent:

1. **Connection intent** — whether the caller/user wants the configured endpoint connected.
2. **Transport lifecycle** — whether an actual OS serial transport is connecting, connected, unavailable, disconnecting, or disposed.
3. **Communications health** — CRC failures, malformed/incomplete responses, timeouts, garbled data, isolated read/write failures, etc.

Bad communications do **not** imply loss of transport.

Core ownership invariant:

> **Connection intent persists; ownership of a failed transport does not.**

`ConnectionRequested` represents intent. `SerialConnectionState` represents transport lifecycle. `Ready` requires `Connected` plus an open owned transport. Every recovery attempt creates a fresh transport; failed partial acquisition is fully released. Explicit `Disconnect()` cancels intent/recovery. `Dispose()` is idempotent.

`SerialRecoveryPolicy.Default` retries once per second. Repeated identical failures are reported individually for the first three, then once as continuing recovery attempts; further identical failures are silent until the result changes. Retry cadence is not slowed by diagnostic condensation.

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

## Receive framing and integrity — settled core behavior

Aeon field framing is:

```text
<payload><two-byte CRC><ETX>
```

Production Aeon CRC characterization includes polynomial `0xDAAE`, initial value `0xFFFF`, expected residue `0x82C0`, post-invert enabled, and ETX `0x03`. CRC excludes ETX. ETX is illegal in payload data, but either CRC byte may legitimately equal ETX.

`TrailerAwareTerminatorMessageFramer` handles that ambiguity. Framing determines where a message ends; integrity determines whether it is good. Integrity is consulted by the framer only where the fixed-width trailer makes a terminator byte ambiguous.

The wire decoder removes only bytes identified by the configured protocol as framing or integrity fields. It does **not** strip CR, LF, CRLF, NUL, spaces, or other ordinary content.

Silence framing is supported as an explicit framing strategy. Acquisition coalescing is only a host optimization and is not a message boundary. Truly unframed streaming remains a possible future feature rather than something the decoder should invent.

Live protocol changes use immutable `SerialProtocolSettings`. A staged decoder/settings snapshot is adopted at the next completed receive-message boundary, including when bytes for the following message are already present in the same acquisition chunk. Changes between silence framing and CRC+terminator framing therefore do not require reconnecting.

## Diagnostics and UI semantics

Communications shows the logical conversation; Transport Diagnostics shows evidence of how it was transported. The disk log is the authoritative ordered diagnostic record; the UI is a live view/tail.

Automatic session logging is independent of diagnostic detail. Baseline connection state, errors, commands, responses, recovery, etc. remain logged even when detailed transport events are disabled. Signal logging is separately selectable.

Presentation rules:

- use **Send** and **Received**, not command/response assumptions;
- always show unsolicited received traffic;
- display choices never alter authoritative received data;
- Text mode is escaped-safe; Bytes mode is byte-oriented;
- recognized framed messages begin distinct display entries independent of payload CR/LF;
- do not yank a deliberately scrolled-up view back to the latest entry.

Deferred UI work: current `TextBox.Text +=` live views are O(N²); replace them with append-friendly bounded presentation while retaining the complete disk log. The permanent left configuration pane is provisional; a flyout/drawer remains favored later.

## Deferred decisions / future work

These are not blockers for the serial-framework checkpoint:

- Decide the legacy replacement details still worth restoring: Reset/display-counter semantics, compact counters, standalone TX/RX CRC displays, separate normalized data recording, and Escape behavior in command history.
- Verify RTS Toggle with current .NET/Windows and representative adapters if a target setup requires it. Do not restore reflection into private `SerialPort` internals.
- Consider whether legacy `ErrorBufferOverflow` / `BufferOverflows` surface still has value now that the old application ring buffer is gone.
- Consider whether `ErrorCrc` should represent the last message or remain sticky diagnostic state; `CRCErrors` is the cumulative evidence counter.
- Consider bounded dispatch/backpressure policy if a chronically blocked consumer becomes a realistic use case.
- Add genuinely unframed-stream support only if a protocol/application requires it.
- Improve UI scalability/usability and validate the self-contained portable package on a clean Windows machine.

## Deployment

The tracked Windows x64 self-contained publish profile is `Properties/PublishProfiles/FolderProfile.pubxml`:

```text
dotnet publish SerialPortTerminal.csproj -p:PublishProfile=FolderProfile
```

Portable deployment remains preferred for the immediate replacement milestone. Installing under Program Files would require reconsidering beside-the-executable writable settings/logs.

## Development rules

- Work directly on `main` unless explicitly directed otherwise; use small coherent commits.
- Re-fetch current files immediately before writing.
- Keep serial/application behavior testable without Avalonia.
- Preserve characterized behavior while modernizing surrounding structure.
- Correctness and resource ownership take priority over convenience or cosmetic cleanup.
- Keep this file concise; completed implementation history belongs in Git, not here.
