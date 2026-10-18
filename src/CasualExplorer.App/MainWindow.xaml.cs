using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;

namespace CasualExplorer;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        RootFrame.Navigate(typeof(MainPage));
        AppWindow.Changed += (_, _) =>
        {
            bool visible = AppWindow.IsVisible && (AppWindow.Presenter is not OverlappedPresenter presenter || presenter.State != OverlappedPresenterState.Minimized);
            (RootFrame.Content as MainPage)?.SetHostVisible(visible);
        };
        Closed += (_, _) => (RootFrame.Content as MainPage)?.StopGravityPreview();
    }
}
