using System.Collections.ObjectModel;
using CasualExplorer.Models;
using CasualExplorer.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CasualExplorer;

public sealed partial class MainPage : Page
{
    private bool _isLoaded;

    public ObservableCollection<FileItem> Files { get; } = new();

    public MainPage()
    {
        InitializeComponent();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (_isLoaded)
        {
            return;
        }

        _isLoaded = true;
        LoadSamples();
    }

    private void LoadSamples()
    {
        try
        {
            FileReadResult result = ReadOnlyFileCatalog.LoadBundledSamples();

            foreach (FileItem file in result.Files)
            {
                Files.Add(file);
            }

            StatusText.Text = ReadOnlyStatusMessages.ForReadSuccess(result);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Files.Clear();
            StatusText.Text = ReadOnlyStatusMessages.ForReadFailure(error);
        }
    }
}
