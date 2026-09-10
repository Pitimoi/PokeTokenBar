using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using PokeTokenBar.Core.Companions;

namespace PokeTokenBar.Tray;

/// <summary>
/// The popup behind the tray icon: borderless, docked in a corner, gone as soon as it loses
/// focus. Shows either the eggs on offer or the active companion, plus budget and usage.
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
    private readonly Button _feed = new() { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };
    private readonly StackPanel _companionPanel = new() { Spacing = 2 };

    private readonly Button[] _eggs = new Button[CompanionEconomy.OfferSize];
    private readonly TextBlock _offerHint = Label(12, opacity: 0.7);
    private readonly StackPanel _offerPanel = new() { Spacing = 2 };

    private readonly TextBlock _budget = Label(13);
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

    public event EventHandler? AdvanceRequested;

    /// <summary>Raised with the index of the egg chosen from the offer.</summary>
    public event Action<int>? HatchRequested;

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
        var hasCompanion = snapshot.Current is not null;
        _companionPanel.IsVisible = hasCompanion;
        _offerPanel.IsVisible = !hasCompanion;

        if (snapshot.Current is { } current)
        {
            var companion = snapshot.Companion;
            var percent = (int)Math.Round(companion.StageProgress * 100);

            _species.Text = string.Create(CultureInfo.InvariantCulture, $"#{current.SpeciesId} · {companion.Rarity}");
            _stage.Text = string.Create(CultureInfo.InvariantCulture, $"Stage {companion.SafeStageIndex + 1} of {companion.TotalForms}");
            _progress.Value = companion.StageProgress;
            _progressText.Text = string.Create(
                CultureInfo.InvariantCulture,
                $"{TokenFormat.Compact(companion.TokensAtStage)} / {TokenFormat.Compact(companion.StageThreshold)} · {percent}%");
            _feed.Content = "Feed " + TokenFormat.Compact(CompanionEconomy.ClickCost);
            _feed.IsEnabled = snapshot.CanAdvance;

            if (current.SpritePath is not null && !string.Equals(current.SpritePath, _spritePath, StringComparison.Ordinal))
            {
                ShowSprite(current.SpritePath);
                _spritePath = current.SpritePath;
            }
        }
        else
        {
            for (var i = 0; i < _eggs.Length; i++)
            {
                _eggs[i].IsVisible = i < snapshot.OfferCount;
                _eggs[i].IsEnabled = snapshot.CanHatch;
            }

            _offerHint.Text = snapshot.CanHatch
                ? "Pick one to hatch it · " + TokenFormat.Compact(CompanionEconomy.HatchPrice) + " tokens"
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"Hatching costs {TokenFormat.Compact(CompanionEconomy.HatchPrice)} · {TokenFormat.Compact(CompanionEconomy.HatchPrice - snapshot.Available)} to go");
        }

        _budget.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"Budget {TokenFormat.Compact(snapshot.Available)} · earned {TokenFormat.Compact(snapshot.Earned)} · spent {TokenFormat.Compact(snapshot.Spent)}");

        _today.Text = Amount(snapshot.Today.Total, snapshot.Today.Cost);
        _week.Text = Amount(snapshot.Week.Total, snapshot.Week.Cost);
        _month.Text = Amount(snapshot.Month.Total, snapshot.Month.Cost);

        _status.Text = StatusLine(snapshot);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // The window is reused for the life of the process; closing just hides it.
        e.Cancel = true;
        Hide();
        base.OnClosing(e);
    }

    private static string StatusLine(UsageSnapshot snapshot)
    {
        if (snapshot.Refusal is not null)
        {
            return snapshot.Refusal;
        }

        var status = "Updated " + snapshot.ScannedAt.ToString("HH:mm", CultureInfo.InvariantCulture);
        if (snapshot.GraduatedCount > 0)
        {
            status += string.Create(CultureInfo.InvariantCulture, $" · {snapshot.GraduatedCount} lines completed");
        }

        if (snapshot.HatchedSpeciesId is not null)
        {
            status += " · It hatched!";
        }
        else if (snapshot.GraduatedSpeciesId is not null)
        {
            status += " · A line completed!";
        }
        else if (snapshot.Evolutions.Count > 0)
        {
            status += " · It evolved!";
        }

        return status;
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
            _status.Text = "Sprite unreadable: " + System.IO.Path.GetFileName(path);
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
        _feed.Click += (_, _) => AdvanceRequested?.Invoke(this, EventArgs.Empty);
        _companionPanel.Children.Add(_sprite);
        _companionPanel.Children.Add(_species);
        _companionPanel.Children.Add(_stage);
        _companionPanel.Children.Add(_progress);
        _companionPanel.Children.Add(_progressText);
        _companionPanel.Children.Add(_feed);

        var eggRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 16,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 24, 0, 12),
        };
        for (var i = 0; i < _eggs.Length; i++)
        {
            var index = i;
            _eggs[i] = new Button
            {
                Padding = new Thickness(14, 10),
                Content = new Ellipse
                {
                    Width = 30,
                    Height = 40,
                    Fill = new SolidColorBrush(Color.Parse("#F3E5C3")),
                    Stroke = new SolidColorBrush(Color.Parse("#B8A47C")),
                    StrokeThickness = 1.5,
                },
            };
            _eggs[i].Click += (_, _) => HatchRequested?.Invoke(index);
            eggRow.Children.Add(_eggs[i]);
        }

        _offerPanel.Children.Add(Label(18, FontWeight.SemiBold, text: "Choose an egg"));
        _offerPanel.Children.Add(eggRow);
        _offerPanel.Children.Add(_offerHint);

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

        _budget.Margin = new Thickness(0, 12, 0, 0);

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
                    _companionPanel,
                    _offerPanel,
                    _budget,
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

    private static TextBlock Label(double size, FontWeight weight = FontWeight.Normal, double opacity = 1, string? text = null) => new()
    {
        Text = text,
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
