using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using PermaDel.Core;
using PermaDel.Core.Updates;
using PermaDel.Dialogs;
using PermaDel.Models;
using PermaDel.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.System;

namespace PermaDel;

public sealed partial class MainWindow : Window
{
    private static readonly (string Name, string Glyph, string Path)[] KnownFolders =
    [
        ("Desktop", "\uE7F4", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)),
        ("Documents", "\uE8A5", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
        ("Downloads", "\uE896", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")),
        ("Pictures", "\uE91B", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)),
        ("Music", "\uE8D6", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)),
        ("Videos", "\uE714", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)),
    ];

    private readonly ObservableCollection<FileEntry> _queue = [];
    private readonly Stack<string?> _back = new();
    private readonly Stack<string?> _forward = new();
    private readonly ShredRequest? _request;
    private SettingsView? _settingsView;
    private string? _currentPath;
    private int _navigationVersion;
    private bool _syncingNavigation;
    private CancellationTokenSource? _cancellation;
    private ReleaseInfo? _availableRelease;
    private CancellationTokenSource? _updateDownload;

    /// <param name="request">Items to shred right away, when PermaDel was started from the File Explorer context menu.</param>
    public MainWindow(ShredRequest? request = null)
    {
        _request = request;
        InitializeComponent();
        ConfigureWindow();
        ApplyTheme(AppSettings.Theme);

        PassesBox.Minimum = Shredder.MinPasses;
        PassesBox.Maximum = Shredder.MaxPasses;
        PassesBox.Value = request?.Passes ?? AppSettings.DefaultPasses;
        QueueList.ItemsSource = _queue;
        _queue.CollectionChanged += (_, _) => UpdateCommands();
        Updates = new UpdateCoordinator();
        Updates.Checked += OnUpdateChecked;
        Root.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnRootPointerPressed), handledEventsToo: true);
        Root.Loaded += async (_, _) =>
        {
            // The check runs in the background; the window is never delayed.
            _ = Updates.CheckOnStartupAsync();
            try
            {
                await InitializeAsync();
            }
            catch (Exception ex)
            {
                ShowStatus(InfoBarSeverity.Error, "PermaDel couldn't finish loading", ex.Message);
            }
        };

        UpdateCommands();
    }

    internal UpdateCoordinator Updates { get; }

    private enum History { Record, Back, Forward, Keep }

    private bool IsBusy => _cancellation is not null;

    private nint WindowHandle => Win32Interop.GetWindowFromWindowId(AppWindow.Id);

    private void ConfigureWindow()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "PermaDel.ico"));
        AppWindow.Closing += OnWindowClosing;

        var scale = GetDpiForWindow(WindowHandle) / 96.0;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = (int)(980 * scale);
            presenter.PreferredMinimumHeight = (int)(600 * scale);
        }

        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var width = Math.Min((int)(1280 * scale), workArea.Width);
        var height = Math.Min((int)(800 * scale), workArea.Height);
        AppWindow.MoveAndResize(new RectInt32(workArea.X + (workArea.Width - width) / 2, workArea.Y + (workArea.Height - height) / 2, width, height));
    }

    private void ApplyTheme(ElementTheme theme)
    {
        Root.RequestedTheme = theme;
        AppWindow.TitleBar.PreferredTheme = theme switch
        {
            ElementTheme.Light => TitleBarTheme.Light,
            ElementTheme.Dark => TitleBarTheme.Dark,
            _ => TitleBarTheme.UseDefaultAppMode,
        };
    }

    private async Task InitializeAsync()
    {
        NavView.MenuItems.Add(CreateNavigationItem("This PC", "\uE977", null));
        NavView.MenuItems.Add(new NavigationViewItemSeparator());
        foreach (var (name, glyph, path) in KnownFolders.Where(folder => Directory.Exists(folder.Path)))
            NavView.MenuItems.Add(CreateNavigationItem(name, glyph, path));
        NavView.MenuItems.Add(new NavigationViewItemSeparator());
        foreach (var drive in await Task.Run(() => FileEntry.EnumerateDrives()))
            NavView.MenuItems.Add(CreateNavigationItem(drive.Name, drive.Glyph, drive.FullPath));

        if (_request is null)
        {
            await NavigateAsync(null);
            if (AppSettings.ShowWelcome)
                await ShowWelcomeAsync();
            return;
        }

        var requested = _request.Paths.Select(FileEntry.FromPath).OfType<FileEntry>().ToList();
        await NavigateAsync(requested.FirstOrDefault()?.Location);
        AddToQueue(requested);
        if (_queue.Count > 0)
            await ShredAsync();
        else if (requested.Count == 0)
            ShowStatus(InfoBarSeverity.Warning, "Nothing to shred", "The selected items no longer exist.");
    }

    private async Task ShowWelcomeAsync()
    {
        var welcome = new WelcomeDialog { XamlRoot = Content.XamlRoot, RequestedTheme = Root.ActualTheme };
        await welcome.ShowAsync();
        if (welcome.DontShowAgain)
            AppSettings.ShowWelcome = false;
    }

    #region Navigation

    private static NavigationViewItem CreateNavigationItem(string name, string glyph, string? path) =>
        new() { Content = name, Icon = new FontIcon { Glyph = glyph }, Tag = path };

    /// <summary>Opens a folder, or the drive overview when <paramref name="path"/> is null.</summary>
    private async Task NavigateAsync(string? path, History history = History.Record)
    {
        var version = ++_navigationVersion;
        IReadOnlyList<FileEntry> entries;
        try
        {
            entries = await Task.Run(() => path is null ? FileEntry.EnumerateDrives() : FileEntry.EnumerateDirectory(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (version == _navigationVersion)
                ShowStatus(InfoBarSeverity.Error, "Can't open this location", ex.Message);
            return;
        }

        if (version != _navigationVersion)
            return;

        switch (history)
        {
            case History.Record when !string.Equals(path, _currentPath, StringComparison.OrdinalIgnoreCase):
                _back.Push(_currentPath);
                _forward.Clear();
                break;
            case History.Back when _back.TryPop(out _):
                _forward.Push(_currentPath);
                break;
            case History.Forward when _forward.TryPop(out _):
                _back.Push(_currentPath);
                break;
        }
        _currentPath = path;

        if (StatusBar.Severity == InfoBarSeverity.Error)
            StatusBar.IsOpen = false;

        NavView.Content = ShredView;
        BrowserList.ItemsSource = entries;
        BrowserPlaceholder.Text = path is null ? "No drives are available." : "This folder is empty.";
        BrowserPlaceholder.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ModifiedHeader.Text = path is null ? string.Empty : "Date modified";
        SizeHeader.Text = path is null ? "Free space" : "Size";
        Breadcrumbs.ItemsSource = BuildCrumbs(path);
        BackButton.IsEnabled = _back.Count > 0;
        ForwardButton.IsEnabled = _forward.Count > 0;
        UpButton.IsEnabled = path is not null;

        _syncingNavigation = true;
        NavView.SelectedItem = NavView.MenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, path, StringComparison.OrdinalIgnoreCase));
        _syncingNavigation = false;

        UpdateCommands();
    }

    /// <summary>Reloads the current folder, falling back to the nearest ancestor that still exists.</summary>
    private Task RefreshAsync()
    {
        var path = _currentPath;
        while (path is not null && !Directory.Exists(path))
            path = Path.GetDirectoryName(path);
        return NavigateAsync(path, History.Keep);
    }

    private async Task GoBackAsync()
    {
        if (_back.TryPeek(out var path))
            await NavigateAsync(path, History.Back);
    }

    private async Task GoForwardAsync()
    {
        if (_forward.TryPeek(out var path))
            await NavigateAsync(path, History.Forward);
    }

    private void ShowSettings()
    {
        if (_settingsView is null)
        {
            _settingsView = new SettingsView(WindowHandle, Updates);
            _settingsView.ThemeChanged += (_, theme) => ApplyTheme(theme);
            _settingsView.UpdateAvailable += (_, release) => ShowUpdateAvailable(release);
            _settingsView.DefaultPassesChanged += (_, passes) =>
            {
                if (!IsBusy)
                    PassesBox.Value = passes;
            };
        }

        _settingsView.RefreshContextMenuState();
        NavView.Content = _settingsView;
    }

    private static List<Crumb> BuildCrumbs(string? path)
    {
        var crumbs = new List<Crumb>();
        for (var directory = path is null ? null : new DirectoryInfo(path); directory is not null; directory = directory.Parent)
            crumbs.Insert(0, new Crumb(directory.Parent is null ? directory.FullName.TrimEnd('\\') : directory.Name, directory.FullName));
        crumbs.Insert(0, new Crumb("This PC", null));
        return crumbs;
    }

    private async void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_syncingNavigation)
            return;

        if (args.IsSettingsSelected)
            ShowSettings();
        else if (args.SelectedItemContainer is { } item)
            await NavigateAsync(item.Tag as string);
    }

    private void OnPaneToggleRequested(TitleBar sender, object args) => NavView.IsPaneOpen = !NavView.IsPaneOpen;

    private async void OnBackClick(object sender, RoutedEventArgs e) => await GoBackAsync();

    private async void OnForwardClick(object sender, RoutedEventArgs e) => await GoForwardAsync();

    /// <summary>The back and forward buttons found on many mice, as in File Explorer.</summary>
    private async void OnRootPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!ReferenceEquals(NavView.Content, ShredView))
            return;

        var properties = e.GetCurrentPoint(Root).Properties;
        if (properties.IsXButton1Pressed)
            await GoBackAsync();
        else if (properties.IsXButton2Pressed)
            await GoForwardAsync();
    }

    private async void OnUpClick(object sender, RoutedEventArgs e)
    {
        if (_currentPath is not null)
            await NavigateAsync(Path.GetDirectoryName(_currentPath));
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void OnBreadcrumbClicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args)
    {
        if (args.Item is Crumb crumb)
            await NavigateAsync(crumb.Path);
    }

    private async void OnBrowserDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is FileEntry { IsContainer: true } entry)
            await NavigateAsync(entry.FullPath);
    }

    private async void OnBrowserKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && BrowserList.SelectedItems is [FileEntry { IsContainer: true } entry])
        {
            e.Handled = true;
            await NavigateAsync(entry.FullPath);
        }
        else if (e.Key == VirtualKey.Back)
        {
            e.Handled = true;
            await GoBackAsync();
        }
    }

    private void OnBrowserSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateCommands();

    #endregion

    #region Shred list

    private void OnAddClick(object sender, RoutedEventArgs e) => AddToQueue(BrowserList.SelectedItems.Cast<FileEntry>());

    private void AddToQueue(IEnumerable<FileEntry> entries)
    {
        var skipped = new List<string>();
        foreach (var entry in entries)
        {
            if (Shredder.IsProtected(entry.FullPath))
                skipped.Add(entry.Name);
            else if (!_queue.Any(queued => string.Equals(queued.FullPath, entry.FullPath, StringComparison.OrdinalIgnoreCase)))
                _queue.Add(entry);
        }

        if (skipped.Count > 0)
        {
            ShowStatus(
                InfoBarSeverity.Warning,
                "Protected locations skipped",
                $"Drives, system folders, your user folders (such as Documents) and PermaDel itself can't be shredded as a whole. You can shred the items inside them instead. Skipped: {string.Join(", ", skipped)}");
        }
    }

    private void OnQueueDragOver(object sender, DragEventArgs e)
    {
        if (IsBusy || !e.DataView.Contains(StandardDataFormats.StorageItems))
            return;

        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "Add to shred list";
    }

    private async void OnQueueDrop(object sender, DragEventArgs e)
    {
        if (IsBusy || !e.DataView.Contains(StandardDataFormats.StorageItems))
            return;

        var deferral = e.GetDeferral();
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            AddToQueue(items.Select(item => FileEntry.FromPath(item.Path)).OfType<FileEntry>());
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void OnRemoveQueueItemClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is FileEntry entry)
            _queue.Remove(entry);
    }

    private void OnClearQueueClick(object sender, RoutedEventArgs e) => _queue.Clear();

    #endregion

    #region Shredding

    private async void OnShredClick(object sender, RoutedEventArgs e) => await ShredAsync();

    private async Task ShredAsync()
    {
        var passes = double.IsNaN(PassesBox.Value) ? AppSettings.DefaultPasses : (int)Math.Clamp(PassesBox.Value, Shredder.MinPasses, Shredder.MaxPasses);
        if (AppSettings.ConfirmBeforeShredding && !await ConfirmShredAsync(passes))
            return;
        if (AppSettings.RequireVerification && !await VerifyUserAsync())
            return;

        using var cancellation = new CancellationTokenSource();
        var paths = _queue.Select(entry => entry.FullPath).ToList();
        var progress = new Progress<ShredProgress>(ReportProgress);
        SetBusy(cancellation);

        try
        {
            var result = await Task.Run(() => new Shredder(passes).Shred(paths, progress, cancellation.Token));
            ShowResult(result);
        }
        catch (OperationCanceledException)
        {
            ShowStatus(InfoBarSeverity.Warning, "Shredding cancelled", "Items processed before cancelling were destroyed. The file in progress may be partially overwritten.");
        }
        catch (Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, "Shredding failed", ex.Message);
        }
        finally
        {
            SetBusy(null);
            foreach (var entry in _queue.Where(entry => FileEntry.FromPath(entry.FullPath) is null).ToList())
                _queue.Remove(entry);
            await RefreshAsync();
        }
    }

    private async Task<bool> ConfirmShredAsync(int passes)
    {
        var confirmation = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            RequestedTheme = Root.ActualTheme,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
            Title = $"Permanently shred {Pluralize(_queue.Count, "item")}?",
            Content = $"Everything in the shred list will be overwritten {Pluralize(passes, "time")} and deleted. This can't be undone and the data can't be recovered.",
            PrimaryButtonText = "Shred",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        return await confirmation.ShowAsync() == ContentDialogResult.Primary;
    }

    /// <summary>Asks Windows to confirm the signed-in user, so nobody else at an unlocked PC can shred data with PermaDel.</summary>
    private async Task<bool> VerifyUserAsync()
    {
        try
        {
            if (await AccountVerification.VerifyAsync(WindowHandle, $"Verify it's you to permanently shred {Pluralize(_queue.Count, "item")}."))
                return true;

            ShowStatus(InfoBarSeverity.Informational, "Nothing was shredded", "Your identity wasn't verified.");
        }
        catch (COMException ex)
        {
            ShowStatus(InfoBarSeverity.Error, "Couldn't verify your identity", ex.Message);
        }
        return false;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        _cancellation?.Cancel();
        CancelButton.IsEnabled = false;
        ProgressDetail.Text = "Cancelling…";
    }

    private void ReportProgress(ShredProgress progress)
    {
        if (!IsBusy || _cancellation!.IsCancellationRequested)
            return;

        ShredProgressBar.IsIndeterminate = progress.TotalBytes == 0;
        ShredProgressBar.Value = progress.Fraction;
        ProgressPercent.Text = progress.Fraction.ToString("P0");
        ProgressDetail.Text = $"Pass {progress.Pass} of {progress.Passes} · {FileEntry.FormatBytes(progress.BytesProcessed)} of {FileEntry.FormatBytes(progress.TotalBytes)} · {Path.GetFileName(progress.CurrentPath)}";
    }

    private void ShowResult(ShredResult result)
    {
        var summary = $"{Pluralize(result.FilesShredded, "file")} and {Pluralize(result.DirectoriesRemoved, "folder")} permanently destroyed.";
        if (result.Succeeded)
        {
            ShowStatus(InfoBarSeverity.Success, "Shredding complete", summary);
            return;
        }

        var details = new Button { Content = "View details" };
        details.Click += async (_, _) => await ShowFailuresAsync(result.Failures);
        ShowStatus(InfoBarSeverity.Warning, $"{Pluralize(result.Failures.Count, "item")} could not be shredded", summary, details);
    }

    private async Task ShowFailuresAsync(IReadOnlyList<ShredFailure> failures)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            RequestedTheme = Root.ActualTheme,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
            Title = "Items that could not be shredded",
            Content = new ListView
            {
                ItemsSource = failures,
                ItemTemplate = (DataTemplate)Root.Resources["FailureTemplate"],
                SelectionMode = ListViewSelectionMode.None,
                MaxHeight = 400,
            },
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close,
        };
        await dialog.ShowAsync();
    }

    private void OnWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (!IsBusy)
            return;

        args.Cancel = true;
        ShowStatus(InfoBarSeverity.Warning, "Shredding in progress", "Cancel the operation before closing PermaDel.");
    }

    #endregion

    private void SetBusy(CancellationTokenSource? cancellation)
    {
        _cancellation = cancellation;
        var busy = cancellation is not null;
        RefreshUpdateActions();

        ProgressPanel.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        ShredButton.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
        CancelButton.IsEnabled = busy;
        PassesBox.IsEnabled = QueueList.IsEnabled = !busy;
        ShredProgressBar.IsIndeterminate = true;
        ProgressPercent.Text = string.Empty;
        ProgressDetail.Text = "Preparing…";
        UpdateCommands();
    }

    private void UpdateCommands()
    {
        var selected = BrowserList.SelectedItems.Count;
        SelectionText.Text = selected == 0 ? string.Empty : $"{Pluralize(selected, "item")} selected";
        AddButton.IsEnabled = selected > 0 && !IsBusy;

        QueueSummary.Text = _queue.Count == 0 ? "Empty" : Pluralize(_queue.Count, "item");
        QueuePlaceholder.Visibility = _queue.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearButton.IsEnabled = _queue.Count > 0 && !IsBusy;
        ShredButton.IsEnabled = _queue.Count > 0;
    }

    private void ShowStatus(InfoBarSeverity severity, string title, string message, ButtonBase? action = null)
    {
        StatusBar.Severity = severity;
        StatusBar.Title = title;
        StatusBar.Message = message;
        StatusBar.ActionButton = action;
        StatusBar.Content = null;
        StatusBar.IsOpen = true;
    }

    /// <summary>The startup check's result. Only a newer version opens the bar.</summary>
    private void OnUpdateChecked(object? sender, UpdateCheckResult result)
    {
        if (result is { Status: UpdateCheckStatus.UpdateAvailable, Release: { } release })
            ShowUpdateAvailable(release);
    }

    internal void ShowUpdateAvailable(ReleaseInfo release)
    {
        _availableRelease = release;
        UpdateBar.Title = $"PermaDel {release.Version.ToString(3)} is available";
        UpdateBar.Message = $"You are running PermaDel {Updates.CurrentVersion.ToString(3)}.";
        UpdateBar.IsOpen = true;
        RefreshUpdateActions();
    }

    /// <summary>An update cannot start while a shred is in progress.</summary>
    private void RefreshUpdateActions() =>
        UpdateInstallButton.IsEnabled = !IsBusy && _updateDownload is null;

    private void OnUpdateBarClosed(InfoBar sender, object args) => _availableRelease = null;

    private async void OnUpdateNotesClick(object sender, RoutedEventArgs e)
    {
        if (_availableRelease is { } release)
            await UpdateDialogs.ShowReleaseNotesAsync(Root.XamlRoot, Root.ActualTheme, release);
    }

    private async void OnUpdateInstallClick(object sender, RoutedEventArgs e)
    {
        if (_availableRelease is { } release)
            await DownloadUpdateAsync(release);
    }

    /// <summary>
    /// Downloads the release's installer, verifies it against its checksum, then
    /// starts it and closes the window. Progress and cancel use the status bar,
    /// so no modal has to be dismissed when the work finishes.
    /// </summary>
    private async Task DownloadUpdateAsync(ReleaseInfo release)
    {
        if (IsBusy)
        {
            ShowStatus(InfoBarSeverity.Warning, "PermaDel is busy", "Wait for the current shred to finish before updating.");
            return;
        }
        if (_updateDownload is not null)
            return;

        using var cancellation = new CancellationTokenSource();
        _updateDownload = cancellation;
        RefreshUpdateActions();

        var progressBar = new ProgressBar { Width = 220, IsIndeterminate = true, VerticalAlignment = VerticalAlignment.Center };
        var percent = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Text = "0%" };
        var cancel = new Button { Content = "Cancel" };
        cancel.Click += (_, _) => cancellation.Cancel();
        StatusBar.Severity = InfoBarSeverity.Informational;
        StatusBar.Title = $"Downloading PermaDel {release.Version.ToString(3)}\u2026";
        StatusBar.Message = "PermaDel verifies the installer before it runs it.";
        StatusBar.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Children = { progressBar, percent, cancel },
        };
        StatusBar.IsOpen = true;

        var progress = new Progress<UpdateProgress>(update =>
        {
            if (update.TotalBytes is not > 0)
                return;
            progressBar.IsIndeterminate = false;
            progressBar.Value = Math.Clamp(100.0 * update.BytesReceived / update.TotalBytes.Value, 0, 100);
            percent.Text = $"{progressBar.Value:0}%";
        });

        UpdateDownloadResult result;
        try
        {
            result = await Updates.Service.DownloadAsync(release, cancellation.Token, progress);
        }
        catch (OperationCanceledException)
        {
            ShowStatus(InfoBarSeverity.Informational, "Update cancelled", "PermaDel is unchanged.");
            return;
        }
        finally
        {
            _updateDownload = null;
            StatusBar.Content = null;
            RefreshUpdateActions();
        }

        StatusBar.IsOpen = false;
        if (!result.Success)
        {
            await UpdateDialogs.ShowUpdateFailureAsync(Root.XamlRoot, Root.ActualTheme, result.Error ?? "The installer could not be downloaded.", result.ReleasePageUrl);
            return;
        }

        switch (await Updates.Service.InstallAsync(result, CancellationToken.None))
        {
            case InstallOutcome.Started:
                Close();
                break;
            case InstallOutcome.Cancelled:
                ShowStatus(InfoBarSeverity.Informational, "Update cancelled", "Windows did not get permission to run the installer.");
                break;
            default:
                await UpdateDialogs.ShowUpdateFailureAsync(Root.XamlRoot, Root.ActualTheme, "The installer could not be started.", release.PageUrl);
                break;
        }
    }

    private static string Pluralize(int count, string noun) => $"{count:N0} {noun}{(count == 1 ? string.Empty : "s")}";

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);

    private sealed record Crumb(string Label, string? Path)
    {
        public override string ToString() => Label;
    }
}
