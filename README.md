# VoiceDeck

VoiceDeck は、指定した通話アプリがマイクを使用している間だけ、他のアプリの音量を自動で下げ、終了後に元の音量へ戻す Windows 向け常駐アプリです。

A lightweight Windows app that lowers other app volumes while selected voice apps are using the microphone.

> **Note:** リポジトリ名や実行ファイル名（`VoiceDuck.App.Wpf.exe`）には開発時の名称「VoiceDuck」が残っていますが、正式なアプリ名は VoiceDeck です。

## Download

通常利用者は、[GitHub Releases](https://github.com/u10-github/VoiceDuck-public/releases) から `VoiceDuck-SelfContained.zip` をダウンロードしてください。

.zip を展開し、`VoiceDuck.App.Wpf.exe` を実行するだけで使えます（.NET ランタイムの追加インストールは不要です）。

開発者向けのビルド手順は [Build](#build) セクションを参照してください。

## 動作要件

- Windows 10 / 11（64-bit）
- Discord 系クライアント（Discord.exe / DiscordCanary.exe / DiscordPTB.exe）

SelfContained 版（Releases で配布）:
- .NET ランタイム同梱のため、追加インストール不要
- 配布サイズ約 150MB

Framework-dependent 版（自ビルド時）:
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) が必要

## Build

開発者向けのビルド手順です。

### 前提条件

- [.NET SDK 8.0.400+](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
- Windows 10 / 11

### ビルドとテスト

```powershell
dotnet restore --locked-mode
dotnet build --configuration Release
dotnet test
```

### 実行（WPF 常駐アプリ）

```powershell
dotnet run --project src\VoiceDuck.App.Wpf
```

### 配布パッケージの作成

Framework-dependent 版（約 5MB、.NET 8 Desktop Runtime が必要）:

```powershell
.\scripts\publish.ps1
```

SelfContained 版（約 150MB、ランタイム同梱）:

```powershell
.\scripts\publish-self-contained.ps1
```

## 既知の制約

- Discord Canary / Discord PTB は初期プリセットとして登録されていますが、Windows 実機での動作確認は行われていません。
- 複数の Trigger Apps を同時に使用した場合の動作は実機未確認です。
- 音量 0 の音声セッションが正しく列挙されるかは未確認です。
- ブラウザ版通話サービス（Google Meet / Zoom Web 等）はプロセス単位でのみ制御し、タブ単位の制御は行いません。
- マイク使用状態の検出はキャプチャセッションの有無に基づいており、実際の発話中かどうかは区別しません。

## License

[MIT](LICENSE)

## Maintenance Policy

このリポジトリは作者が個人利用目的で開発したものです。積極的な機能追加や Issue 対応、Pull Request のレビュー・マージは行いません。改変・拡張したい場合は fork してください。
