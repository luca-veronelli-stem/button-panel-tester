# VENDOR.md — Infrastructure.Protocol

**Upstream**: `git@github.com:luca-veronelli-stem/stem-device-manager.git`
**Pinned SHA**: `4700c2db65c858f53b4796971a174508b99bce0a`
**Vendored on**: 2026-05-24
**Vendored by**: spec-002 PR-A (issue #113) via `eng/vendor-protocol-stack.ps1`

## Manifest

| Upstream path | Local path | LOC | Last verified |
|---------------|------------|-----|---------------|
| Core/Interfaces/ICommunicationPort.cs | Core/Interfaces/ICommunicationPort.cs | 39 | 2026-05-24 |
| Core/Interfaces/IDeviceVariantConfig.cs | Core/Interfaces/IDeviceVariantConfig.cs | 49 | 2026-05-24 |
| Core/Interfaces/IPacketDecoder.cs | Core/Interfaces/IPacketDecoder.cs | 27 | 2026-05-24 |
| Core/Interfaces/IProtocolService.cs | Core/Interfaces/IProtocolService.cs | 55 | 2026-05-24 |
| Core/Models/AppLayerDecodedEvent.cs | Core/Models/AppLayerDecodedEvent.cs | 46 | 2026-05-24 |
| Core/Models/ChannelKind.cs | Core/Models/ChannelKind.cs | 17 | 2026-05-24 |
| Core/Models/Command.cs | Core/Models/Command.cs | 9 | 2026-05-24 |
| Core/Models/ConnectionState.cs | Core/Models/ConnectionState.cs | 11 | 2026-05-24 |
| Core/Models/DeviceVariant.cs | Core/Models/DeviceVariant.cs | 17 | 2026-05-24 |
| Core/Models/DeviceVariantConfig.cs | Core/Models/DeviceVariantConfig.cs | 107 | 2026-05-24 |
| Core/Models/DictionaryData.cs | Core/Models/DictionaryData.cs | 8 | 2026-05-24 |
| Core/Models/ImmutableArrayEquality.cs | Core/Models/ImmutableArrayEquality.cs | 34 | 2026-05-24 |
| Core/Models/ProtocolAddress.cs | Core/Models/ProtocolAddress.cs | 9 | 2026-05-24 |
| Core/Models/RawPacket.cs | Core/Models/RawPacket.cs | 28 | 2026-05-24 |
| Core/Models/SmartBootDeviceEntry.cs | Core/Models/SmartBootDeviceEntry.cs | 13 | 2026-05-24 |
| Core/Models/TelemetryDataPoint.cs | Core/Models/TelemetryDataPoint.cs | 87 | 2026-05-24 |
| Core/Models/Variable.cs | Core/Models/Variable.cs | 10 | 2026-05-24 |
| Services/Protocol/DictionarySnapshot.cs | Services/DictionarySnapshot.cs | 93 | 2026-05-24 |
| Services/Protocol/NetInfo.cs | Services/NetInfo.cs | 47 | 2026-05-24 |
| Services/Protocol/PacketDecoder.cs | Services/PacketDecoder.cs | 159 | 2026-05-24 |
| Services/Protocol/PacketReassembler.cs | Services/PacketReassembler.cs | 102 | 2026-05-24 |
| Services/Protocol/ProtocolService.cs | Services/ProtocolService.cs | 364 | 2026-05-24 |
| Infrastructure.Protocol/Hardware/CanPort.cs | Hardware/CanPort.cs | 170 | 2026-05-24 |
| Infrastructure.Protocol/Hardware/IPcanDriver.cs | Hardware/IPcanDriver.cs | 21 | 2026-05-24 |
| Infrastructure.Protocol/Hardware/PCANManager.cs | Hardware/PCANManager.cs | 313 | 2026-05-24 |


## Local modifications

| File | Lines | Why | Upstream PR |
|------|-------|-----|-------------|
| _none yet — populated by T005_ |  |  |  |

## Removal path

Replace this vendored copy with the `Stem.Communication` NuGet once
`stem-device-manager`'s Phase 5 migration completes. Tracking:
https://github.com/luca-veronelli-stem/button-panel-tester/issues/111

## Re-vendoring procedure

See `specs/002-can-link-and-panel-discovery/contracts/vendor-manifest.md`
section "Re-vendoring procedure". Edit `System.Collections.Hashtable System.Collections.Hashtable System.Collections.Hashtable System.Collections.Hashtable System.Collections.Hashtable System.Collections.Hashtable System.Collections.Hashtable System.Collections.Hashtable System.Collections.Hashtable System.Collections.Hashtable System.Collections.Hashtable System.Collections.Hashtable System.Collections.Hashtable System.Collections.Hashtable System.Collections.Hashtable System.Collections.Hashtable System.Collections.Hashtable System.Collections.Hashtable System.Collections.Hashtable System.Collections.Hashtable System.Collections.Hashtable System.Collections.Hashtable System.Collections.Hashtable System.Collections.Hashtable System.Collections.Hashtable` in
`eng/vendor-protocol-stack.ps1`, re-run the script, update
`VENDOR.sha256`.