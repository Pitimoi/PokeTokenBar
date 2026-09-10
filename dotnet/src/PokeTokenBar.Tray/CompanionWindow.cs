using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using PokeTokenBar.Core.Companions;

namespace PokeTokenBar.Tray;

/// <summary>
/// The popup behind the tray icon: borderless, docked in a corner, gone as soon as it loses
/// focus. Laid out like the VS Code view: budget, companion (or eggs), feed, usage, Pokédex.
/// </summary>
internal sealed class CompanionWindow : Window
{
    /// <summary>Largest square the companion sprite may occupy, in logical pixels.</summary>
    private const int SpriteBox = 160;
    private const int EdgeMargin = 12;

    /// <summary>
    /// Clicking the tray icon while the popup is open first deactivates the window, which hides
    /// it, and then delivers the click, which would reopen it. A hide this recent means the click
    /// was meant to close.
    /// </summary>
    private static readonly TimeSpan ReopenGuard = TimeSpan.FromMilliseconds(400);

    private readonly TextBlock _banked = Label(30, FontWeight.Bold);
    private readonly TextBlock _ledger = Label(12, opacity: 0.6);

    private readonly Image _sprite = new()
    {
        Width = SpriteBox,
        Height = SpriteBox,
        Stretch = Stretch.Uniform,
        HorizontalAlignment = HorizontalAlignment.Center,
    };

    private readonly TextBlock _name = Label(16, FontWeight.SemiBold);
    private readonly TextBlock _number = Label(12, opacity: 0.6);
    private readonly TextBlock _stage = Label(12, opacity: 0.6);
    private readonly ProgressBar _progress = new() { Minimum = 0, Maximum = 1, Height = 8, Margin = new Thickness(0, 10, 0, 6) };
    private readonly TextBlock _progressText = Label(12, opacity: 0.7);
    private readonly TextBlock _presses = Label(12, opacity: 0.7);
    private readonly Button _feed = new() { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0), Padding = new Thickness(18, 8) };
    private readonly StackPanel _companionPanel = new() { Spacing = 2 };

    private readonly Button[] _eggs = new Button[CompanionEconomy.OfferSize];
    private readonly TextBlock _offerHint = Label(12, opacity: 0.7);
    private readonly StackPanel _offerPanel = new() { Spacing = 2 };

    private readonly TextBlock _today = Value();
    private readonly TextBlock _week = Value();
    private readonly TextBlock _month = Value();

    private readonly TextBlock _footer = Label(11, opacity: 0.6);
    private readonly WrapPanel _pokedexGrid = new() { HorizontalAlignment = HorizontalAlignment.Center };
    private readonly ScrollViewer _pokedexScroller = new() { MaxHeight = 150, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private bool _pokedexCollapsed;
    private readonly Dictionary<string, Bitmap> _thumbnails = new(StringComparer.Ordinal);

    private readonly TextBlock _status = Label(11, opacity: 0.7);

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

    /// <summary>Raised when the popup opens, so what it shows is fresh.</summary>
    public event EventHandler? RefreshRequested;

    public event EventHandler? AdvanceRequested;

    /// <summary>Raised with the index of the egg chosen from the offer.</summary>
    public event Action<int>? HatchRequested;

    /// <summary>Transient message under the footer; empty hides it.</summary>
    public string Status
    {
        set
        {
            _status.Text = value;
            _status.IsVisible = value.Length > 0;
        }
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
        RefreshRequested?.Invoke(this, EventArgs.Empty);
    }

    public new void Hide()
    {
        _hiddenAt = DateTime.UtcNow;
        base.Hide();
    }

    public void Update(UsageSnapshot snapshot)
    {
        _banked.Text = TokenFormat.Compact(snapshot.Available);
        _ledger.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"banked · {TokenFormat.Compact(snapshot.Earned)} earned · {TokenFormat.Compact(snapshot.Spent)} spent");

        var hasCompanion = snapshot.Current is not null;
        _companionPanel.IsVisible = hasCompanion;
        _offerPanel.IsVisible = !hasCompanion;

        if (snapshot.Current is { } current)
        {
            var companion = snapshot.Companion;
            var percent = (int)Math.Round(companion.StageProgress * 100);
            var remaining = Math.Max(0, companion.StageThreshold - companion.TokensAtStage);
            var presses = (remaining + CompanionEconomy.ClickCost - 1) / CompanionEconomy.ClickCost;

            _name.Text = current.Name ?? string.Empty;
            _number.Text = "#" + current.SpeciesId.ToString(CultureInfo.InvariantCulture);
            _stage.Text = string.Create(CultureInfo.InvariantCulture, $"{companion.Rarity} · stage {companion.SafeStageIndex + 1} / {companion.TotalForms}");
            _progress.Value = companion.StageProgress;
            _progressText.Text = string.Create(
                CultureInfo.InvariantCulture,
                $"{TokenFormat.Compact(companion.TokensAtStage)} / {TokenFormat.Compact(companion.StageThreshold)} · {percent}%");
            _presses.Text = string.Create(
                CultureInfo.InvariantCulture,
                $"{presses} {(presses == 1 ? "press" : "presses")} to {(companion.IsFinalStage ? "complete" : "evolve")}");
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
                ? "Pick one to hatch it · " + TokenFormat.Compact(CompanionEconomy.HatchPrice)
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"Hatching costs {TokenFormat.Compact(CompanionEconomy.HatchPrice)} · {TokenFormat.Compact(CompanionEconomy.HatchPrice - snapshot.Available)} to go");
        }

        _today.Text = TokenFormat.Compact(snapshot.Today.Total);
        _week.Text = TokenFormat.Compact(snapshot.Week.Total);
        _month.Text = TokenFormat.Compact(snapshot.Month.Total);

        _footer.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"POKÉDEX {snapshot.Pokedex.Count} · COMPLETED {snapshot.GraduatedCount}");

        // Forms of the line still being raised are shown in grey; everything else came from a
        // completed line.
        var inProgress = snapshot.Current is null
            ? new HashSet<int>()
            : snapshot.Companion.ReachedForms.ToHashSet();
        UpdatePokedex(snapshot.Pokedex, inProgress);

        Status = EventLine(snapshot);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // The window is reused for the life of the process; closing just hides it.
        e.Cancel = true;
        Hide();
        base.OnClosing(e);
    }

    private static string EventLine(UsageSnapshot snapshot)
    {
        if (snapshot.Refusal is not null)
        {
            return snapshot.Refusal;
        }

        if (snapshot.HatchedSpeciesId is not null)
        {
            return "It hatched!";
        }

        if (snapshot.GraduatedSpeciesId is not null)
        {
            return "A line completed!";
        }

        return snapshot.Evolutions.Count > 0 ? "It evolved!" : string.Empty;
    }

    /// <summary>One cell per owned species: its sprite (or a blank) over its number, name on hover.</summary>
    private void UpdatePokedex(IReadOnlyList<SpeciesInfo> pokedex, HashSet<int> inProgress)
    {
        _pokedexScroller.IsVisible = pokedex.Count > 0 && !_pokedexCollapsed;
        _pokedexGrid.Children.Clear();

        foreach (var species in pokedex)
        {
            var pending = inProgress.Contains(species.SpeciesId);
            var image = new Image { Width = 48, Height = 48, Stretch = Stretch.Uniform, Opacity = pending ? 0.75 : 1 };
            RenderOptions.SetBitmapInterpolationMode(image, BitmapInterpolationMode.None);
            if (species.SpritePath is not null)
            {
                image.Source = Thumbnail(species.SpritePath, grayscale: pending);
            }

            var cell = new StackPanel
            {
                Width = 60,
                Margin = new Thickness(2),
                Children =
                {
                    image,
                    new TextBlock
                    {
                        Text = "#" + species.SpeciesId.ToString(CultureInfo.InvariantCulture),
                        FontSize = 11,
                        Opacity = 0.7,
                        HorizontalAlignment = HorizontalAlignment.Center,
                    },
                },
            };
            ToolTip.SetTip(cell, species.Name ?? "#" + species.SpeciesId.ToString(CultureInfo.InvariantCulture));
            _pokedexGrid.Children.Add(cell);
        }
    }

    private Bitmap? Thumbnail(string path, bool grayscale)
    {
        var key = grayscale ? path + "#gray" : path;
        if (_thumbnails.TryGetValue(key, out var cached))
        {
            return cached;
        }

        try
        {
            Bitmap bitmap;
            if (grayscale)
            {
                using var stream = File.OpenRead(path);
                var writeable = WriteableBitmap.Decode(stream);
                Desaturate(writeable);
                bitmap = writeable;
            }
            else
            {
                bitmap = new Bitmap(path);
            }

            _thumbnails[key] = bitmap;
            return bitmap;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException)
        {
            return null;
        }
    }

    /// <summary>Replaces every pixel's colour with its luminance, keeping alpha.</summary>
    private static unsafe void Desaturate(WriteableBitmap bitmap)
    {
        using var buffer = bitmap.Lock();
        var pixels = (byte*)buffer.Address;
        for (var y = 0; y < buffer.Size.Height; y++)
        {
            var row = pixels + y * buffer.RowBytes;
            for (var x = 0; x < buffer.Size.Width; x++)
            {
                var p = row + x * 4;
                // Channel order does not matter for a luminance average; the alpha stays put.
                var grey = (byte)((p[0] * 30 + p[1] * 59 + p[2] * 11) / 100);
                p[0] = grey;
                p[1] = grey;
                p[2] = grey;
            }
        }
    }

    /// <summary>
    /// The sprite cropped to the creature and blown up by a whole number of pixels, so it fills
    /// the box without the canvas padding or uneven scaling.
    /// </summary>
    private void ShowSprite(string path)
    {
        try
        {
            var previous = _sprite.Source as IDisposable;
            var cropped = TrayIconRenderer.CropSquare(path);
            var scale = Math.Max(1, SpriteBox / cropped.PixelSize.Width);
            _sprite.Width = _sprite.Height = cropped.PixelSize.Width * scale;
            _sprite.Source = cropped;
            previous?.Dispose();
        }
        catch (Exception ex) when (ex is ArgumentException or IOException)
        {
            Status = "Sprite unreadable: " + System.IO.Path.GetFileName(path);
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
        _sprite.Margin = new Thickness(0, 8, 0, 8);
        _companionPanel.Children.Add(_sprite);
        _companionPanel.Children.Add(_name);
        _companionPanel.Children.Add(_number);
        _companionPanel.Children.Add(_stage);
        _companionPanel.Children.Add(_progress);
        _companionPanel.Children.Add(_progressText);
        _companionPanel.Children.Add(_presses);
        _companionPanel.Children.Add(_feed);

        var eggRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 16,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 28, 0, 12),
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

        _offerPanel.Children.Add(Label(16, FontWeight.SemiBold, text: "Choose an egg"));
        _offerPanel.Children.Add(eggRow);
        _offerPanel.Children.Add(_offerHint);

        var usage = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 18, 0, 0),
        };
        AddRow(usage, 0, "Today", _today);
        AddRow(usage, 1, "Week", _week);
        AddRow(usage, 2, "Month", _month);

        _footer.Margin = new Thickness(0, 22, 0, 0);
        _footer.Cursor = new Cursor(StandardCursorType.Hand);
        _footer.PointerPressed += (_, _) =>
        {
            _pokedexCollapsed = !_pokedexCollapsed;
            _pokedexScroller.IsVisible = !_pokedexCollapsed && _pokedexGrid.Children.Count > 0;
        };
        ToolTip.SetTip(_footer, "Click to show or hide the Pokédex");
        _pokedexScroller.Content = _pokedexGrid;
        _pokedexScroller.Margin = new Thickness(0, 8, 0, 0);

        _status.IsVisible = false;
        _status.Margin = new Thickness(0, 8, 0, 0);

        return new Border
        {
            Padding = new Thickness(20, 22, 20, 18),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.Parse("#66808080")),
            Child = new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    _banked,
                    _ledger,
                    _companionPanel,
                    _offerPanel,
                    usage,
                    _footer,
                    _pokedexScroller,
                    _status,
                },
            },
        };
    }

    private static void AddRow(Grid grid, int row, string label, TextBlock value)
    {
        var caption = new TextBlock { Text = label, Opacity = 0.7, TextAlignment = TextAlignment.Right, Margin = new Thickness(0, 2, 14, 2) };
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
        FontWeight = FontWeight.SemiBold,
        TextAlignment = TextAlignment.Left,
        Margin = new Thickness(0, 2, 0, 2),
    };
}
