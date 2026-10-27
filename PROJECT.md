# SerialBench project notes

> Development handoff for unresolved work and easy-to-lose constraints. Settled user-facing behavior belongs in code, tests, XML documentation, and README.md; completed history belongs in Git.

## Project boundary and working layout

SerialBench is an interactive serial communications investigation/diagnostic application. `SerialCommunications` is the completed reusable communications foundation used by SerialBench and intended for automated consumers such as AeonHacs.

For source development the repositories are expected to be sibling directories. `SerialBench.csproj` references `../SerialCommunications/SerialCommunications.csproj`, and `SerialBench.sln` includes that sibling project. SerialBench CI must likewise check out SerialCommunications as a sibling while this source-level reference remains in use.

`SerialCommunications` owns faithful serial mechanisms: port configuration, transport lifecycle/recovery, connection intent/state, pacing, receive acquisition, framing/checking, byte-oriented delivery, diagnostics, telemetry, and reusable byte/check helpers. SerialBench owns interactive presentation, command entry/history, configuration editing, persistence/profiles, session-recording policy, diagnostic presentation/filtering, UX, and deployment behavior. Instrument consumers own command/response meaning.

Keep connection intent, transport availability, and communications health independent. Do not add library features merely to make SerialBench convenient; a library change requires a demonstrated reusable communications requirement.

## Agreed design rules

- `ReceivedData` is authoritative byte-oriented evidence. Text decoding and presentation belong to SerialBench; recording and visual rendering are derivatives and must not alter or obscure the bytes.
- Live protocol changes apply at receive-message boundaries through the library's staged protocol settings.
- SerialBench exposes the complete `FrameLayout` model plus literal start/end sequences and optional message check. Its historical default is `PCE`, CRC-16, end `\x03`.
- SerialBench alone owns `ByteExpression`: printable ASCII literal; `\\`, `\r`, `\n`, `\t`; all other bytes `\xHH`; no octal. Arbitrary byte arrays round-trip exactly.
- `SerialChannelTelemetry.MessagesReceived` counts decoded/framed messages, including invalid messages suppressed from consumer delivery. `CheckFailures` is independent.
- A logical transmit trace is emitted only after the complete message has been written successfully.
- Subscriber exceptions are consumer faults, not communications errors; isolate them and report only as detailed diagnostics.
- `TraceFlags.Detail` and `TraceFlags.Noisy` are independent inclusion categories.
- Consumer dispatch remains asynchronous and deliberately unbounded. Bounded admission belongs above the faithful communications layer.
- Deliberate transport cycling uses `Disconnect()` / `Connect()`; communications-health failures do not imply transport loss.
- Request/response orchestration stays above `SerialCommunications`.
- Configuration ownership is split by role: reusable library snapshots below, live editing/persistence/profiles/UI defaults in SerialBench.
- Receive decoder/framer/pump/dispatch types remain implementation details. Message-check calculations remain reusable public mechanisms.

## Current checkpoint: session store and scalable presentation

Full framing support is complete and was locally verified with 22/22 tests green. Phase 1 now replaces unbounded `TextBox.Text +=` history with an append-only session journal plus bounded rendered views.

### Session file format

The session file is an append-only journal. It is intended primarily to make arbitrarily long current activity manageable, but files may also be reopened and continued.

Header, version 1.0:

```text
Offset  Size  Field
0       4     ASCII "SBSS"
4       1     MajorVersion = 1
5       1     MinorVersion = 0
```

All multibyte integer fields are explicitly little-endian independent of host platform. The application targets .NET/Avalonia on Windows and Linux. Version bytes have major/minor compatibility semantics.

Every record begins:

```text
RecordType       UInt8
RecordLength     Int32   // total record length including RecordType
TimestampUtc     Int64   // UTC DateTime ticks, 100 ns resolution
```

Record types are distinct even though Tx/Rx currently share framing configuration: `TxMessage`, `RxMessage`, and `Diagnostic`. Do not store event IDs/counters; chronological file order is the durable event identity.

`TxMessage` fixed fields are `PrefixLength UInt8`, `PayloadLength Int32`, `SuffixLength UInt8`, followed by prefix, payload, suffix. Fixed size including common introduction is 19 bytes.

`RxMessage` adds `CheckValid UInt8` before those lengths. Encoding is `0 = invalid`, `1 = valid`, `2 = ignore/not applicable`; thus ordinary false/true semantics mean problem/no problem. Fixed size is 20 bytes.

`Diagnostic` is the 13-byte common introduction followed directly by UTF-8 text; `RecordLength` determines text length. No BOM is stored.

Prefix means every wire byte before payload; suffix means every wire byte after payload. Thus the full wire message is exactly `prefix + payload + suffix`, without duplicate storage. Communications presents Rx payload only. Trace can reconstruct complete Tx/Rx wire evidence. With start layouts, pre-start bytes retained by the receive decoder naturally belong to the Rx prefix.

Checks cover payload according to the protocol configuration, but the session record only needs the tri-state check result. Rich check-failure evidence belongs in Diagnostic records emitted by `SerialCommunications` before invalid-message suppression.

### Session indexing, recovery, and lifetime

A forward scan rebuilds disposable indexes:

```text
eventOffsets[]       // file offset of every record
rxMessageEvents[]    // event ordinal for every RxMessage
```

Trace indexes through `eventOffsets`. Received indexes through `rxMessageEvents -> event ordinal -> eventOffsets`. No Tx secondary index is required.

Validation is deliberately modest. Require a defined record type, a record length at least large enough for that type, component lengths exactly consistent with the record length for message records, and enough bytes for the complete record. No per-record checksum, error correction, or resynchronization search is required.

An obviously incomplete final record at EOF is presumed to be an interrupted append and may be truncated automatically to the last complete record. A structurally corrupt record that is not merely an incomplete final append must not cause automatic destruction of the remaining file; stop normal scanning and preserve the file for possible recovery. A recovery facility is not presently required.

One `SessionStore` owns the current file and is always the current writable journal. Startup creates a new session. `File > New` closes the current session and creates another. `File > Open` closes the current session and makes the selected existing file the current session. Merely browsing an opened file writes nothing; subsequent recordable activity simply appends to that file. A separate historical/read-only application mode is unnecessary. `File > Close` is semantically equivalent to New and need not exist separately.

The store uses buffered writes and periodic ordinary flushes, nominally about one second, plus orderly flush on close/shutdown. Do not force durable physical-media flush for every record. Losing the final short interval to machine failure is acceptable. Newly appended records remain available in a small record-oriented in-memory pending tail until flushed, so live UI reads do not force a disk flush merely for read-after-write coherence.

### Communications and Trace semantics

Communications answers: "What conversation took place with the instrument?" Received presentation is semantic and uncluttered: payload only, with Text and Hex as alternate projections of exactly the same stored bytes.

Trace answers: "What actually happened in the communications machinery?" It includes Tx and Rx wire evidence plus diagnostics such as acquired/discarded bytes, framing/check results, timeouts, connection transitions, errors, and configuration changes. Raw/framing bytes should be unambiguous hex. Diagnostic text is recorded exactly as supplied rather than reinterpreted later.

Both views are projections of one chronological session sequence; neither owns history. Cross-view navigation may later use the common event ordinal.

Normal displayed timestamps use `HH:mm:ss.fff`; the journal retains full 100 ns UTC tick precision.

### Bounded presentation windows

The session journal, not the Avalonia TextBox, owns history. Each view keeps only a bounded record buffer behind the scenes and renders that relatively small buffer to a string assigned to an ordinary TextBox.

Received keeps a window of Rx records and renders payloads as either strict hex bytes or canonical `ByteExpression` text. Trace keeps a window of all event types and has one diagnostic/wire-oriented rendering path. Changing Text/Hex, Word Wrap, or the staged range rebuilds only the current bounded string. These operations and window refills must feel instantaneous; tune the working-set sizes empirically to preserve that requirement.

Do not impose a rigid target size that causes churn during ordinary appending. A buffer may contain zero records and should be allowed to grow through a broad comfortable range before trimming/shifting. When the visible range approaches an edge, preserve useful overlap and fetch/render only the newly required records. Near file/session boundaries, naturally bias the window rather than insisting on exact centering.

Collect presentation tuning constants such as minimum/nominal/maximum retained records, fetch size, and edge clearance together in one internal class (for example `SessionPresentationLimits`). These are performance knobs, not file-format invariants, and changing them must not affect stored data.

Scrolling is local/pixel-oriented within the TextBox even though staging is record-oriented; records may span arbitrary numbers of rendered lines. The logical refresh anchor is `(projection record ordinal, payload byte offset)` plus any transient viewport offset needed by Avalonia to preserve apparent vertical position. This survives Text/Hex and Word Wrap changes without global rendered-line accounting.

Tail following is visibility-based: if the end of the last current record is visible when new material arrives, keep following and scroll enough to show the new end. If the end is not visible, capture continues but the viewport must not move. Do not infer tail-follow state merely from total record count or a nominal scrollbar position.

### Data Visualizer logging

The separate operator-controlled data log remains a live receive sink, not a session-file projection. Start/Stop affects future Rx messages only; no retrospective dump. Log received payload only, one record per Rx message, with actual reception timestamp. Trim leading/trailing whitespace, collapse internal whitespace runs to one literal TAB, prepend timestamp, and terminate normally. It is independent of Communications display mode; sent messages and session diagnostics are never included.

### Communications UX decisions queued behind the scalable path

- Remove old Reset and Clear Communications/Trace semantics; session history is authoritative. Command history gets its own clear operation.
- Command history default depth is 100 and bounded/configurable; exact recall/edit/replay state-machine behavior remains to be designed deliberately.
- Remove the old permanent CRC/check result field. Hovering the Send textbox should show the check bytes that would be generated for the current payload/configuration, in wire order as space-separated hex.
- Received gets a red check-failure indicator only when the attempted response fails its check; sending clears the prior warning. It must work even when invalid payload delivery is suppressed.
- Keep gross connection/channel state visible during ordinary Communications use; exact placement remains open.
- Drop predecessor counters from Communications. Channel telemetry may later appear as optional statistics with independent reset.

## Planned later work

### Phase 2 — configuration and workflow

Define named profiles / connection names and their lifecycle, persistence semantics, then redesign the configuration UI around that workflow. A flyout/drawer is favored over permanently dedicating substantial main-window space to configuration. Do not design the pane first and force profiles into it.

Distinguish physical port settings, protocol/framing settings, genuine device-required transmit pacing, empirically tuned receive-silence values, and host-side polling/throttling policy. Device-required pacing belongs in communications configuration; host polling frequency does not.

### Phase 3 — diagnostics and general UX

After terminal and configuration structures stabilize, clarify live diagnostics versus authoritative recording, reduce visual density, add contextual help/tooltips, improve terminology/labels, and perform product-facing naming cleanup, including the historical main-window title `Serial Port Terminal` -> `SerialBench`.

### Phase 4 — deployment

Validate self-contained Windows deployment on a clean machine and Linux deployment as appropriate: startup, dependencies, writable configuration/session locations, persistence, and serial-port access. Absent newly discovered defects or requirements, completion is the SerialBench 1.0 functional-completeness checkpoint.

## Related SerialCommunications work

Treat `SerialCommunications` as complete for SerialBench development except where a demonstrated reusable requirement is found. Deferred library work is tracked there, including RTS Toggle/physical qualification, incomplete-frame timeout behavior, bounded-wait audit, and cleanup of receive-check diagnostics/API naming. In particular, future generic terminology should use check bytes/check code rather than CRC where the concept is not inherently CRC-specific.

Future asymmetric Tx/Rx framing may be useful for devices whose commands and responses differ. Do not implement it now, but keep the session taxonomy direction-neutral by retaining distinct Tx/Rx record types.

## Development rules

- Work directly on `main`; use small coherent commits and re-fetch files immediately before writing.
- Commit each tangible cohesive change, but group many such commits between user build/test checkpoints. Stop for build/test only after a substantial change.
- Keep communications behavior testable without Avalonia and GUI behavior in the application layer.
- Preserve characterized behavior while modernizing structure; correctness and explicit ownership/semantics outrank convenience.
- Avoid naive O(N²) work where scale can matter.
- Do not add speculative abstractions or configuration for imagined future devices.
- Proceed autonomously through coherent work; ask for user testing at useful checkpoints rather than after every commit.
- Do not claim tests ran unless they actually ran.
