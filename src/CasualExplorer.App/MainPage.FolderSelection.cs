using CasualExplorer.Services;
using Microsoft.UI.Xaml;
using Microsoft.Windows.Storage.Pickers;

namespace CasualExplorer;

public sealed partial class MainPage
{
    private bool _folderLoadActive;
    private CancellationTokenSource? _folderCancellation;

    private void SetReading(bool active)
    {
        _folderLoadActive = active;
        ChooseFolderButton.IsEnabled = SamplesButton.IsEnabled = ListButton.IsEnabled = GravityButton.IsEnabled = PauseButton.IsEnabled = ResetButton.IsEnabled = !active;
        CancelReadButton.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        if (active) { EndDrag(); StopRendering(); }
        else StartRendering();
    }

    private async void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_folderLoadActive || _closed) return;
        SetReading(true);
        CancelReadButton.Visibility = Visibility.Collapsed;
        try
        {
            var picker = new FolderPicker(XamlRoot.ContentIslandEnvironment.AppWindowId)
            {
                CommitButtonText = "このフォルダを表示"
            };
            var selected = await picker.PickSingleFolderAsync();
            if (_closed) return;
            if (selected == null) { StatusText.Text = "状態: フォルダ選択を取り消しました。対象と表示は変更していません。"; return; }
            await LoadSelectedFolderAsync(selected.Path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            if (!_closed) StatusText.Text = "状態: フォルダ選択を完了できませんでした。対象は変更していません（ファイル変更なし）。";
        }
        finally { if (!_closed) SetReading(false); }
    }

    private async Task LoadSelectedFolderAsync(string path)
    {
        SetReading(true);
        _folderCancellation = new CancellationTokenSource();
        var token = _folderCancellation.Token;
        StatusText.Text = "状態: 選んだフォルダの直下を読取り中です。内容は開きません。中止できます。";
        try
        {
            // Give the UI a dispatch opportunity to display the reading state
            // and accept cancellation before starting the worker.
            await Task.Yield();
            var result = await Task.Run(() => SelectedFolderCatalog.Load(path, token), token);
            if (_closed) return;
            token.ThrowIfCancellationRequested();
            ClearCatalogDisplay();
            foreach (var entry in result.Entries.Files) Files.Add(entry);
            TargetFolderText.Text = "選択したローカルフォルダ（直下のみ）: " + path;
            StatusText.Text = ReadOnlyStatusMessages.ForSelectedReadSuccess(result);
        }
        catch (OperationCanceledException)
        {
            if (!_closed) StatusText.Text = "状態: 読取りを中止しました。対象と表示は変更していません（ファイル変更なし）。";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            if (_closed) return;
            ClearCatalogDisplay();
            TargetFolderText.Text = "選択したフォルダ: 安全な読取りを確認できませんでした";
            StatusText.Text = ReadOnlyStatusMessages.ForSelectedReadFailure(error);
        }
        finally
        {
            _folderCancellation.Dispose(); _folderCancellation = null;
            if (!_closed) SetReading(false);
        }
    }

    private void CancelRead_Click(object sender, RoutedEventArgs e) => _folderCancellation?.Cancel();

    private void ClearCatalogDisplay()
    {
        List_Click(this, new RoutedEventArgs());
        _layout = null; _cards.Clear(); GravityCanvas.Children.Clear();
        _paused = false; PauseButton.Content = "一時停止"; Files.Clear();
        DetailText.Text = "カードを選ぶと名前・種類・更新日時を確認できます（ファイルやフォルダは開きません）。";
    }

    private void Samples_Click(object sender, RoutedEventArgs e)
    {
        if (_folderLoadActive || _closed) return;
        ClearCatalogDisplay();
        TargetFolderText.Text = "同梱ダミーフォルダ: Samples\\TestData";
        LoadSamples();
    }
}
