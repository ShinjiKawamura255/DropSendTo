# Macro Script Samples

この文書は、現行の Macro Script だけで使える実用サンプル集です。各サンプルはスロットの `Macro Script` 欄へ貼り付けて使えます。`COMMAND` を含む例は `Macro Script 拡張` モードで、スロットの Command/Arguments も設定してください。

## 1. ドロップしたパス一覧を貼り付ける

複数ファイルをドロップし、番号付きで前面アプリへ入力します。

```text
FOREACH_DROP Item INDEX i
    TEXT {{i}}. {{Item}}
    KEY Enter
ENDFOREACH
```

## 2. ドロップパスをスラッシュ区切りに整形する

Markdown やチャットへ貼る前に Windows パスの `\` を `/` に変換します。

```text
FOREACH_DROP Item INDEX i
    SET Path {{Item}}
    REPLACE Path "\" "/"
    TEXT {{i}}. {{Path}}
    KEY Enter
ENDFOREACH
```

## 3. ドロップファイルを外部コマンドへ順番に渡す

スロットの Command に処理したいアプリを登録し、各ドロップパスを 1 件ずつ渡します。

```text
FOREACH_DROP Item INDEX i
    COMMAND "{{Item}}"
    WAIT 300
ENDFOREACH
```

## 4. 拡張子で起動先アプリを切り替える

`COMMAND_APP` で以降の `COMMAND` の起動先を一時的に変更します。

```text
SET Target {{drop_path}}
IF {{Target}} ENDSWITH ".png" OR {{Target}} ENDSWITH ".jpg"
    COMMAND_APP "C:\Apps\ImageViewer.exe"
    COMMAND "{{Target}}"
    COMMAND_APP RESET
ELSE
    COMMAND "{{Target}}"
ENDIF
```

## 5. クリップボード文字列を整形して貼り付ける

空白を削除し、連続改行を 1 つにまとめて貼り付けます。

```text
SET Body {{clipboard}}
REPLACE Body " " ""
REPLACE_REGEX Body "\r?\n+" "\n"
CLIPTEXT {{Body}}
```

## 6. 入力値を受け取って検索する

スロットの Command にブラウザや検索ツールを登録し、入力値を引数として渡します。

```text
PROMPT Query "検索語を入力してください"
IF {{Query}}
    COMMAND "{{Query}}"
ELSE
    RETURN "empty query"
ENDIF
```

## 7. Wi-Fi SSID に応じて処理を切り替える

社内 Wi-Fi など、場所に応じた処理を分けられます。

```text
WIFI_SSID CurrentSsid
IF {{CurrentSsid}} == "OfficeWiFi"
    COMMAND
ELSE
    POPUP "OfficeWiFi 接続中ではないため実行しません。"
ENDIF
```

## 8. ファイル存在チェック後に安全に実行する

ドロップパスが存在する場合だけコマンドへ渡します。

```text
TESTPATH PathOk {{drop_path}}
IF {{PathOk}}
    COMMAND {{drop_args}}
ELSE
    POPUP "ドロップされたパスが見つかりません。"
ENDIF
```

## 9. ショートカットやリンクの実体パスを貼り付ける

`.lnk` やシンボリックリンクの解決結果を変数へ入れます。

```text
RESOLVE_LINK TargetPath {{drop_path}}
TEXT {{TargetPath}}
```

## 10. ファイル内容の先頭を読んで判定する

ログや設定ファイルの軽いチェックに使えます。

```text
READFILE Body {{drop_path}} MAX 8192
IF {{Body}} CONTAINS "ERROR"
    POPUP "ERROR を含むファイルです。"
ELSE
    POPUP "ERROR は見つかりませんでした。"
ENDIF
```

## 11. アクティブウィンドウ中央を操作する

キーボードショートカットが弱いアプリ向けの補助操作です。

```text
MOUSEMOVEABS WIN_CENTER
MOUSELEFTDOUBLECLICK
WAIT 200
KEY Ctrl+A
```

## 12. マクロ開始位置へ戻る

操作後にカーソルを開始地点へ戻してクリックします。

```text
MOUSEMOVEABS CURSOR_START
MOUSELEFTCLICK
```

## 13. ウィンドウ左上からの相対位置をクリックする

座標予約語の `_X` / `_Y` 成分を変数に入れ、オフセットして使います。

```text
SET X WIN_TOPLEFT_X
SET Y WIN_TOPLEFT_Y
ADD X 120
ADD Y 60
MOUSEMOVEABS {{X}} {{Y}}
MOUSELEFTCLICK
```

## 14. Prefix 待機へ戻す

下準備マクロの最後に Prefix 待機状態へ移行し、続けて別スロットを選びやすくします。

```text
PREFIX ARM
```

## 15. 条件に合わない場合は早期終了する

`RETURN` で後続処理を止めます。

```text
TESTPATH PathOk {{drop_path}}
IF {{PathOk}} == 0
    RETURN "path not found"
ENDIF
COMMAND {{drop_args}}
```
