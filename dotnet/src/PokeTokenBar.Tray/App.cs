using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

namespace PokeTokenBar.Tray;

[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
    Justification = "The tray icon lives exactly as long as the process; Application is not disposable.")]
internal sealed class App : Application
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(1);

    private readonly UsageScanner _scanner = new();

    private TrayIcon? _tray;
    private CompanionWindow? _window;
    private string? _iconPath;
    private bool _refreshing;

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        base.Initialize();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        _window = new CompanionWindow { Status = "Scanning…" };
        _window.RefreshRequested += (_, _) => Refresh();
        _window.QuitRequested += (_, _) => Quit();

        var open = new NativeMenuItem("Open");
        open.Click += (_, _) => _window.Toggle();

        var quit = new NativeMenuItem("Quit");
        quit.Click += (_, _) => Quit();

        _tray = new TrayIcon
        {
            Icon = TrayIconRenderer.Placeholder(),
            ToolTipText = "PokeTokenBar",
            Menu = new NativeMenu { Items = { open, new NativeMenuItemSeparator(), quit } },
            IsVisible = true,
        };
        _tray.Clicked += (_, _) => _window.Toggle();
        TrayIcon.SetIcons(this, [_tray]);

        var timer = new DispatcherTimer { Interval = RefreshInterval };
        timer.Tick += (_, _) => Refresh();
        timer.Start();

        Refresh();
        base.OnFrameworkInitializationCompleted();
    }

    private void Quit() => (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();

    private void Refresh()
    {
        if (_refreshing)
        {
            return;
        }

        _refreshing = true;
        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        try
        {
            var snapshot = await Task.Run(() => _scanner.ScanAsync(CancellationToken.None).AsTask()).ConfigureAwait(false);
            Dispatcher.UIThread.Post(() => Apply(snapshot));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            var message = ex.Message;
            Dispatcher.UIThread.Post(() =>
            {
                if (_window is not null)
                {
                    _window.Status = "Scan failed: " + message;
                }
            });
        }
        finally
        {
            Dispatcher.UIThread.Post(() => _refreshing = false);
        }
    }

    private void Apply(UsageSnapshot snapshot)
    {
        if (_tray is null || _window is null)
        {
            return;
        }

        _window.Update(snapshot);

        var companion = snapshot.Companion;
        var percent = (int)Math.Round(companion.StageProgress * 100);
        _tray.ToolTipText = string.Create(
            CultureInfo.InvariantCulture,
            $"Today {TokenFormat.Compact(snapshot.Today.Total)} ({TokenFormat.Cost(snapshot.Today.Cost)}) · #{companion.CurrentSpeciesId} {percent}%");

        if (snapshot.SpritePath is not null && !string.Equals(snapshot.SpritePath, _iconPath, StringComparison.Ordinal))
        {
            try
            {
                _tray.Icon = TrayIconRenderer.FromSprite(snapshot.SpritePath);
            }
            catch (Exception ex) when (ex is ArgumentException or IOException)
            {
                // A sprite the cache accepted but the decoder rejects: keep whatever icon is showing.
                _window.Status = "Sprite unreadable: " + Path.GetFileName(snapshot.SpritePath);
            }

            _iconPath = snapshot.SpritePath;
        }
    }
}
