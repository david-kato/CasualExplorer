namespace CasualExplorer.Services;

public static class ReadOnlyStatusMessages
{
    public static string ForSelectedReadSuccess(SelectedFolderReadResult result) =>
        $"状態: {result.Entries.Files.Count}項目を表示（直下のファイル・フォルダ{(result.IsPartial ? "確認済み" : "全")}{result.Entries.TotalFileCount}項目・リンク{result.Entries.SkippedLinkCount}項目/未対応{result.SkippedUnsupportedCount}項目をスキップ）" +
        (result.IsPartial ? "。走査上限に達したため総数と全体の最新順は未確定です。" : "。") +
        "最大30項目・下の階層/内容は読まず、ファイル変更は行いません。";

    public static string ForSelectedReadFailure(Exception error) => error switch
    {
        DirectoryNotFoundException or FileNotFoundException => "状態: 選んだフォルダが見つかりません。もう一度選択してください（ファイル変更は行っていません）。",
        UnauthorizedAccessException => "状態: 安全に読取りできません。ローカルの普通のフォルダを選んでください。リンク/ネットワーク/オフライン経路、アクセス権限不足や使用中の経路は対応しません（ファイル変更は行っていません）。",
        IOException => "状態: 選んだフォルダの読取りに失敗しました。再選択または同梱ダミーへ戻してください（ファイル変更は行っていません）。",
        ArgumentException => "状態: 選択したパスを扱えません。ローカルフォルダを再選択してください（ファイル変更は行っていません）。",
        _ => throw new ArgumentException("Only expected selected-folder failures can be displayed.", nameof(error))
    };
    public static string ForReadSuccess(FileReadResult result) =>
        $"状態: {result.Files.Count}件を表示しています（通常ファイル全{result.TotalFileCount}件・リンク{result.SkippedLinkCount}件をスキップ・ファイル変更は行いません）";

    public static string ForReadFailure(Exception error) => error switch
    {
        DirectoryNotFoundException or FileNotFoundException =>
            "状態: 同梱ダミーフォルダまたはファイルが見つかりません。アプリ一式を再ビルドしてください（ファイル変更は行っていません）。",
        UnauthorizedAccessException =>
            "状態: 同梱ダミーフォルダを安全に読み取れません。アクセス権限と同梱先を確認してください（ファイル変更は行っていません）。",
        IOException =>
            "状態: 同梱ダミーデータの読み取り中に問題が発生しました。アプリを閉じて再起動してください（ファイル変更は行っていません）。",
        _ => throw new ArgumentException("Only expected read failures can be displayed.", nameof(error))
    };
}
