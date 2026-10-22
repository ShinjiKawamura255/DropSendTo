# リリース規約

## バージョンと公開対象

- 正式版は `vMAJOR.MINOR.PATCH`（例: `v1.4.0`）のタグを使う。各数値に不要な先頭ゼロを付けない。
- 互換性を維持した修正は PATCH、機能追加は MINOR、既存の設定・操作・外部動作の互換性を壊す変更は MAJOR を上げる。破壊的変更には移行手順を付ける。
- 正式版のタグは検証済みの `main` のコミットへ付ける。配布元は、そのタグと HEAD が一致したクリーンな作業ツリーとする。未追跡ファイルも含むが、Git の ignore 対象の成果物・ログは含まない。
- 公開済みのタグ・配布ファイルを別の内容で上書きしない。修正は次のバージョンで公開する。
- `vX.Y.Z-rc.N` 等の試験版は GitHub Releases の **pre-release** として公開し、Latest に指定しない。タグ未一致・未コミット変更を含むローカル成果物は既存の `-dev...` 識別子を使う。
- 正式版の配布には `scripts/Build-Release.ps1` を使う。正式版形式を指定すると、タグ一致と作業ツリーのクリーン状態を配布処理の前に検証する。`-VersionOnly` は識別子の確認のみで、この検証や成果物生成を行わない。
- 通常の `dotnet build/run` は開発版である。配布スクリプトが `AssemblyInformationalVersion` に埋め込む識別子を、アプリのバージョン表示・更新判定の正本とする。Git の SHA を正式版に自動付加しない。
- 単一ファイルのパス取得を壊す `IL3000` はプロジェクト設定でエラーとして扱う。通常版・ランタイム同梱版の publish がともに成功することを確認し、警告の抑制で回避しない。

## 正式版の作成と公開

1. 対象の変更、関連テスト、`USER_GUIDE.md`、必要な仕様文書を完成させる。GitHub Release の説明には変更点、修正、不互換変更・移行手順、動作環境を記載する。
2. Windows の .NET 10 SDK で次の検証を実行する。GUI 変更時は `docs/TESTPLAN.md` に従って表示・操作も確認する。

   ```powershell
   dotnet restore
   ./scripts/Test-Release-Version.ps1
   ./scripts/Test-Release-Artifacts.ps1
   dotnet format --verify-no-changes --no-restore
   dotnet test -c Release --no-restore
   dotnet build -c Release --no-restore
   ./scripts/Test-SettingsWindow.ps1 -Configuration Release
   ```

3. 変更をコミットし、検証したコミットへ未使用のタグを付ける。以下は例なので実際の次期バージョンに置き換える。

   ```powershell
   git status --short
   git tag -a v1.4.0 -m "DropSendTo v1.4.0"
   ./scripts/Build-Release.ps1 -Rid win-x64 -Version v1.4.0 -Portable
   ```

4. ZIP を別フォルダーへ展開し、起動・バージョン表示・既存設定の読み込みを確認する。Framework Dependent 版には .NET 10 Desktop Runtime が必要。`Portable` 版はランタイム同梱で、設定保存先は通常版と同じ `%AppData%/DropSendTo` である。署名する場合は配布スクリプトの署名オプションを使う。
5. 検証済みのコミット・タグを GitHub へ push し、同じタグの **draft release** を作る。生成した `DropSendTo_win-x64_vX.Y.Z.zip` と `DropSendTo_Portable_win-x64_vX.Y.Z.zip`、リリース説明を添付する。ソースコード ZIP だけでは実行ファイルの配布にならない。
6. 添付ファイルと説明を確認して draft を公開する。正式版は pre-release を OFF にし、最新版として公開する版だけ **Set as the latest release** を指定する。GitHub の Latest が更新確認の参照先となるため、旧版の保守リリースで上書きしない。
7. 公開ページとダウンロードを確認する。更新確認機能を備えた旧正式版がある場合は「DropSendTo について…」を開き、新版とダウンロードボタンが表示されることを確認する。新版自身では更新不要になることを確認する。

`dist/` は生成物であり直接編集しない。タグの push、Release の作成・公開は保守者が行う外部公開操作で、ローカルのビルドスクリプトは実行しない。

両形式へ `LICENSE` と `USER_GUIDE.md` を同梱する。ランタイム同梱版は解決された runtime pack のライセンスと第三者通知を `licenses/` に含め、ZIP にも残す。署名を指定した場合は SignTool の終了コードと Authenticode の `Valid` 状態を確認し、失敗なら ZIP 作成へ進まない。検証用の配布物は `-OutputRoot <隔離フォルダー>` に生成でき、その中の版別出力と `latest/` が更新される。

配布スクリプトは OutputRoot 内の固有の staging ディレクトリで全選択形式の publish、文書同梱、署名、ZIP と `latest/` コピーを準備する。準備がすべて成功するまで、既存の同版ディレクトリ・ZIP・`latest/` を変更しない。同じ出力先への昇格は Global named mutex で Windows セッション間も直列化し、30秒で取得できない場合は既存成果物を変更せず失敗する。昇格中にファイル移動が失敗した場合は、それまでに置き換えた成果物を旧版へ戻す。復元も失敗した場合は staging を削除せず、回復ファイルの場所をエラーで示す。ロック・権限・空き容量の問題を解消してから、staging 内の `rollback/` に残る旧成果物を対応する OutputRoot のパスへ復元する。プロセス強制終了・電源断をまたぐ自動復旧は保証しない。

staging と成果物のリンク検査では、[Windows SDK の CLOUD 系タグ](https://github.com/microsoft/win32metadata/blob/main/generation/WinSDK/RecompiledIdlHeaders/um/winnt.h)を持つ OneDrive の非転送 reparse point を許容する。ジャンクション・シンボリックリンク・不明な reparse tag は昇格/削除の前に拒否し、取得できない属性情報を安全扱いで続行しない。

`-NoZip` は版別ディレクトリと `latest/` のみを更新し、既存の同版 ZIP は保持する。ZIP を配布するときは `-NoZip` を付けずに再生成し、今回のビルドと一致することを確認する。

## 問題発見時

公開前に問題が見つかった場合は公開せず修正と検証をやり直す。公開後の問題は説明へ追記し、必要なら GitHub の Latest を既知の正常版へ戻し、修正版を新しい PATCH 以上のバージョンで公開する。アプリは手元より古い正式版への更新を促さないため、問題版を利用中のユーザーには個別の復旧手順を案内する。

## 更新確認との契約

利用者向けの挙動は [SPEC.md](SPEC.md#sp-017-about-and-update-check) が正本。公開 Latest Release だけを参照し、開発版・試験版を正式版と比較しない。更新操作はブラウザで該当リリースページを開き、ファイルのダウンロード・置換は利用者が行う。
