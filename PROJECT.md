# SerialBenchproject notes

> Temporary working handoff. Keep only context needed to resume development effectively plus decisions/deferred work that must not be forgotten. This is not project history; Git already serves that purpose. Delete this file when the project is release-ready and move any remaining work to GitHub issues.

## Current checkpoint

We are in the midst of carving the SerialCommunications library out of the application, which has been renamed to SerialBench.


## Architectural intent

There are two consumers of the intended reusable stack.

### SerialBench

SerialBench is evolving from a simple general-purpose COM terminal into an **interactive serial communications investigation and diagnostic instrument**. It remains able to send and receive arbitrary data manually, but is increasingly useful for diagnosing the communications system itself: exact wire evidence, framing/integrity, timing, recovery, signals, protocol configuration experiments, etc.

SerialBench is effectively a configurable **shape-shifter** that can assume the communications identity of many different serial devices while remaining ignorant of their application language. Compared with an AeonHacs hardware Component:

1. SerialBench is a complete application rather than one hardware abstraction.
2. Its configuration is changed live and it connects/disconnects on demand; a HacsComponent normally has one fixed configuration and system-managed lifetime.
3. SerialBench does not understand the remote device's command language; a concrete HacsComponent embeds that knowledge.
4. SerialBench exposes detailed diagnostic evidence for troubleshooting and profile development (for example: fastest reliable baud rate, whether a silence threshold helps, or why CRC fails); a HacsComponent is expected simply to operate and report meaningful failure.

A configured SerialBench instance and a concrete HacsComponent should ultimately be capable of reaching the same hardware through substantially the same underlying communications machinery. The human supplies the conversation semantics in SerialBench; code supplies them in the Component.

### AeonHacs and other automated consumers

The library must provide a standardized bridge between AeonHacs `SerialController`-style abstractions and physical hardware. The strong design goal is that **one configurable intermediate high-level communications class should suffice for essentially all normal serial-speaking Components**, rather than requiring each device class to reconstruct transport/framing/conversation machinery.

Survey examples include `HC6Controller`, `RS485Controller`, `ActuatorController`, `InductionFurnace`, `EurothermFurnace`, and all other materially distinct serial users found in AeonHacs.Components. Also inspect actual configuration/settings used by derived implementations where needed; base classes alone may hide important combinations.

The reusable stack may contain multiple internal layers; whether those layers become multiple assemblies is a later packaging decision. Prefer a simple consumer-facing model over a giant protocol DSL.

## Current lower-level architecture

The receive path is approximately:

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

Transport notification only signals work. It must return essentially immediately. Acquisition and deterministic decoding occur promptly; arbitrary application work is dispatched away from transport/reception paths.

Transport chunk boundaries have no protocol meaning. Protocol decoding must produce identical results for the same byte stream regardless of acquisition partitioning (whole frame, arbitrary splits, byte-at-a-time, multiple frames in one chunk, etc.).

`SerialDevice` currently coordinates transport ownership, TX pacing, recovery, protocol settings, receive-pipeline composition, signals, and low-level diagnostics. `TerminalSession` is the SerialBench application boundary above it. The serial engine is independent of Avalonia.

`TerminalConfiguration` is SerialBench's authoritative live configuration. The UI binds to it; sessions, diagnostics, and persistence consume it rather than interrogating controls.

## Transport lifecycle and recovery — settled

Keep these independent:

1. **Connection intent** — whether the caller wants the endpoint connected.
2. **Transport lifecycle** — connecting/connected/unavailable/disconnecting/disposed physical transport state.
3. **Communications health** — CRC failures, malformed/incomplete responses, timeouts, garbled data, isolated I/O failures, etc.

Bad communications do **not** imply loss of transport.

Core invariant:

> **Connection intent persists; ownership of a failed transport does not.**

`ConnectionRequested` represents intent. `SerialConnectionState` represents transport lifecycle. `Ready` requires `Connected` plus an open owned transport. Every recovery attempt creates a fresh transport; failed partial acquisition is fully released. Explicit `Disconnect()` cancels intent/recovery. `Dispose()` is idempotent.

`SerialRecoveryPolicy.Default` retries once per second. Repeated identical recovery failures are condensed diagnostically without slowing retry cadence.

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

## Framing, integrity, and data semantics — settled principles

Framing determines where a message ends; integrity determines whether it is valid. These are composable concerns. The library should support common framing/integrity combinations plus an escape hatch for unusual vendor protocols, rather than encode one giant protocol language.

Production Aeon field framing is:

```text
<payload><two-byte CRC><ETX>
```

Known Aeon CRC profile: initial `0xFFFF`, polynomial `0xDAAE`, expected residue `0x82C0`, post-invert enabled, ETX `0x03`. CRC excludes ETX. ETX is illegal in payload data but may legitimately occur in either CRC byte; `TrailerAwareTerminatorMessageFramer` handles this ambiguity.

The wire decoder removes only configured framing/integrity bytes. It does not silently normalize CR, LF, CRLF, NUL, spaces, or other content.

Supported/required configuration concepts include terminator without CRC, terminator after CRC, CRC and alternative integrity mechanisms such as BCC/LRC/XOR/additive forms where actual surveyed protocols require them, and silence framing. Acquisition coalescing is only a host optimization and is not a protocol boundary. Truly unframed streaming should be added only for a real use case.

A possible historical 5 ms minimum silence setting is not a requirement; determine whether any lower bound is technically justified and what actual AeonHacs configurations require.

Live protocol configuration must not require reconnect merely because framing/integrity changes. Current immutable `SerialProtocolSettings` and staged decoder machinery provide the basis. During the library design, verify the exact desired boundary semantics for a setting changed while completely idle: the next transmission/reception should not accidentally remain on obsolete settings merely because no old-style receive message occurred to trigger staging.

## Higher communications/conversation layer — survey before designing

Do not assume that the reusable boundary ends at decoded messages. The AeonHacs survey must determine what common conversation mechanics should be provided above framing/integrity so existing device abstractions can become near-drop-in consumers.

Look for, among other things:

- request/response association;
- command echo;
- ACK/NAK;
- unsolicited messages;
- multi-message responses;
- addressing and shared buses (including RS-485 patterns);
- prompts or other conversational framing;
- half-duplex/turnaround timing;
- timeouts and protocol-level retries;
- polling/datalogging patterns;
- sequence/request IDs;
- message encoding and decoding boundaries;
- device-specific exceptions that test the common abstraction.

The reusable layer should know conversation mechanics where useful, not instrument meaning. A Component may know that a particular command means "read pressure"; the stack may know that the transaction is send -> echo -> response with a timeout.

Timeouts/protocol retries and malformed responses are communications-health evidence, not automatic proof of transport loss.

## Diagnostics, events, and telemetry

Responsibility boundary:

> **The library provides communications mechanisms, state, events, and evidence. SerialBench decides what a human sees, records, summarizes, resets, or does with them.**

Revisit existing diagnostic messaging before extraction. Development accumulated useful but possibly excessive narration. Prefer lower layers exposing facts/evidence once, with upper layers deciding presentation, rather than several layers logging the same event in different words. Raw wire evidence and interpreted conversation events must remain separable.

Treat **diagnostic/event surface** (what happened, when, with evidence) separately from **telemetry surface** (current/accumulated measurements derived from operation).

Telemetry requires a deliberate design pass and likely belongs primarily in the library. Guiding criterion: expose measurements/state that are **(A) cheap and potentially useful and (B) internal information/state/conditions that would otherwise be difficult for a consumer to obtain**. Inventory existing counters/flags/timing before deciding what survives. Define semantics, scope/lifetime, reset behavior, and consumer access deliberately rather than preserving legacy counters by inertia.

Candidates only, not commitments: TX count, decoded/received message count, response/conversation counts if that concept exists at the relevant layer, timing/averages, integrity failures, other errors, silence observations, recovery attempts/duration, bytes transferred, etc. Keep operational state such as connection state distinct from telemetry.

Legacy `ErrorCrc`, `CRCErrors`, compact counters, standalone TX/RX CRC displays, reset-statistics behavior, and similar historical reporting are absorbed into this telemetry review rather than being preserved independently.

Legacy application-buffer `ErrorBufferOverflow` / `BufferOverflows` are obsolete: the old application ring buffer is gone. Remove rather than reproduce them. A genuine OS/driver overrun reported by a transport would be a distinct diagnostic condition.

Legacy generic `Reset()` is also obsolete. Automatic transport recovery replaces its historical recovery role; explicit disconnect/reconnect and object recreation already cover those meanings. If clearing telemetry is useful, define that with the telemetry suite. Physical/device reset belongs at the device/protocol layer.

## Configuration and profiles

The reusable library should provide the configuration model needed to describe communications behavior. SerialBench owns live editing, named-profile storage/persistence, UI defaults, and profile management.

Do not make Aeon CRC values magic library defaults. They may be supplied as an Aeon profile/configuration. Ensure the model can express every materially distinct configuration found in AeonHacs.Components and real derived implementations.

SerialBench should eventually support a connection/profile name and use it in the UI/title (for example `COM3 - HC6`).

## SerialBench-specific deferred work

These are application concerns, not reasons to contaminate the reusable library:

- **Normalized data recording:** SerialBench should provide it, but transformation/normalization/recording is application responsibility; the library supplies faithful data/evidence.
- **Command history / Escape behavior:** UI/application behavior; revisit together with broader command-entry usability.
- **Received-data presentation:** decide escaped/displayed/interpreted treatment of CR/LF/newline, tab, BEL and other control characters; possible selectable BEL sound; decide visual message separation. Presentation must never alter authoritative data.
- **UI density/help:** reduce excessive control size/spacing; add useful hover/tooltip help. Command entry should convey `Enter sends. Up/Down recalls sent commands.` Avoid redundant help.
- **Diagnostic presentation terminology:** reconsider `Transport Diagnostics`, `Diagnostic Trace`, `Log`, etc. The authoritative disk record and live diagnostic view should not be confused.
- **Live-view scalability:** current `TextBox.Text +=` behavior is O(N²); replace with append-friendly bounded presentation while retaining authoritative disk evidence.
- **Configuration UI:** permanent left pane remains provisional; flyout/drawer remains favored later.
- **Profiles:** store/edit/select named profiles and connection names after the reusable configuration model is settled.
- **Portable deployment:** validate the self-contained package on a clean Windows machine. If installed under Program Files, reconsider writable settings/log placement.

## Other deferred library/transport work

- Verify RTS Toggle behavior with current .NET/Windows and representative adapters only if a target setup requires it. Do not restore reflection into private `SerialPort` internals.
- Decide bounded dispatch/backpressure policy if a chronically blocked consumer is a realistic use case. Consumer work must never block acquisition/decoding, but an unbounded queue may be undesirable.
- Add genuinely unframed-stream support only if a surveyed protocol/application requires it.
- Review protocol-settings association/staging and any dead reconnect-required UI branch after the architecture is settled.

## Naming and repository identity — deliberately deferred

The accumulated naming words are **character cues, not proposed names**.

Application cues such as *detective / doctor / analyzer / tester / spy / COM Spook* express the application's changed character: interactive investigation, diagnosis, experimentation, and manual communication. Avoid a name that implies only passive sniffing or only a simple terminal.

Library cue *Serial Device Comm Stack* expresses infrastructure: reusable hardware-intimate communications machinery, not diagnostics. The library should be a natural dependency of both SerialBench and AeonHacs, not sound like merely "SerialBench's engine."

Working briefs:

- **Application:** an interactive serial communications investigation and diagnostic instrument.
- **Library:** a reusable layered serial-device communications stack, from transport ownership through whatever common protocol/conversation services the AeonHacs survey justifies.


## Deployment

Tracked Windows x64 self-contained publish profile:

```text
Properties/PublishProfiles/FolderProfile.pubxml
```

Publish command:

```text
dotnet publish SerialBench.csproj -p:PublishProfile=FolderProfile
```

## Development rules

- Work directly on `main` unless explicitly directed otherwise; use small coherent commits.
- Prefer one revised file per commit. Use distinct commit messages for independently meaningful changes; use `(1/N)`, `(2/N)`, etc. when several one-file commits are one conceptual change forced apart by tooling.
- Re-fetch current files immediately before writing; do not work from stale snapshots.
- Keep communications behavior testable without Avalonia.
- Preserve characterized behavior while modernizing structure.
- Correctness, explicit semantics, and resource ownership take priority over convenience or cosmetic cleanup.
- Avoid naive/O(N^2) algorithms where scale can matter.
- Keep GUI-specific behavior in the application layer.
- Do not stop for user testing after every small commit when the next steps are clear; proceed through coherent work and ask for testing at useful checkpoints.
- Do not claim tests ran unless they actually ran; user local testing is authoritative and faster than waiting on CI.
- Keep this file concise and forward-looking. Completed implementation history belongs in Git.
