# DOH Shield — Windows

A Windows desktop app that encrypts all DNS queries using **DNS-over-HTTPS (DoH)**, implemented via kernel-level packet interception with WinDivert. Ported from the [Android version](https://github.com/wusagiojisan/doh-shield-android), replacing the VPN TUN mechanism with WinDivert.

Runs as a standalone `.exe` — no installer required. Requires administrator privileges to load the WinDivert kernel driver.

---

## Architecture

```
App (DNS Query, UDP port 53)
        │
        ▼
┌────────────────────────┐
│   WinDivert (kernel)   │  ← Filter: "udp.DstPort == 53"
│   WinDivertOpen()      │     Intercepts outbound DNS packets before
│   WinDivertRecv()      │     they reach the network stack
└──────────┬─────────────┘
           │ raw packet bytes
           ▼
┌────────────────────────┐
│   WinDivertEngine.cs   │  ← Parses IP (v4/v6) + UDP header
│   (capture loop)       │     Extracts DNS payload
└──────────┬─────────────┘
           │
           ▼
┌────────────────────────────────────────┐
│  DnsPacketParser.cs                    │  ← Manual RFC 1035 wire format parsing
│  (no external DNS library)             │     Name compression pointer support
└──────────┬─────────────────────────────┘
           │ DnsQuery(domain, type, txId)
           ▼
┌────────────────────────────────────────┐
│          DohResolver.cs                │
│  1. Cache hit  → replace TX ID, return│  ← LRU cache with TTL expiry
│  2. Primary DoH server               │  ← Circuit Breaker (Closed/Open/HalfOpen)
│  3. Retry (100ms delay)              │
│  4. Fallback DoH server              │
│  5. SERVFAIL response                │
└──────────┬─────────────────────────────┘
           │ DNS response bytes
           ▼
┌────────────────────────┐
│  BuildResponsePacket() │  ← Swap src↔dst IP + port, replace DNS payload
│                        │     WinDivertHelperCalcChecksums() for IP/UDP
└──────────┬─────────────┘
           │
           ▼
    WinDivertSend()  (inject as INBOUND packet)
```

---

## Key Technical Challenges

### 1. WinDivert P/Invoke
Manually authored the entire P/Invoke layer for WinDivert in C# — `WinDivertOpen`, `WinDivertRecv`, `WinDivertSend`, `WinDivertHelperCalcChecksums`. The `WINDIVERT_ADDRESS` struct must be exactly 80 bytes; a startup `Debug.Assert` validates the layout at runtime.

### 2. Bootstrap DNS — Preventing DoH Loop
When WinDivert intercepts all UDP port 53 traffic, the DoH client's own HTTP stack also triggers DNS queries to resolve the DoH server hostname — causing infinite recursion and timeouts. Solved using `SocketsHttpHandler.ConnectCallback` to bypass DNS and connect directly to bootstrap IPs. For custom DoH URLs, the hostname is pre-resolved *before* WinDivert starts intercepting.

### 3. Packet Reconstruction
DNS response packets are built by manually manipulating raw byte arrays: swapping IPv4/IPv6 source and destination addresses, swapping UDP ports, updating IP total length and UDP length fields, zeroing checksums, then calling `WinDivertHelperCalcChecksums` for recalculation.

### 4. Cross-platform Build
The entire project is developed and built on macOS cross-compiling to `win-x64` using `EnableWindowsTargeting=true`. WPF + System.Drawing work correctly in this setup via the official .NET 8 SDK.

### 5. Same DNS Core as Android
`DnsPacketParser`, `DnsCache`, `CircuitBreaker`, `DohResolver`, and `DohClient` are direct C# ports of the Kotlin counterparts, with identical logic. 32 unit tests run on macOS (targeting `net8.0`) with no Windows dependency.

---

## Tech Stack

| Layer | Technology |
|---|---|
| Language | C# 12 / .NET 8 |
| UI | WPF (net8.0-windows) |
| MVVM | CommunityToolkit.Mvvm |
| Database | Microsoft.Data.Sqlite |
| System Tray | Hardcodet.NotifyIcon.Wpf |
| Packet Interception | WinDivert 2.2 (P/Invoke) |
| HTTP Client | System.Net.Http.HttpClient (SocketsHttpHandler) |
| Settings | System.Text.Json |

---

## Features

- 🔒 Encrypts all DNS traffic via DoH (RFC 8484)
- ⚡ LRU DNS cache with TTL-based expiry
- 🔄 Automatic fallback between DoH servers with Circuit Breaker
- 📊 Per-query logging with domain, type, cache status, response time
- ⚙️ Supports Cloudflare, Google, Quad9, or custom DoH URL
- 🖥️ System Tray with status icon (blue = active, grey = stopped)
- 📦 Single `.exe` — no installer, just copy and run

---

## Requirements

- Windows 10 x64 (build 17763) or later
- Administrator privileges (required for WinDivert kernel driver)
- **Not compatible with ARM64 Windows** — WinDivert provides x86/x64 binaries only

---

## Building from Source

**Requirements:** .NET 8 SDK (official Microsoft installer, not Homebrew on macOS)

```bash
# 1. Clone the repo
git clone https://github.com/wusagiojisan/doh-shield-windows.git
cd doh-shield-windows

# 2. Download WinDivert 2.2.x from https://github.com/basil00/WinDivert/releases
#    Copy the following files to DohShield/Resources/native/
#      x64/WinDivert.dll
#      x64/WinDivert64.sys

# 3. Build (cross-compile from macOS is supported)
dotnet build DohShield/DohShield.csproj -r win-x64 -c Release

# 4. Run unit tests (macOS/Linux compatible)
dotnet test DohShield.Tests/

# 5. Publish single-file exe
dotnet publish DohShield/DohShield.csproj -r win-x64 -c Release \
  --self-contained true -o publish/
```

---

## Third-party Licenses

- [WinDivert](https://github.com/basil00/WinDivert) — LGPL v3 or GPL v2
- [Microsoft.Data.Sqlite](https://github.com/dotnet/efcore) — MIT
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) — MIT
- [Hardcodet.NotifyIcon.Wpf](https://github.com/hardcodet/wpf-notifyicon) — MIT

---

## Related

- [DOH Shield Android](https://github.com/wusagiojisan/doh-shield-android) — The Android counterpart using VpnService + TUN

---

## License

MIT License — see [LICENSE](LICENSE)
