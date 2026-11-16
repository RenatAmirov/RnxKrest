using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace RnxKrest;

public partial class MainWindow : Window
{
    private const double SlotSize = 57;
    private const double BoardInset = 49;
    private const double BoardSize = 2 * BoardInset + Puzzle.Size * SlotSize;

    private static readonly Brush Track = BrushFrom("#0C1624");
    private static readonly Brush Slot = BrushFrom("#0E1A29");
    private static readonly Brush SlotOutline = BrushFrom("#23354A");
    private static readonly Brush RailOutline = BrushFrom("#456079");
    private static readonly Brush Mint = BrushFrom("#93E8C7");
    private static readonly Brush TileEdge = BrushFrom("#55FFFFFF");
    private static readonly Brush[] TileFaces =
    [
        TileGradient("#9DF0DA", "#5CCBB0"),
        TileGradient("#FFDD98", "#F4B85A"),
        TileGradient("#D1C8FB", "#A79AE8")
    ];
    private static readonly Brush[] TileInk =
    [
        BrushFrom("#226659"), BrushFrom("#80511F"), BrushFrom("#55458E")
    ];
    private static readonly Geometry[] TileSymbols =
    [
        Geometry.Parse("M 8,1 L 15,8 L 8,15 L 1,8 Z"),
        Geometry.Parse("M 8,1 A 7,7 0 1 1 7.999,1 Z"),
        Geometry.Parse("M 8,1 L 15,14 L 1,14 Z")
    ];
    private static readonly Direction[] Directions =
    [
        new(Axis.Horizontal, -1, "←", "влево"),
        new(Axis.Horizontal,  1, "→", "вправо"),
        new(Axis.Vertical,   -1, "↑", "вверх"),
        new(Axis.Vertical,    1, "↓", "вниз")
    ];

    private readonly Canvas board = new() { Width = BoardSize, Height = BoardSize };
    private readonly Canvas tiles = new() { IsHitTestVisible = false };
    private readonly Canvas rails = new() { IsHitTestVisible = false };
    private readonly Dictionary<Direction, Button> directionButtons = [];
    private readonly Stack<Turn> history = new();
    private readonly Stopwatch stopwatch = new();
    private readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromMilliseconds(200) };

    private Position position;
    private Move? hint;

    public MainWindow()
    {
        InitializeComponent();
        Height = Math.Min(Height, SystemParameters.WorkArea.Height);
        Width = Math.Min(Width, SystemParameters.WorkArea.Width);

        BoardHost.Children.Add(board);
        DrawTrack(board, BoardInset);
        board.Children.Add(rails);
        board.Children.Add(tiles);
        AddDirectionButtons();
        TargetHost.Children.Add(CreateTargetBoard());
        ProgressFill.Loaded += (_, _) => UpdateProgress();
        ((FrameworkElement)ProgressFill.Parent).SizeChanged += (_, _) => UpdateProgress();

        clock.Tick += (_, _) => UpdateTime();
        Closed += (_, _) => clock.Stop();
        StartNewGame(introductory: true);
    }

    private void StartNewGame(bool introductory)
    {
        position = Puzzle.NewPosition(introductory, position);
        history.Clear();
        hint = null;
        stopwatch.Reset();
        clock.Stop();
        UpdateScreen();
    }

    private void Slide(Direction direction, bool toEnd)
    {
        if (position == Puzzle.Goal)
            return;

        int offset = position.Offset(direction.Axis);
        int destination = toEnd ? direction.Sign : Math.Clamp(offset + direction.Sign, -1, 1);
        if (destination == offset)
            return;

        var move = new Move(direction.Axis, destination);
        var previous = position;
        position = Puzzle.Apply(position, move);
        history.Push(new Turn(previous, move));
        hint = null;
        if (position == Puzzle.Goal)
        {
            stopwatch.Stop();
            clock.Stop();
        }
        else
        {
            stopwatch.Start();
            clock.Start();
        }

        UpdateScreen(previous, move);
    }

    private void UndoClicked(object sender, RoutedEventArgs e) => Undo();

    private void Undo()
    {
        if (!history.TryPop(out Turn turn))
            return;

        var previous = position;
        position = turn.Before;
        hint = null;
        if (history.Count == 0)
        {
            stopwatch.Reset();
            clock.Stop();
        }
        else
        {
            stopwatch.Start();
            clock.Start();
        }

        UpdateScreen(previous, new Move(turn.Move.Axis, position.Offset(turn.Move.Axis)));
    }

    private void HintClicked(object sender, RoutedEventArgs e) => ShowHint();

    private void ShowHint()
    {
        hint = Puzzle.Hint(position);
        UpdateStatus();
        UpdateDirectionButtons();
    }

    private void NewClicked(object sender, RoutedEventArgs e) => StartNewGame(introductory: false);
    private void HelpClicked(object sender, RoutedEventArgs e) => ShowHelp();

    private void ShowHelp()
    {
        var close = new Button
        {
            Content = "Понятно",
            MinWidth = 112,
            IsDefault = true,
            IsCancel = true,
            HorizontalAlignment = HorizontalAlignment.Right,
            Style = (Style)FindResource("PrimaryCommand")
        };
        var body = new StackPanel { Margin = new Thickness(25) };
        body.Children.Add(new TextBlock
        {
            Text = "Как играть",
            FontSize = 25,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 14)
        });
        foreach (string line in new[]
        {
            "Соберите рисунок справа. Цвет и символ каждой фишки должны совпасть с образцом.",
            "На поле две пересекающиеся обоймы. Каждая стрелка сдвигает свою обойму вместе с восемью фишками на одну клетку. Фишки в общей части могут двигаться в обоих направлениях.",
            "Удерживайте Shift при нажатии стрелки или щелчке по ней, чтобы сдвинуть обойму до упора за один ход.",
            "Подсказка выделяет следующий ход по кратчайшему пути к решению."
        })
        {
            body.Children.Add(new TextBlock
            {
                Text = line,
                FontSize = 15,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 22,
                Foreground = (Brush)FindResource("Muted"),
                Margin = new Thickness(0, 0, 0, 13)
            });
        }
        close.Margin = new Thickness(0, 4, 0, 0);
        body.Children.Add(close);
        var dialog = new Window
        {
            Title = "Правила игры",
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Width = 480,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Background = (Brush)FindResource("Surface"),
            Foreground = (Brush)FindResource("Text"),
            FontFamily = FontFamily,
            Content = body
        };
        close.Click += (_, _) => dialog.Close();
        dialog.ShowDialog();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        bool control = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        switch (e.Key)
        {
            case Key.Left: Slide(Directions[0], shift); break;
            case Key.Right: Slide(Directions[1], shift); break;
            case Key.Up: Slide(Directions[2], shift); break;
            case Key.Down: Slide(Directions[3], shift); break;
            case Key.Z when control: Undo(); break;
            case Key.N when control: StartNewGame(introductory: false); break;
            case Key.H when !control: ShowHint(); break;
            case Key.F1: ShowHelp(); break;
            default: return;
        }
        e.Handled = true;
    }

    private void UpdateScreen(Position? previous = null, Move? move = null)
    {
        DrawPosition(previous, move);
        UpdateProgress();
        UpdateTime();
        MoveText.Text = history.Count.ToString();
        UndoButton.IsEnabled = history.Count > 0;
        HintButton.IsEnabled = position != Puzzle.Goal;
        UpdateStatus();
        UpdateDirectionButtons();
    }

    private void UpdateStatus()
    {
        if (position == Puzzle.Goal)
        {
            StatusText.Text = "Готово! Рисунок совпал с образцом.";
            StatusText.Foreground = Mint;
        }
        else if (hint is { } next)
        {
            int delta = next.Destination - position.Offset(next.Axis);
            Direction direction = GetDirection(next);
            string keys = Math.Abs(delta) == 2 ? $"Shift + {direction.Arrow}" : direction.Arrow;
            StatusText.Text = $"Подсказка · сдвиньте {direction.Name} ({keys})";
            StatusText.Foreground = Mint;
        }
        else
        {
            StatusText.Text = "Стрелки — сдвиг на клетку   ·   Shift + стрелка — до упора";
            StatusText.Foreground = (Brush)FindResource("Muted");
        }
    }

    private void UpdateProgress()
    {
        int correct = Puzzle.CountMatches(position);
        ProgressText.Text = $"{correct} / {Puzzle.TileCount}";
        if (ProgressFill.Parent is FrameworkElement track)
            ProgressFill.Width = track.ActualWidth * correct / Puzzle.TileCount;
    }

    private void UpdateTime()
    {
        TimeSpan elapsed = stopwatch.Elapsed;
        TimeText.Text = elapsed.TotalHours >= 1
            ? $"{(int)elapsed.TotalHours}:{elapsed.Minutes:00}:{elapsed.Seconds:00}"
            : $"{elapsed.Minutes:00}:{elapsed.Seconds:00}";
    }

    private Direction GetDirection(Move move) => Directions.First(direction =>
        direction.Axis == move.Axis && direction.Sign == Math.Sign(move.Destination - position.Offset(move.Axis)));

    private void AddDirectionButtons()
    {
        const double size = 41;
        double middle = (BoardSize - size) / 2;
        foreach (Direction direction in Directions)
        {
            var button = new Button
            {
                Width = size,
                Height = size,
                MinHeight = size,
                Padding = new Thickness(0),
                FontSize = 24,
                FontFamily = new FontFamily("Segoe UI"),
                Content = direction.Arrow,
                Style = (Style)FindResource("Command"),
                ToolTip = $"Сдвинуть {direction.Name} · Shift + щелчок до упора"
            };
            AutomationProperties.SetName(button, $"Сдвинуть {direction.Name}");
            button.Click += (_, _) => Slide(direction, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            directionButtons.Add(direction, button);

            double edge = direction.Sign < 0 ? 0 : BoardSize - size;
            if (direction.Axis == Axis.Horizontal)
                Place(board, button, edge, middle);
            else
                Place(board, button, middle, edge);
        }
    }

    private void UpdateDirectionButtons()
    {
        Direction? suggested = hint is { } move ? GetDirection(move) : null;
        foreach (var (direction, button) in directionButtons)
        {
            button.IsEnabled = position != Puzzle.Goal && position.Offset(direction.Axis) != direction.Sign;
            button.Style = (Style)FindResource(suggested == direction ? "PrimaryCommand" : "Command");
        }
    }

    private void DrawPosition(Position? previous, Move? move)
    {
        rails.Children.Clear();
        DrawSleeve(rails, Axis.Horizontal, position.Horizontal, BoardInset);
        DrawSleeve(rails, Axis.Vertical, position.Vertical, BoardInset);
        tiles.Children.Clear();

        if (previous is { } before && move is { } shift)
        {
            var originalCells = Puzzle.Cells(before);
            int delta = shift.Destination - before.Offset(shift.Axis);
            for (int index = 0; index < Puzzle.TileCount; index++)
            {
                Cell origin = originalCells[index];
                Cell destination = origin.Slide(shift.Axis, delta);
                AddTile(tiles, destination, before.ColorAt(index), BoardInset, origin);
            }
            return;
        }

        DrawTiles(tiles, position, BoardInset);
    }

    private static Canvas CreateTargetBoard()
    {
        const double inset = 5;
        double size = Puzzle.Size * SlotSize + inset * 2;
        var canvas = new Canvas { Width = size, Height = size };
        DrawTrack(canvas, inset);
        DrawSleeve(canvas, Axis.Horizontal, 0, inset);
        DrawSleeve(canvas, Axis.Vertical, 0, inset);
        DrawTiles(canvas, Puzzle.Goal, inset);
        AutomationProperties.SetName(canvas,
            "Целевой рисунок. В центре четыре круга, слева и справа ромбы, сверху и снизу треугольники.");
        return canvas;
    }

    private static void DrawTrack(Canvas canvas, double inset)
    {
        double core = inset + 2 * SlotSize;
        Place(canvas, RectangleShape(6 * SlotSize + 8, 2 * SlotSize + 8, Track, 12), inset - 4, core - 4);
        Place(canvas, RectangleShape(2 * SlotSize + 8, 6 * SlotSize + 8, Track, 12), core - 4, inset - 4);
        for (int y = 0; y < Puzzle.Size; y++)
        for (int x = 0; x < Puzzle.Size; x++)
        {
            if (x is not (2 or 3) && y is not (2 or 3))
                continue;
            var socket = RectangleShape(SlotSize - 8, SlotSize - 8, Slot, 8, SlotOutline);
            Place(canvas, socket, inset + x * SlotSize + 4, inset + y * SlotSize + 4);
        }
    }

    private static void DrawSleeve(Canvas canvas, Axis axis, int offset, double inset)
    {
        bool horizontal = axis == Axis.Horizontal;
        double start = inset + (1 + offset) * SlotSize;
        double middle = inset + 2 * SlotSize;
        double length = 4 * SlotSize;
        double width = 2 * SlotSize;
        var frame = RectangleShape(horizontal ? length : width, horizontal ? width : length,
            Brushes.Transparent, 11, RailOutline);
        frame.StrokeThickness = 1.4;
        Place(canvas, frame, horizontal ? start : middle, horizontal ? middle : start);
    }

    private static void DrawTiles(Canvas canvas, Position value, double inset)
    {
        var cells = Puzzle.Cells(value);
        for (int index = 0; index < Puzzle.TileCount; index++)
            AddTile(canvas, cells[index], value.ColorAt(index), inset, cells[index]);
    }

    private static void AddTile(Canvas canvas, Cell cell, int color, double inset, Cell origin)
    {
        var tile = new Grid
        {
            Width = SlotSize - 8,
            Height = SlotSize - 8,
            IsHitTestVisible = false
        };
        tile.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(9),
            Background = TileFaces[color],
            BorderBrush = TileEdge,
            BorderThickness = new Thickness(1, 1, 1, 2)
        });
        tile.Children.Add(new Path
        {
            Data = TileSymbols[color],
            Fill = TileInk[color],
            Width = 16,
            Height = 16,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });
        Place(canvas, tile, inset + cell.X * SlotSize + 4, inset + cell.Y * SlotSize + 4);

        if (origin == cell || !SystemParameters.ClientAreaAnimation)
            return;
        var transform = new TranslateTransform();
        tile.RenderTransform = transform;
        Animate(transform, TranslateTransform.XProperty, (origin.X - cell.X) * SlotSize);
        Animate(transform, TranslateTransform.YProperty, (origin.Y - cell.Y) * SlotSize);
    }

    private static void Animate(TranslateTransform transform, DependencyProperty property, double from)
    {
        if (from == 0)
            return;
        transform.BeginAnimation(property, new DoubleAnimation(from, 0, TimeSpan.FromMilliseconds(170))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        });
    }

    private static Rectangle RectangleShape(double width, double height, Brush fill, double radius, Brush? stroke = null) =>
        new() { Width = width, Height = height, Fill = fill, RadiusX = radius, RadiusY = radius, Stroke = stroke };

    private static void Place(Canvas canvas, UIElement element, double left, double top)
    {
        Canvas.SetLeft(element, left);
        Canvas.SetTop(element, top);
        canvas.Children.Add(element);
    }

    private static SolidColorBrush BrushFrom(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }

    private static LinearGradientBrush TileGradient(string top, string bottom)
    {
        var brush = new LinearGradientBrush(
            (Color)ColorConverter.ConvertFromString(top), (Color)ColorConverter.ConvertFromString(bottom), 90);
        brush.Freeze();
        return brush;
    }

    private readonly record struct Turn(Position Before, Move Move);
    private readonly record struct Direction(Axis Axis, int Sign, string Arrow, string Name);
}
