# VOICEROID2-YMM

[YMM4 (ゆっくり Movie Maker v4)](https://manjubox.net/ymm4/) 向け VOICEROID2 音声合成プラグイン。
VOICEROID2 に同梱される AITalk SDK (`aitalked.dll`) を C# から直接操作する
(設計は [aitalk_wrapper](https://github.com/rumia/aitalk_wrapper) の C# 移植、GUI デザインは VoicePeak-plus を踏襲)。
YMM4 上でのプラグイン名 (声質のエンジン名・設定名) は **VOICEROID2** です
(リポジトリ / DLL 名は VOICEROID2-YMM。API 識別子も他プラグインとの衝突回避のため VOICEROID2-YMM を使います)。

**64bit 専用**: YMM4 (64bit) のプロセス内で `aitalked.dll` をロードするため、
64bit 版の VOICEROID2 が必要です。

## 構成

```
VOICEROID2-YMM/
├── VOICEROID2-YMM.csproj             <- プラグインビルド定義
├── Directory.Build.props             <- 環境変数 YMM4_DIR から YMM4 のパスを解決
├── code.jpg                          <- 認証シード値の画像 (README 内で参照)
├── Voice/
│   ├── Voiceroid2VoicePlugin.cs      <- IVoicePlugin (エントリポイント)
│   ├── Voiceroid2VoiceSpeaker.cs     <- IVoiceSpeaker (声質 1 つ分, ゆっくり記法対応)
│   ├── Voiceroid2VoiceParameter.cs   <- 話速 / ピッチ / 抑揚 / 音量 / ポーズ
│   ├── Voiceroid2VoiceSettings.cs    <- 設定 (声質キャッシュ + 読み仮名辞書)
│   ├── Voiceroid2VoicePronounce.cs   <- 合成結果 (編集用かな) の保持
│   ├── Settings/                     <- 設定画面 (VoicePeak-plus デザイン踏襲)
│   ├── PropertyEditor/               <- アクセントエディタ (VoicePeak-plus のアクセント画面を移植)
│   │   ├── AccentLineControl.xaml    <- 高低ラインの表示・クリック編集
│   │   ├── Voiceroid2AccentEditorWindow.xaml <- 発音編集ウィンドウ
│   │   └── Voiceroid2AccentEditorAttribute.cs <- YMM4 プロパティエディタ連携
│   └── AITalk/                       <- aitalked.dll 操作層 (aitalk_wrapper の C# 移植)
│       ├── AITalkApi.cs              <- 構造体 / コールバック / 関数ポインタ
│       ├── AITalkEngine.cs           <- ライブラリ初期化・読み変換・音声合成
│       ├── AITalkInstallation.cs     <- インストール先検出 (環境変数 + レジストリ)
│       ├── AquesTalkKana.cs          <- ゆっくり記法のアクセント指定 (') 変換
│       ├── ReadingApplier.cs         <- 読み仮名辞書 (表記 → 読み)
│       └── WavFile.cs                <- WAV 書き出し (依存なし)
└── tests/
    ├── fake_aitalked/                <- フェイク aitalked.dll (P/Invoke 契約の検証用)
    └── vo_check/                     <- エンジン検証コンソール
```

## 必要なもの

| 種別       | 指定方法                                                    |
|------------|-------------------------------------------------------------|
| YMM4 本体  | 環境変数 `YMM4_DIR` (例: `C:\YukkuriMovieMaker_v4\`)        |
| VOICEROID2 | 64bit 版。自動検出 or 環境変数 `VOICEROID2_YMM_INSTALL_DIR`  |
| 認証シード | 環境変数 `VOICEROID2_AUTH_SEED` (値は下記の code.jpg)       |

パスやシード値はコード・設定ファイルには一切含めず、すべて環境変数で指定します。

## 環境変数

| 変数 | 用途 | 既定 |
|------|------|------|
| `YMM4_DIR` | YMM4 のインストール先 (ビルド時, 末尾 `\` 含む) | なし (未設定ならビルドエラー) |
| `VOICEROID2_AUTH_SEED` | 認証コードのシード値 (必須) | なし (未設定なら合成時にエラー) |
| `VOICEROID2_YMM_INSTALL_DIR` | VOICEROID2 インストール先の上書き | Program Files / レジストリから自動検出 |
| `VOICEROID2_YMM_USER_DIR` | VOICEROID2 ユーザーデータ (辞書) の基準ディレクトリ | `%USERPROFILE%\Documents\VOICEROID2` |

認証コードのシード値は

<img src="code.jpg" height="24px">

(コード内には含めていません。システム環境変数として設定し、YMM4 を再起動してください)

## ビルド方法

```bat
setx YMM4_DIR "C:\YukkuriMovieMaker_v4\"
dotnet build -c Release
```

ビルド成果物の `VOICEROID2-YMM.dll` は YMM4 の `user\plugin\VOICEROID2-YMM\` に自動コピーされる
(`<PostBuild>` ターゲット。YMM4 起動中はロックのためコピーをスキップし警告を出します)。
`dotnet build` を実行するシェルに `YMM4_DIR` が設定されていれば `setx` は不要です。

### 認証コードのビルド時埋め込み

ビルド時に `-p:AuthSeed=<値>` を渡すと、認証コードが DLL に埋め込まれます
(実行時は環境変数 `VOICEROID2_AUTH_SEED` が埋め込み値より優先されます)。

```bat
dotnet build -c Release -p:AuthSeed=<シード値は code.jpg を参照>
```

- 未指定なら何も埋め込まず、実行時の環境変数のみで動作します。
- 埋め込み値はビルド環境の環境変数 `VOICEROID2_AUTH_SEED` が自動で使われます
  (テストプロジェクトは除く。テストは決定性のため既定では埋め込みません)。
- 埋め込んだ DLL を配布する場合は認証コードが含まれる点に注意してください。

## GitHub Actions (CI)

`.github/workflows/ci.yml` が以下を実行します (Windows ランナー):

1. フェイク `aitalked.dll` をビルドし、`tests\vo_check` のエンジン検証を実行
   (YMM4 / VOICEROID2 不要)。
2. YMM4 公式配布 ZIP をダウンロードして参照 DLL を取得し、プラグインをビルド。
3. ビルドした DLL を成果物としてアップロード。

リポジトリの Settings > Secrets に `VOICEROID2_AUTH_SEED` を登録すると、
CI ビルドにも認証コードが埋め込まれます (未登録でもビルド自体は成功します)。

## 使い方 (YMM4)

1. `VOICEROID2_AUTH_SEED` をシステム環境変数に設定する (値は上記の画像を参照)。
2. YMM4 のタイムラインに「音声合成」アイテムを追加し、キャラクターの声質に
   `VOICEROID2` を選ぶ。
3. 初回は声質一覧が `Voice` ディレクトリのスキャンで取得されキャッシュされる
   (設定画面の「声質一覧を更新」でも再取得できる)。
4. 音声パラメータで、話速、ピッチ、抑揚、音量、ポーズ (文間) を調整できる。
5. 合成時、`Documents\VOICEROID2` のユーザー辞書 (単語 / フレーズ / 記号ポーズ) が
   存在すれば自動で読み込まれる。

### アクセントの指定 (ゆっくり記法)

ゆっくりボイス (AquesTalk) と同様に、**モーラの直後に「'」(アポストロフィ)** を置くと
そのモーラからピッチが下がります。

| 入力 | 意味 |
|------|------|
| `ハ'シ` | は**し** (橋: ハで下がる) |
| `ハシ'` | はし (箸: シで下がる) |
| `ゆっ'くりしていってね` | ゆっ**く**り… (既定は ゆ にアクセント) |

- セリフ欄に「'」を書いても反映されます。細かく調整したい場合はアクセントエディタ
  (下記) を使うのがおすすめです。
- 読みは AITalk の読み記号 (AI-Kana) を介して変換されるため、かなのモーラ数が
  ずれる場合はアクセント指定が無視されて既定の読みで合成されます。

### YMM4 の辞書 (読み上げ変換) について

このプラグインは YMM4 の**読み上げ変換のユーザー辞書** (`YukkuriMovieMaker.KanjiToYomi.UserDictionary.json`)
を直接読み込み、セリフへのテキスト置換として適用します。YMM4 の設定で登録した内容が
そのまま合成に反映されます (辞書が見つからない・壊れている場合は辞書なしで動作します)。

- **読み上げ変換** (WordSets): 表記の置換 (例: ゆっくりMovieMaker → ゆっくりムービーメーカー、時刻 → 〇時〇分)
- **伏せ字** (AsteriskWordSets): 不適切な単語を伏せ字にして読み上げる
- 正規表現・大文字小文字無視・有効/無効は YMM4 側の設定に従います

### アクセントエディタ (VOICEPEAK-plus のアクセント画面を移植)

音声アイテムのプロパティにある「VOICEROID2 発音編集 (アクセント)...」ボタンから、
アクセント位置をグラフィカルに編集できるウィンドウが開きます。

- 単語 (アクセント句) ごとに、カタカナのモーラと VOICEPEAK 風の高低ラインが表示される
  (区切りは VOICEROID2 と同じアクセント句境界です。例: 弦巻マキです → ツルマキ、マキデス の 2 句)
- **モーラをクリック**すると、そのモーラからピッチが下がる位置にアクセント核が移動する
  (同じモーラをもう一度クリックすると指定を解除してエンジン既定へ)
- **モーラを上下にドラッグ**でもアクセント位置を変更できる (上へドラッグ = そのモーラまで高、
  下へドラッグ = そのモーラから低。最後のモーラを上へ = 句内に下がりなし = エンジン既定)
- 下がり位置には **▼ マーカー**が表示される。▼ を**左右にドラッグ**するとアクセント核がモーラ境界
  に沿って移動し、**クリックで解除** (エンジン既定へ)。解除中はエンジン既定の位置に破線の ▼ が
  表示され、クリックでその位置に確定できる
- 各モーラの下に「高」「低」ラベルが表示される
- ヘッダー帯の**「句分割」**ボタンで、アクセント核の位置に句読点ポーズ (、) を挿入して
  アクセント句を 2 つに分割できる (句ごとに独立したアクセント核を持てるようになる)
- **単語の読み (ヘッダー帯) をクリック**すると読み編集欄が開き、よみがなを直接変更できる
  (かな・漢字どちらでも可。Enter で確定、Esc でキャンセル)。確定するとエンジンが読みを
  再取得してモーラ列とアクセントを再構成する (読みが変わったためアクセント位置はエンジン
  既定に戻る。編集欄に「'」を書けばアクセント位置も同時に指定できる)
- 「プレビュー再生」で編集中の読みを試聴できる
- 「OK (適用)」で反映、キャンセルで破棄。反映後はその読みが合成に使われる

### 設定画面

`設定 > VOICEROID2` に以下を表示します (VoicePeak-plus の設定画面デザインを踏襲):

- 接続: 検出されたインストール先 / ユーザーデータ / 認証シードの設定状態 (値自体は表示しない)
- 声質一覧の更新ボタン
- 読み仮名辞書エディタ (表記 → 読みの置換テーブル。合成時にテキストへ適用)

## テスト

```bat
cd tests\fake_aitalked
build.cmd            # フェイク aitalked.dll をビルド (vswhere で MSVC を検出)
cd ..\..
dotnet run --project tests\vo_check -c Release
```

フェイク `aitalked.dll` は AITalk SDK のふりをするテストダブルで、構造体レイアウト・
呼び出し規約・コールバック・イベント駆動のジョブ完了・WAV 出力・環境変数駆動の検出を
VOICEROID2 実機なしで検証します (x64)。

### 実機モード (VOICEROID2 インストール環境)

```bat
set VOICEROID2_AUTH_SEED=<シード値は code.jpg を参照>
dotnet run --project tests\vo_check -c Release -- --real tamiyasu_44
```

実際の VOICEROID2 に対して 読み変換 → 音声合成 → WAV 出力までを検証します
(出力: `%TEMP%\vo2_real_out.wav`)。シード値は環境変数でのみ渡し、コードには含めません。

## 制限

- 64bit 版 VOICEROID2 専用。32bit 版しかない環境では合成時に明確なエラーを表示します。
- 声質の表示名は VoiceDB ディレクトリ名です (既知のコード `akari_44` → 紲星あかり のみ変換)。

## ライセンス

このリポジトリのコードは [LICENSE](LICENSE) (BSD 2-Clause) に従います。
`aitalked.dll` の呼び出し方法の参考として [aitalk_wrapper](https://github.com/rumia/aitalk_wrapper)
(MIT) を、プラグイン構造・GUI デザインの参考として VoicePeak-plus (MIT 相当) を使用しています。
