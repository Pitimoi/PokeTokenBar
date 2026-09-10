using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
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

    private readonly CompanionService _service = new();

    private TrayIcon? _tray;
    private CompanionWindow? _window;
    private string? _iconPath;
    private bool _busy;

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        base.Initialize();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        _window = new CompanionWindow { Status = "Scanning…" };
        _window.RefreshRequested += (_, _) => Run(_service.ScanAsync);
        _window.AdvanceRequested += (_, _) => Run(_service.AdvanceAsync);
        _window.HatchRequested += index => Run(token => _service.ChooseEggAsync(index, token));
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
        timer.Tick += (_, _) => Run(_service.ScanAsync);
        timer.Start();

        Run(_service.ScanAsync);
        base.OnFrameworkInitializationCompleted();
    }

    private void Quit() => (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();

    /// <summary>Runs one game operation off the UI thread; at most one at a time.</summary>
    private void Run(Func<CancellationToken, ValueTask<UsageSnapshot>> operation)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        _ = RunAsync(operation);
    }

    private async Task RunAsync(Func<CancellationToken, ValueTask<UsageSnapshot>> operation)
    {
        try
        {
            var snapshot = await Task.Run(() => operation(CancellationToken.None).AsTask()).ConfigureAwait(false);
            Dispatcher.UIThread.Post(() => Apply(snapshot));

            try
            {
                StatusExport.Write(snapshot);
                SpinnerVerbs.Update(snapshot);
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                Report("Export failed: " + ex.Message);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or HttpRequestException)
        {
            Report("Failed: " + ex.Message);
        }
        finally
        {
            Dispatcher.UIThread.Post(() => _busy = false);
        }
    }

    private void Report(string message) => Dispatcher.UIThread.Post(() =>
    {
        if (_window is not null)
        {
            _window.Status = message;
        }
    });

    private void Apply(UsageSnapshot snapshot)
    {
        if (_tray is null || _window is null)
        {
            return;
        }

        _window.Update(snapshot);

        if (snapshot.Current is { } current)
        {
            var percent = (int)Math.Round(snapshot.Companion.StageProgress * 100);
            _tray.ToolTipText = string.Create(
                CultureInfo.InvariantCulture,
                $"#{current.SpeciesId} {percent}% · budget {TokenFormat.Compact(snapshot.Available)} · today {TokenFormat.Compact(snapshot.Today.Total)}");

            if (current.SpritePath is not null && !string.Equals(current.SpritePath, _iconPath, StringComparison.Ordinal))
            {
                try
                {
                    _tray.Icon = TrayIconRenderer.FromSprite(current.SpritePath);
                }
                catch (Exception ex) when (ex is ArgumentException or IOException)
                {
                    // A sprite the cache accepted but the decoder rejects: keep whatever icon is showing.
                    _window.Status = "Sprite unreadable: " + Path.GetFileName(current.SpritePath);
                }

                _iconPath = current.SpritePath;
            }
        }
        else
        {
            _tray.ToolTipText = string.Create(
                CultureInfo.InvariantCulture,
                $"{snapshot.OfferCount} eggs waiting · budget {TokenFormat.Compact(snapshot.Available)} · today {TokenFormat.Compact(snapshot.Today.Total)}");

            if (_iconPath is not null)
            {
                _tray.Icon = TrayIconRenderer.Placeholder();
                _iconPath = null;
            }
        }
    }
}
