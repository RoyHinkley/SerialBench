# SerialBench

SerialBench is a Windows desktop workbench for communicating with serial devices. It is intended for bringing up unfamiliar equipment, testing commands, investigating protocol behavior, and diagnosing serial links without writing a device-specific application first.

The application lets you configure the serial connection and framing/check rules, send arbitrary byte-oriented commands, inspect received data, watch modem/control signals, and examine detailed transport diagnostics. The reusable communications engine is maintained separately in [SerialCommunications](https://github.com/RoyHinkley/SerialCommunications).

## What SerialBench can do

- Configure port, baud rate, data bits, parity, stop bits, handshake, and RTS behavior.
- Pace transmissions between messages or between individual bytes.
- Receive messages using terminator-based or silence-based framing.
- Configure CRC-16 handling and suppress messages that fail their configured CRC check.
- Enter and display serial data without losing the distinction between text and arbitrary byte values.
- Display RTS, CTS, DTR, DSR, DCD, and RI signal states.
- Show ordinary communications separately from detailed transport diagnostics.
- Maintain an automatic diagnostic session log.
- Recover automatically when a requested serial connection becomes temporarily unavailable.
- Persist application/configuration settings between sessions.

SerialBench is deliberately protocol-neutral. It does not try to decide what a command means, which response belongs to which request, whether a device should be polled, or when an instrument-level operation should be retried. Those policies belong in device-specific software.

## Using SerialBench

### 1. Configure the connection

Select the serial port and the device's baud rate, data bits, parity, stop bits, handshake, and RTS mode in the **Connection** section. Use the refresh button beside the port list after attaching or removing a USB serial adapter.

The connection settings are locked while connected. Disconnect before changing them.

### 2. Configure framing and timing

The **Timing** section controls delays between transmitted messages and bytes and the maximum receive silence used when silence framing is selected. A transmit pacing value of `-1` disables that pacing option.

The **Protocol** section provides the current CRC and framing controls. For CRC protocols, enter the polynomial, initial value, expected residue, byte/bit ordering, and other settings required by the device. Select **No term character (silence framed)** when a message ends by silence rather than a terminating byte.

These settings describe transport/message mechanics; they do not encode the higher-level semantics of a particular instrument protocol.

### 3. Connect and send data

Click **Connect**, then enter a command in the **Send** field. Press **Enter** or click **Send** to transmit it. Up/Down recalls previously sent commands.

SerialBench treats the communications boundary as bytes rather than assuming that every device speaks ordinary text. Literal text entry is ASCII; arbitrary byte values can be represented with escapes such as `\x03` or `\xE9`.

### 4. Inspect received data

The **Communications** tab shows received messages in the selected display format. The received-data controls determine presentation and whether CRC bytes are shown. **Clear** clears the displayed history; it does not alter the serial device or connection.

### 5. Diagnose the link

The **Transport Diagnostics** tab shows how traffic is transported and processed. Enable **Include detailed transport events** when investigating low-level behavior; ordinary use generally does not need the extra detail. **Log signals** includes signal-state changes in the diagnostic record.

The diagnostic log continues independently of whether detailed transport events are displayed. The log-file field shows the active log location.

## Building from source

SerialBench and SerialCommunications are independent repositories but currently use a source-level project reference. Clone them as sibling directories:

```text
<source-root>/
    SerialBench/
    SerialCommunications/
```

Both projects currently target .NET 10. From the `SerialBench` directory:

```text
dotnet restore SerialBench.sln
dotnet build SerialBench.sln
dotnet test SerialBench.sln
```

The sibling `SerialCommunications` repository must be present because `SerialBench.csproj` references `../SerialCommunications/SerialCommunications.csproj` and the solution includes that project.

## Communications model

SerialBench and SerialCommunications intentionally distinguish three different facts:

1. **Connection intent** — whether the user has requested a connection.
2. **Transport availability** — whether the serial transport is actually connected and usable.
3. **Communications health** — whether messages are timely and valid.

A malformed response, failed message check, timeout, or isolated I/O problem is useful diagnostic evidence, but it does not by itself mean that the physical serial transport has disappeared.

Received data is likewise byte-oriented. SerialCommunications preserves the wire and payload bytes and applicable check evidence; SerialBench converts that evidence into human-readable displays and logs.

## Provenance

SerialBench continues the project formerly maintained as `RoyHinkley/SerialPortTerminal`. It began as a fork of the GPL-licensed SerialPortTerminal project and was substantially refactored and extended into a serial communications investigation and diagnostic application.

In October 2026, its reusable communications subsystem was extracted into the independent [SerialCommunications](https://github.com/RoyHinkley/SerialCommunications) repository. This repository retains the earlier development history, including development of the communications library before extraction.

## License

SerialBench is distributed under the GNU General Public License v3.0. See `LICENSE` for the complete license text.
