WinDivert 原生二進位檔案
==========================

請從 WinDivert 官方網站下載預編譯的二進位檔案：
https://reqrypt.org/windivert.html

下載後，將以下檔案放入本目錄：
  - WinDivert.dll     （WinDivert 2.2.x，x64）
  - WinDivert64.sys   （WinDivert 核心驅動程式，x64）

版本需求：WinDivert 2.2.x（官方預編譯，已簽章）

重要：
- 必須使用已簽章的官方版本，自行編譯的版本在 64-bit Windows 需要停用 driver signature enforcement
- WinDivert64.sys 必須與 WinDivert.dll 版本完全一致

下載後 build：
  dotnet build DohShield/DohShield.csproj -r win-x64 -c Release
