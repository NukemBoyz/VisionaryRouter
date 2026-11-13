using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Automation;

namespace VisionaryRouter.Desktop;

public sealed record DeckState(IReadOnlyList<Device> Devices, Guid? Source, IReadOnlySet<Guid> Destinations, bool Ready, bool Busy, string Result);

// The deck shares the router's selection and verified routing operation. It never switches inputs.
public sealed class DeckWindow : Window
{
    readonly Action<Guid> selectSource;
    readonly Action<Guid> toggleDestination;
    readonly Action<bool> selectAll;
    readonly Func<Task> route;
    readonly Func<DeckState> read;
    readonly WrapPanel sources = new(), destinations = new();
    readonly TextBlock summary = Text("Select an encoder", 17), result = Text("", 12);
    readonly Button send = new() { Content = "Route", MinWidth = 160 };
    readonly TextBlock sourcePageLabel = Text("", 12), destinationPageLabel = Text("", 12);
    readonly Button sourcePrevious = new(), sourceNext = new(), destinationPrevious = new(), destinationNext = new();
    readonly Slider size = new() { Minimum = 120, Maximum = 240, Value = 170, Width = 110, TickFrequency = 10, IsSnapToTickEnabled = true };
    int sourcePage, destinationPage;
    const int PageSize = 12;
    static Brush Color(string value) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
    static TextBlock Text(string value, double font) => new() { Text = value, FontSize = font, Foreground = Color("#E5EAF3"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4) };

    public DeckWindow(Func<DeckState> read, Action<Guid> selectSource, Action<Guid> toggleDestination, Action<bool> selectAll, Func<Task> route)
    {
        this.read = read; this.selectSource = selectSource; this.toggleDestination = toggleDestination; this.selectAll = selectAll; this.route = route;
        Title = "Visionary Router • Deck"; Width = 860; Height = 680; MinWidth = 420; MinHeight = 420;
        Background = Color("#10151E"); FontFamily = new FontFamily("Segoe UI");
        var root = new DockPanel { Margin = new Thickness(16) }; Content = root;
        var toolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) }; DockPanel.SetDock(toolbar, Dock.Top); root.Children.Add(toolbar);
        toolbar.Children.Add(Text("ROUTING DECK", 19)); toolbar.Children.Add(Text("Button size", 12)); toolbar.Children.Add(size);
        AutomationProperties.SetName(size, "Deck button size");
        var clear = MakeButton("Clear selection", () => { if (!read().Busy) { selectAll(false); } }); toolbar.Children.Add(clear);
        var bottom = new StackPanel { Margin = new Thickness(0, 10, 0, 0) }; DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        bottom.Children.Add(summary); send.Padding = new Thickness(20, 12, 20, 12); send.Margin = new Thickness(0, 6, 0, 6); send.FontSize = 18;
        send.Background = Color("#245FA3"); send.BorderBrush = Color("#75B3FF");
        send.Click += async (_, _) => { if (!send.IsEnabled) return; send.IsEnabled = false; await this.route(); UpdateState(); };
        bottom.Children.Add(send); bottom.Children.Add(new ScrollViewer { Content = result, MaxHeight = 64, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var sections = new Grid(); sections.RowDefinitions.Add(new RowDefinition()); sections.RowDefinitions.Add(new RowDefinition()); root.Children.Add(sections);
        AddSection(sections, 0, "1  Encoder", sources, sourcePageLabel, sourcePrevious, sourceNext, () => sourcePage--, () => sourcePage++);
        AddSection(sections, 1, "2  Decoders", destinations, destinationPageLabel, destinationPrevious, destinationNext, () => destinationPage--, () => destinationPage++);
        size.ValueChanged += (_, _) => ResizeTiles(); SizeChanged += (_, _) => ResizeTiles();
        UpdateState();
    }

    Button MakeButton(string text, Action action)
    {
        var button = new Button { Content = text, Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(4) };
        button.Click += (_, _) => { action(); UpdateState(); }; return button;
    }

    void AddSection(Grid root, int row, string title, WrapPanel tiles, TextBlock pageLabel, Button previous, Button next, Action back, Action forward)
    {
        var section = new DockPanel { Margin = new Thickness(0, 0, 0, 8) }; Grid.SetRow(section, row); root.Children.Add(section);
        var heading = new WrapPanel(); DockPanel.SetDock(heading, Dock.Top); section.Children.Add(heading); heading.Children.Add(Text(title, 16));
        previous.Content = "‹"; next.Content = "›"; previous.Padding = next.Padding = new Thickness(10, 3, 10, 3);
        previous.Margin = next.Margin = new Thickness(4); AutomationProperties.SetName(previous, "Previous " + title); AutomationProperties.SetName(next, "Next " + title);
        previous.Click += (_, _) => { back(); UpdateState(); }; next.Click += (_, _) => { forward(); UpdateState(); };
        heading.Children.Add(previous); heading.Children.Add(pageLabel); heading.Children.Add(next);
        if (row == 1) heading.Children.Add(MakeButton("All decoders", () => { if (!read().Busy) selectAll(true); }));
        var scroll = new ScrollViewer { Content = tiles, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        section.Children.Add(scroll); scroll.SizeChanged += (_, _) => ResizeTiles();
    }

    public void UpdateState()
    {
        var state = read();
        Fill(sources, state.Devices.Where(d => d.Enabled && d.Type == "Encoder").OrderBy(d => d.SortOrder).ToList(), ref sourcePage, sourcePageLabel, sourcePrevious, sourceNext, state, true);
        Fill(destinations, state.Devices.Where(d => d.Enabled && d.Type == "Decoder").OrderBy(d => d.SortOrder).ToList(), ref destinationPage, destinationPageLabel, destinationPrevious, destinationNext, state, false);
        var sourceName = state.Devices.FirstOrDefault(d => d.Id == state.Source)?.Name;
        summary.Text = sourceName is null ? "Select an encoder, then one or more decoders." : $"{sourceName} → {state.Destinations.Count} decoder(s)";
        send.Content = state.Busy ? "Routing…" : $"Route to {state.Destinations.Count} decoder(s)";
        send.IsEnabled = state.Ready && !state.Busy && sourceName != null && state.Destinations.Count > 0;
        result.Text = state.Ready ? state.Result : "Router disconnected — routing unavailable.";
        ResizeTiles();
    }

    void Fill(WrapPanel panel, List<Device> devices, ref int page, TextBlock label, Button previous, Button next, DeckState state, bool encoder)
    {
        var pages = Math.Max(1, (devices.Count + PageSize - 1) / PageSize); page = Math.Clamp(page, 0, pages - 1);
        label.Text = $"{page + 1} / {pages}"; previous.IsEnabled = page > 0; next.IsEnabled = page < pages - 1;
        panel.Children.Clear();
        foreach (var device in devices.Skip(page * PageSize).Take(PageSize))
        {
            bool selected = encoder ? state.Source == device.Id : state.Destinations.Contains(device.Id);
            var body = new StackPanel(); body.Children.Add(Text((selected ? "✓  " : "") + device.Name, 17));
            var health = Text(device.OnlineState, 11); health.Foreground = Color(device.OnlineState == "Online" ? "#8DD8B6" : "#EDAB9F"); body.Children.Add(health);
            if (!encoder) body.Children.Add(Text("Showing: " + (device.CurrentSourceName ?? "No source reported"), 12));
            var button = MakeButton("", () => { if (read().Busy) return; if (encoder) selectSource(device.Id); else toggleDestination(device.Id); });
            button.Content = body; button.MinHeight = 100; button.IsEnabled = !state.Busy;
            button.Background = Color(selected ? "#254F77" : "#202C3C"); button.BorderBrush = Color(selected ? "#85C1FF" : "#405570"); button.BorderThickness = new Thickness(selected ? 2 : 1);
            AutomationProperties.SetName(button, device.Name + (selected ? " selected" : "") + (encoder ? " encoder" : " decoder")); panel.Children.Add(button);
        }
        if (devices.Count == 0) panel.Children.Add(Text(encoder ? "No enabled encoders. Add devices in the main window." : "No enabled decoders. Add devices in the main window.", 14));
    }

    void ResizeTiles()
    {
        foreach (var panel in new[] { sources, destinations })
        {
            double available = Math.Max(200, panel.ActualWidth > 0 ? panel.ActualWidth : ActualWidth - 64);
            int columns = Math.Max(1, (int)(available / (size.Value + 8)));
            double width = Math.Max(100, available / columns - 10);
            foreach (var button in panel.Children.OfType<Button>()) button.Width = width;
        }
    }
}

public sealed partial class RouterWindow
{
    DeckWindow? deck;
    DeckState ReadDeck() => new(devices, source, new HashSet<Guid>(selected), connected, busy, status.Text);
    void OpenDeck()
    {
        if (deck != null) { deck.Activate(); return; }
        deck = new DeckWindow(ReadDeck,
            id => { if (busy) return; source = id; Render(); },
            id => { if (busy) return; if (!selected.Add(id)) selected.Remove(id); Render(); },
            all => { if (busy) return; selected.Clear(); if (all) foreach (var d in devices.Where(d => d.Enabled && d.Type == "Decoder")) selected.Add(d.Id); else source = null; Render(); },
            Route) { Owner = this };
        deck.Closed += (_, _) => deck = null;
        deck.Show();
    }
}
