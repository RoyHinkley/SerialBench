# SerialBench project notes

> Development handoff for unresolved work and easy-to-lose constraints. Settled user-facing behavior belongs in code, tests, XML documentation, and README.md; completed history belongs in Git.

## Project boundary and working layout

SerialBench is an interactive serial communications investigation/diagnostic application. `SerialCommunications` is the completed reusable communications foundation used by SerialBench and intended for automated consumers such as AeonHacs.

For source development the repositories are expected to be sibling directories. `SerialBench.csproj` references `../SerialCommunications/SerialCommunications.csproj`, and `SerialBench.sln` includes that sibling project. SerialBench CI must likewise check out SerialCommunications as a sibling while this source-level reference remains in use.

`SerialCommunications` owns faithful serial mechanisms: port configuration, transport lifecycle/recovery, connection intent/state, pacing, receive acquisition, framing/checking, byte-oriented delivery, diagnostics, telemetry, and reusable byte/check helpers. SerialBench owns interactive presentation, command entry/history, configuration editing, persistence/profiles, session-recording policy, diagnostic presentation/filtering, UX, and deployment behavior. Instrument consumers own command/response meaning.

Keep connection intent, transport availability, and communications health independent.

Do not add library features merely to make SerialBench convenient. A `SerialCommunications` change requires a demonstrated reusable communications requirement.

## Agreed design rules

- `ReceivedData` is authoritative byte-oriented evidence. Text decoding and presentation belong to SerialBench; normalized recording and visual rendering are derivatives and must not alter or obscure the received bytes.
- Live protocol changes apply immediately when the receive decoder has no incomplete message. If bytes are already buffered, the old decoder completes that message and the staged decoder takes over exactly at its boundary, including when following bytes occur in the same transport chunk.
- Current `SerialCommunications` framing is expressed by `FrameLayout` plus literal start/end sequences and an optional reusable message check. SerialBench's older CRC/termination model must be migrated to this API rather than preserved as an application-specific approximation.
- `SerialChannelTelemetry.MessagesReceived` counts decoded/framed messages, including invalid messages suppressed from consumer delivery. `CheckFailures` records the independent failed-check fact. Recovery-period attempt state remains distinct from lifetime telemetry.
- A normal logical transmit trace is emitted only after the complete message has been written successfully. Failed writes are errors but are neither counted nor narrated as completed transmissions.
- Exceptions thrown by `SerialChannel` event subscribers are consumer faults, not communications errors; isolate them and report only as detailed diagnostics.
- `TraceFlags.Detail` and `TraceFlags.Noisy` are independent inclusion categories in SerialBench. A trace carrying both is shown when either corresponding option is enabled.
- Consumer dispatch remains asynchronous and deliberately unbounded. The library must not stall serial reception or silently discard decoded messages because application code is slow. Consumers with a genuine bounded-admission requirement impose that policy above the faithful communications layer. Disposal remains non-blocking with respect to arbitrary consumer code.
- Deliberate transport cycling is expressed explicitly with `Disconnect()` / `Connect()`. Communications-health failures do not implicitly mean transport loss.
- Request/response orchestration stays above `SerialCommunications`. Do not add a generic conversation/protocol DSL without concrete common behavior.
- Configuration ownership is split by role: `SerialChannelConfiguration` / `SerialProtocolSettings` are reusable library snapshots; SerialBench owns live editing, persistence, profiles, and UI defaults.
- Receive decoder/framer/pump/dispatch types are implementation details rather than public library API. Message-check calculations remain reusable public mechanisms.

## Immediate work: synchronize to current SerialCommunications API

Before further SerialBench feature work, migrate the application to the current `SerialCommunications` API and preserve the application's existing useful behavior where that behavior still maps cleanly to the library.

The known mismatch is the protocol configuration model. SerialBench still constructs the former `CrcOptions` shape containing termination-character/omit-termination behavior and then uses `SerialProtocolSettings.From(...)`. The library now separates CRC calculation from framing: `CrcOptions` contains only CRC calculation/byte-order options, while `SerialProtocolSettings` owns `FrameLayout`, start/end byte sequences, check kind, and invalid-message suppression.

The migration should therefore:

1. replace the obsolete SerialBench CRC/termination assumptions with explicit framing/check configuration that maps faithfully to `SerialProtocolSettings`;
2. preserve the historical SerialBench default wire behavior where representable (payload + CRC + terminating byte with the terminating byte omitted from delivered payload corresponds to `PCE` with CRC-16 and the configured end sequence);
3. update live protocol editing/staging to the current snapshot API;
4. update received-data presentation code to the current `ReceivedData` representation;
5. add/adjust non-Avalonia tests for configuration-to-library mapping where useful;
6. build/test before moving on to Phase 1 proper.

Treat compile errors and API mismatches discovered during this migration as SerialBench adaptation work unless they demonstrate a genuine reusable-library defect or missing requirement.

## Planned SerialBench work

### Phase 1 — terminal data path

Order matters:

1. replace `TextBox.Text +=` live output with bounded, append-friendly presentation; the current implementation becomes O(N²) as history grows;
2. improve received-data presentation, including CR/LF, tabs, BEL, message separation, and printable versus escaped/non-printable data;
3. revisit command history and Escape behavior for efficient repeated investigation;
4. define normalized/session recording while preserving the distinction `authoritative bytes -> recorded representation -> live presentation`.

Where practical, extract non-Avalonia transformations/models into testable classes rather than embedding them in UI handlers.

### Phase 2 — configuration and workflow

1. define named profiles / connection names and their lifecycle: select, create, duplicate, rename, modify, save/revert, and unsaved-change indication;
2. establish persistence semantics;
3. redesign the configuration UI around that workflow. A flyout/drawer is favored over permanently dedicating substantial main-window space to configuration.

Do not design the configuration pane first and then force profiles into it.

Relevant RoyHacs retrofit findings should inform terminology and distinctions without mechanically reproducing historical AeonHacs concepts. In particular, distinguish physical port settings, protocol/framing settings, genuine device-required transmit pacing, empirically tuned receive-silence values, and host-side polling/throttling policy. Receive-silence values such as 5/10/15/20 ms may be tuning history rather than different protocol definitions. Device-required pacing belongs in communications configuration; host polling frequency does not.

### Phase 3 — diagnostics and general UX

After terminal and configuration structures stabilize, clarify live diagnostics versus authoritative recording, reduce visual density, add concise contextual help/tooltips, improve terminology/labels, and perform product-facing naming cleanup. This includes changing the historical main-window title `Serial Port Terminal` to `SerialBench`.

### Phase 4 — deployment

Validate self-contained Windows deployment on a clean machine: startup, runtime/dependencies, writable configuration and log/session locations, persistence, and normal serial-port access. Absent newly discovered defects or requirements, completion is the SerialBench 1.0 functional-completeness checkpoint.

## Related SerialCommunications work

Treat `SerialCommunications` as complete for SerialBench development. Library-specific deferred work remains in that repository, notably RTS Toggle implementation/qualification and broader physical hardware qualification. Do not let those items drive SerialBench architecture prematurely.

## Development rules

- Work directly on `main`; use small coherent commits and re-fetch files immediately before writing.
- Complete coherent groups of small commits between user build/test checkpoints rather than stopping after every bite-sized change.
- Keep communications behavior testable without Avalonia and GUI behavior in the application layer.
- Preserve characterized behavior while modernizing structure; correctness and explicit ownership/semantics outrank convenience.
- Avoid naive O(N²) work where scale can matter.
- Do not add speculative abstractions or configuration for imagined future devices.
- Proceed autonomously through coherent work; ask for user testing at useful checkpoints rather than after every commit.
- Do not claim tests ran unless they actually ran.
