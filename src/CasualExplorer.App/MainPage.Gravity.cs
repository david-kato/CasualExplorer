using System.Diagnostics;
using CasualExplorer.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace CasualExplorer;

public sealed partial class MainPage
{
    private GravityLayout? _layout;
    private readonly List<Button> _cards = new();
    private readonly Stopwatch _clock = new();
    private bool _preview, _paused, _subscribed, _hostVisible = true, _closed;
    private int _dragged = -1;
    private Point _dragOffset;
    private uint? _pointerId;
    private double _lastFrame;
    public bool IsGravityUpdating => _subscribed;
    public long GravityFrameCount { get; private set; }

    private void List_Click(object sender, RoutedEventArgs e)
    {
        EndDrag();
        _preview = false;
        StopRendering();
        ListPanel.Visibility = Visibility.Visible;
        GravityPanel.Visibility = PauseButton.Visibility = ResetButton.Visibility = Visibility.Collapsed;
    }

    private void Gravity_Click(object sender, RoutedEventArgs e)
    {
        _preview = true;
        ListPanel.Visibility = Visibility.Collapsed;
        GravityPanel.Visibility = PauseButton.Visibility = ResetButton.Visibility = Visibility.Visible;
        // Measure the newly shown panel before choosing the initial surface;
        // otherwise its stale viewport can differ from the Reset surface.
        GravityPanel.UpdateLayout();
        if (_layout == null) BuildCards();
        StartRendering();
    }

    private void BuildCards()
    {
        _layout = new GravityLayout(Files);
        _layout.PrepareInitialLayout(Math.Max(1000, GravityScroll.ViewportWidth), Math.Max(600, GravityScroll.ViewportHeight));
        GravityCanvas.Width = _layout.Width;
        GravityCanvas.Height = _layout.Height;
        foreach (var node in _layout.Nodes)
        {
            int index = _cards.Count;
            var title = new TextBlock { Text = node.File.Name, FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            var kind = new TextBlock { Text = node.File.Kind, FontSize = 12 };
            var content = new StackPanel { Spacing = 3 };
            content.Children.Add(title); content.Children.Add(kind);
            var card = new Button
            {
                Width = GravityLayout.CardWidth, Height = GravityLayout.CardHeight,
                Content = content, HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(8), VerticalContentAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(Colors.White), BorderThickness = new Thickness(1),
                RenderTransform = new TranslateTransform(), Tag = index
            };
            AutomationProperties.SetName(card, node.File.Name + " " + node.File.Kind);
            card.Click += (_, _) => SelectCard(index);
            card.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(Card_Pressed), true);
            card.PointerMoved += Card_Moved;
            card.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(Card_Released), true);
            card.PointerCaptureLost += Card_CaptureLost;
            _cards.Add(card);
            GravityCanvas.Children.Add(card);
        }
        DrawCards();
        GravityHint.Text = _cards.Count == 0 ? "表示できる通常ファイルは0件です。一覧に戻れます。" : "種類ごとにまとまる表示試作です。ドラッグは画面上だけ。狭い画面ではスクロールできます。";
    }

    private void SelectCard(int index)
    {
        var file = _layout!.Nodes[index].File;
        DetailText.Text = $"選択中: {file.Name} ｜ 種類: {file.Kind} ｜ 更新日時: {file.ModifiedAtText}（表示だけ・ファイル変更なし）";
        for (int i = 0; i < _cards.Count; i++)
        {
            _cards[i].BorderBrush = new SolidColorBrush(i == index ? Colors.DodgerBlue : Colors.LightGray);
            _cards[i].BorderThickness = new Thickness(i == index ? 3 : 1);
            var label = (TextBlock)((StackPanel)_cards[i].Content).Children[0];
            label.Text = (i == index ? "✓ " : "") + _layout.Nodes[i].File.Name;
        }
    }

    private void Card_Pressed(object sender, PointerRoutedEventArgs e)
    {
        var card = (Button)sender;
        var point = e.GetCurrentPoint(GravityCanvas);
        if (!point.Properties.IsLeftButtonPressed || _pointerId.HasValue) return;
        int index = (int)card.Tag;
        SelectCard(index);
        if (!card.CapturePointer(e.Pointer)) return;
        _dragged = index; _pointerId = e.Pointer.PointerId;
        var node = _layout!.Nodes[index];
        node.IsPinned = true;
        _dragOffset = new Point(point.Position.X - node.X, point.Position.Y - node.Y);
        StartRendering();
    }

    private void Card_Moved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragged < 0 || e.Pointer.PointerId != _pointerId) return;
        var position = e.GetCurrentPoint(GravityCanvas).Position;
        _layout!.Drag(_dragged, position.X - _dragOffset.X, position.Y - _dragOffset.Y);
        DrawCards();
    }

    private void Card_Released(object sender, PointerRoutedEventArgs e)
    {
        if (e.Pointer.PointerId == _pointerId) { EndDrag(); StartRendering(); }
    }
    private void Card_CaptureLost(object sender, PointerRoutedEventArgs e) { EndDrag(); StartRendering(); }
    private void EndDrag()
    {
        int index = _dragged;
        _dragged = -1; _pointerId = null;
        if (index >= 0)
        {
            _layout!.Nodes[index].IsPinned = false;
            _cards[index].ReleasePointerCaptures();
        }
    }

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        _paused = !_paused;
        PauseButton.Content = _paused ? "再開" : "一時停止";
        if (_paused) StopRendering(); else StartRendering();
        GravityHint.Text = _paused ? "一時停止中。選択・ドラッグは表示だけで使えます。" : "表示だけを再配置しています。ファイルは変更しません。";
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        EndDrag();
        _layout!.Reset(Math.Max(1000, GravityScroll.ViewportWidth), Math.Max(600, GravityScroll.ViewportHeight));
        GravityCanvas.Width = _layout.Width; GravityCanvas.Height = _layout.Height;
        DrawCards(); StartRendering();
    }

    private void Canvas_SizeChanged(object sender, SizeChangedEventArgs e) { /* Fixed minimum surface stays scrollable; no cards are dropped. */ }
    private void DrawCards()
    {
        for (int i = 0; i < _cards.Count; i++)
        {
            var transform = (TranslateTransform)_cards[i].RenderTransform;
            transform.X = _layout!.Nodes[i].X; transform.Y = _layout.Nodes[i].Y;
        }
    }

    private void StartRendering()
    {
        if (_subscribed || !_preview || _paused || !_hostVisible || _closed || _folderLoadActive || _layout?.Nodes.Count is null or 0) return;
        _clock.Restart(); _lastFrame = 0;
        GravityHint.Text = "再配置中です。ドラッグ後の重なりを解消しています（表示だけ）。";
        CompositionTarget.Rendering += RenderFrame;
        _subscribed = true;
    }
    private void StopRendering()
    {
        if (_subscribed) CompositionTarget.Rendering -= RenderFrame;
        _subscribed = false; _clock.Stop();
    }
    private void RenderFrame(object? sender, object e)
    {
        double now = _clock.Elapsed.TotalSeconds;
        if (now - _lastFrame < 1.0 / 30) return;
        bool moving = _layout!.Step(now - _lastFrame);
        _lastFrame = now;
        GravityFrameCount++;
        DrawCards();
        if (!moving) { StopRendering(); GravityHint.Text = "配置が安定しました。ドラッグや「配置を戻す」で動かせます（表示だけ）。"; }
    }

    public void SetHostVisible(bool visible)
    {
        _hostVisible = visible;
        if (!visible) { EndDrag(); StopRendering(); } else StartRendering();
    }
    public void StopGravityPreview() { _closed = true; _folderCancellation?.Cancel(); EndDrag(); StopRendering(); }
    private void Page_Unloaded(object sender, RoutedEventArgs e) => StopGravityPreview();
}
