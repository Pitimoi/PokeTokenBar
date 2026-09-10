using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace PokeTokenBar.Tray;

/// <summary>
/// The popup behind the tray icon: borderless, anchored to the top-right corner, gone as soon as
/// it loses focus. Left-clicking the icon toggles it.
/// </summary>
internal sealed class CompanionWindow : Window
{
    private const double SpriteScale = 3;
    private const int EdgeMargin = 12;

    /// <summary>
    /// Clicking the tray icon while the popup is open first deactivates the window, which hides
    /// it, and then delivers the click, which would reopen it. A hide this recent means the click
    /// was meant to close.
    /// </summary>
    private static readonly TimeSpan ReopenGuard = TimeSpan.FromMilliseconds(400);

    private readonly Image _sprite = new()
    {
        Width = 96 * SpriteScale,
        Height = 96 * SpriteScale,
        Stretch = Stretch.Uniform,
        HorizontalAlignment = HorizontalAlignment.Center,
    };

    private readonly TextBlock _species = Label(18, FontWeight.SemiBold);
    private readonly TextBlock _stage = Label(13, opacity: 0.7);
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 1, Height = 8, Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBlock _progressText = Label(12, opacity: 0.7);
    private readonly TextBlock _today = Value();
    private readonly TextBlock _week = Value();
    private readonly TextBlock _month = Value();
    private readonly TextBlock _status = Label(11, opacity: 0.55);

    private string? _spritePath;
    private DateTime _hiddenAt;

    public CompanionWindow()
    {
        Title = "PokeTokenBar";
        Width = 320;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        SystemDecorations = SystemDecorations.None;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        RenderOptions.SetBitmapInterpolationMode(_sprite, BitmapInterpolationMode.None);

        Content = Build();
        Deactivated += (_, _) => Hide();
    }

    public event EventHandler? RefreshRequested;

    public event EventHandler? QuitRequested;

    public string Status
    {
        set => _status.Text = value;
    }

    public void Toggle()
    {
        if (IsVisible)
        {
            Hide();
            return;
        }

        if (DateTime.UtcNow - _hiddenAt < ReopenGuard)
        {
            return;
        }

        Place();
        Show();
        Activate();
    }

    public new void Hide()
    {
        _hiddenAt = DateTime.UtcNow;
        base.Hide();
    }

    public void Update(UsageSnapshot snapshot)
    {
        var companion = snapshot.Companion;
        var percent = (int)Math.Round(companion.StageProgress * 100);

        _species.Text = string.Create(CultureInfo.InvariantCulture, $"#{companion.CurrentSpeciesId} · {companion.Rarity}");
        _stage.Text = string.Create(CultureInfo.InvariantCulture, $"Stage {companion.SafeStageIndex + 1} of {companion.TotalForms}");
        _progress.Value = companion.StageProgress;
        _progressText.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"{TokenFormat.Compact(companion.TokensAtStage)} / {TokenFormat.Compact(companion.StageThreshold)} · {percent}%");

        _today.Text = Amount(snapshot.Today.Total, snapshot.Today.Cost);
        _week.Text = Amount(snapshot.Week.Total, snapshot.Week.Cost);
        _month.Text = Amount(snapshot.Month.Total, snapshot.Month.Cost);

        var status = "Updated " + snapshot.ScannedAt.ToString("HH:mm", CultureInfo.InvariantCulture);
        if (snapshot.GraduatedCount > 0)
        {
            status += string.Create(CultureInfo.InvariantCulture, $" · {snapshot.GraduatedCount} lines completed");
        }

        if (snapshot.GraduatedSpeciesId is not null)
        {
            status += " · A line completed!";
        }
        else if (snapshot.Evolutions.Count > 0)
        {
            status += " · It evolved!";
        }

        _status.Text = status;

        if (snapshot.SpritePath is not null && !string.Equals(snapshot.SpritePath, _spritePath, StringComparison.Ordinal))
        {
            ShowSprite(snapshot.SpritePath);
            _spritePath = snapshot.SpritePath;
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // The window is reused for the life of the process; closing just hides it.
        e.Cancel = true;
        Hide();
        base.OnClosing(e);
    }

    private void ShowSprite(string path)
    {
        try
        {
            var previous = _sprite.Source as IDisposable;
            _sprite.Source = new Bitmap(path);
            previous?.Dispose();
        }
        catch (Exception ex) when (ex is ArgumentException or IOException)
        {
            _status.Text = "Sprite unreadable: " + Path.GetFileName(path);
        }
    }

    /// <summary>
    /// Docks in the corner nearest the tray: bottom-right where the taskbar lives on Windows,
    /// top-right under the panel or menu bar elsewhere.
    /// </summary>
    private void Place()
    {
        var screen = Screens.Primary ?? (Screens.All.Count > 0 ? Screens.All[0] : null);
        if (screen is null)
        {
            return;
        }

        var area = screen.WorkingArea;
        var width = (int)(Width * screen.Scaling);
        var margin = (int)(EdgeMargin * screen.Scaling);
        var x = area.Right - width - margin;

        if (!OperatingSystem.IsWindows())
        {
            Position = new PixelPoint(x, area.Y + margin);
            return;
        }

        // Height is only known once laid out; before the first show, estimate it.
        var height = (int)((Bounds.Height > 0 ? Bounds.Height : 560) * screen.Scaling);
        Position = new PixelPoint(x, area.Bottom - height - margin);
    }

    private Border Build()
    {
        var refresh = new Button { Content = "Refresh" };
        refresh.Click += (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty);

        var quit = new Button { Content = "Quit" };
        quit.Click += (_, _) => QuitRequested?.Invoke(this, EventArgs.Empty);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0),
            Children = { refresh, quit },
        };

        var usage = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
            Margin = new Thickness(0, 12, 0, 0),
        };
        AddRow(usage, 0, "Today", _today);
        AddRow(usage, 1, "This week", _week);
        AddRow(usage, 2, "This month", _month);

        return new Border
        {
            Padding = new Thickness(20),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.Parse("#66808080")),
            Child = new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    _sprite,
                    _species,
                    _stage,
                    _progress,
                    _progressText,
                    usage,
                    _status,
                    buttons,
                },
            },
        };
    }

    private static void AddRow(Grid grid, int row, string label, TextBlock value)
    {
        var caption = new TextBlock { Text = label, Opacity = 0.7, Margin = new Thickness(0, 2, 16, 2) };
        Grid.SetRow(caption, row);
        Grid.SetColumn(caption, 0);
        Grid.SetRow(value, row);
        Grid.SetColumn(value, 1);
        grid.Children.Add(caption);
        grid.Children.Add(value);
    }

    private static TextBlock Label(double size, FontWeight weight = FontWeight.Normal, double opacity = 1) => new()
    {
        FontSize = size,
        FontWeight = weight,
        Opacity = opacity,
        TextAlignment = TextAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        Margin = new Thickness(0, 2, 0, 0),
    };

    private static TextBlock Value() => new()
    {
        TextAlignment = TextAlignment.Right,
        HorizontalAlignment = HorizontalAlignment.Right,
        Margin = new Thickness(0, 2, 0, 2),
    };

    private static string Amount(long tokens, double cost) =>
        string.Create(CultureInfo.InvariantCulture, $"{TokenFormat.Compact(tokens)} tokens · {TokenFormat.Cost(cost)}");
}
