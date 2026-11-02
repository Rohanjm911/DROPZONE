# DROPZONE Android Application Package

Privacy-first device-to-device file transfer for Android 14+ (API Level 34+).

## Package Contents
- `DropZone.Android.dll`: Android platform integration assembly implementing:
  - `DropZoneTransferForegroundService`: `FOREGROUND_SERVICE_TYPE_DATA_SYNC` persistent foreground service with ongoing progress notification.
  - `AndroidWakeLockService`: CPU `PARTIAL_WAKE_LOCK` and Wi-Fi `MulticastLock` orchestration ensuring mDNS discovery and high-speed transfer continue when screen is locked.
  - `AndroidStoragePicker`: Scoped Storage / Storage Access Framework (SAF) URI resolution.
  - `AndroidNotificationService`: Notification channel manager for active transfers and pairing requests.
- `AndroidManifest.xml`: Production Android manifest with all required permissions and service declarations:
  - `INTERNET`, `ACCESS_NETWORK_STATE`, `ACCESS_WIFI_STATE`, `CHANGE_WIFI_MULTICAST_STATE`
  - `FOREGROUND_SERVICE`, `FOREGROUND_SERVICE_DATA_SYNC`, `POST_NOTIFICATIONS`, `WAKE_LOCK`
  - `NEARBY_WIFI_DEVICES` (Android 13+ local peer discovery)
- `DropZone.UI.dll`: Shared Avalonia MVVM user interface rendered via `ISingleViewApplicationLifetime`.
- `lib/`: Target ABI directories (`arm64-v8a`, `x86_64`) for native Rust engine `libdropzone_engine.so`.

## Building the APK / AAB
To build the signed Android APK package using the .NET Android workload:
```bash
dotnet publish src/DropZone.Android/DropZone.Android.csproj -c Release -f net11.0-android -p:AndroidPackageFormat=apk -p:AndroidKeyStore=false
```

To install directly to a connected device via ADB:
```bash
adb install -r bin/Release/net11.0-android/publish/com.dropzone.app-Signed.apk
```
