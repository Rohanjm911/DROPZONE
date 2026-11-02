# DROPZONE iOS Application Package

Privacy-first device-to-device file transfer for iOS 17+.

## Package Contents
- `DropZone.iOS.dll`: iOS platform integration assembly implementing:
  - `DropZoneBackgroundTransferManager`: Background execution lifecycle using iOS `BGProcessingTask` and `UIBackgroundModes` (`fetch`, `processing`).
  - `IOSWakeLockService`: `UIApplication.SharedApplication.IdleTimerDisabled = true` management during active transmissions.
  - `IOSStoragePicker`: iOS `UIDocumentPickerViewController` integration for sandboxed file selection.
  - `IOSNotificationService`: `UNUserNotificationCenter` local notifications for transfer completions and pairing requests.
- `Info.plist`: Production property list with Apple privacy strings and Bonjour configuration:
  - `NSLocalNetworkUsageDescription`: Required prompt explaining local network discovery.
  - `NSBonjourServices`: Declared `_dropzone._tcp` mDNS service browsing.
  - `UIBackgroundModes`: `fetch`, `processing`.
  - `LSSupportsOpeningDocumentsInPlace` & `UIFileSharingEnabled`: Files app integration.
- `DropZone.UI.dll`: Shared Avalonia MVVM user interface rendered via `ISingleViewApplicationLifetime`.
- `Frameworks/`: Native engine static library archive (`libdropzone_engine.a`) for iOS Xcode linking.

## Building the IPA Package (macOS & Xcode)
To build and archive the iOS IPA bundle on macOS:
```bash
dotnet publish src/DropZone.iOS/DropZone.iOS.csproj -c Release -f net11.0-ios -p:ArchiveOnBuild=true -p:RuntimeIdentifier=ios-arm64
```
Open the generated `.xcarchive` in Xcode Organizer to sign and distribute to TestFlight or enterprise MDM.
