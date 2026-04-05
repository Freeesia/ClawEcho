# TODO

* [ ] GitHub Actions
  * [ ] Build and test on pull request
  * [ ] Deploy to production on push to main branch
* [ ] DotNet Tool でインストール可能にする
  * [ ] インストール時にシステムサービスとして登録する
* [ ] Windows 対応
  * [x] AudioIO をインターフェイス化して、arecord/aplay 以外の実装も可能にする
  * [x] NAudio を使用して Windows でのオーディオ入出力を実装
    * [x] マイクを共有モードで開いて、他のアプリケーションと同時に使用できるようにする
  * [ ] WakeWordDetector の `python3` コマンドを Windows に対応させる（OS に応じて `python` / `py` に切り替え）
  * [ ] Windows 用のビルドとテストを GitHub Actions に追加
* [x] launchSettings.json を追加して、VS Code から簡単にデバッグできるようにする
* [x] ITtsClient の実装を追加する
  * [x] 検証用にコンソールに出力する PlaceholderTtsClient を実装
* [x] 起動時にモデルファイルの存在をチェックして、見つからない場合はダウンロードする機能を追加
