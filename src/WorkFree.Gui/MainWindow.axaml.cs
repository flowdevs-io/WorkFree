using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using WorkFree.Core;

namespace WorkFree.Gui;

public partial class MainWindow : Window
{
    private static readonly IBrush FrogBrush = new SolidColorBrush(Color.Parse("#5FD67A"));
    private static readonly IBrush TongueBrush = new SolidColorBrush(Color.Parse("#FF6F9C"));
    private static readonly IBrush FlyBrush = new SolidColorBrush(Color.Parse("#E8ECEA"));
    private static readonly IBrush GroundBrush = new SolidColorBrush(Color.Parse("#3F7A52"));
    private static readonly IBrush NoteBrush = new SolidColorBrush(Color.Parse("#F2C94C"));

    private MainViewModel? _watched;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_watched is not null) _watched.PropertyChanged -= OnViewModelChanged;
            _watched = DataContext as MainViewModel;
            if (_watched is null) return;
            _watched.ConfirmDelete = request => ConfirmDialog.Ask(this, request);
            _watched.PropertyChanged += OnViewModelChanged;
            PaintFrog(_watched.Frame);
        };
    }

    private MainViewModel Vm => (MainViewModel)DataContext!;

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Frame)) PaintFrog(_watched?.Frame);
    }

    /// <summary>Draws one animation frame as coloured monospace text.</summary>
    private void PaintFrog(FrogFrame? frame)
    {
        var inlines = new InlineCollection();
        if (frame is not null)
        {
            for (var row = 0; row < frame.Lines.Length; row++)
            {
                if (row > 0) inlines.Add(new LineBreak());
                var line = frame.Lines[row];
                var kinds = frame.Kinds[row];
                var start = 0;
                for (var i = 1; i <= line.Length; i++)
                {
                    if (i < line.Length && kinds[i] == kinds[start]) continue;
                    inlines.Add(new Run(line[start..i]) { Foreground = BrushFor(kinds[start]) });
                    start = i;
                }
            }
        }
        FrogText.Inlines = inlines;
    }

    private static IBrush BrushFor(char kind) => kind switch
    {
        FrogScene.KindFrog => FrogBrush,
        FrogScene.KindTongue => TongueBrush,
        FrogScene.KindFly => FlyBrush,
        FrogScene.KindGround => GroundBrush,
        _ => NoteBrush,
    };

    private async void OnAddFolder(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Folder that holds your git repositories",
            AllowMultiple = false,
        });
        var path = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        if (path is not null) await Vm.AddRootAsync(path);
    }

    private async void OnRemoveFolder(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string path }) await Vm.RemoveRootAsync(path);
    }

    private async void OnRescan(object? sender, RoutedEventArgs e) => await Vm.ScanAsync();
    private void OnSelectSafe(object? sender, RoutedEventArgs e) => Vm.SelectAllSafe();
    private void OnClearSelection(object? sender, RoutedEventArgs e) => Vm.ClearSelection();
    private async void OnPrune(object? sender, RoutedEventArgs e) => await Vm.PruneAsync();
    private async void OnDelete(object? sender, RoutedEventArgs e) => await Vm.DeleteSelectedAsync();
}
