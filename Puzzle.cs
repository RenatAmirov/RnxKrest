namespace RnxKrest;

internal enum Axis { Horizontal, Vertical }

internal readonly record struct Cell(int X, int Y)
{
    public Cell Slide(Axis axis, int delta) => axis switch
    {
        Axis.Horizontal when Y is 2 or 3 => this with { X = X + delta },
        Axis.Vertical when X is 2 or 3 => this with { Y = Y + delta },
        _ => this
    };
}

internal readonly record struct Move(Axis Axis, int Destination);

internal readonly record struct Position(int Key)
{
    private const int ColorMask = (1 << (Puzzle.TileCount * 2)) - 1;
    public int Horizontal => (Key >> 24 & 3) - 1;
    public int Vertical => (Key >> 26 & 3) - 1;
    public int ColorAt(int index) => Key >> (index * 2) & 3;
    public int Offset(Axis axis) => axis == Axis.Horizontal ? Horizontal : Vertical;

    public Position WithOffsets(int horizontal, int vertical) =>
        new((Key & ColorMask) | ((horizontal + 1) << 24) | ((vertical + 1) << 26));
}

internal static class Puzzle
{
    public const int Size = 6;
    public const int TileCount = 12;

    // Horizontal sleeve in row order, then the vertical sleeve outside the intersection.
    public static Position Goal { get; } = CreatePosition([0, 1, 1, 0, 0, 1, 1, 0, 2, 2, 2, 2]);
    public static IReadOnlyList<Move> Moves { get; } =
    [
        new(Axis.Horizontal, -1), new(Axis.Horizontal, 0), new(Axis.Horizontal, 1),
        new(Axis.Vertical, -1), new(Axis.Vertical, 0), new(Axis.Vertical, 1)
    ];

    private static readonly Cell[][] Layouts = CreateLayouts();
    private static readonly int[][][] Destinations = CreateDestinations();
    private static readonly Lazy<SolutionTable> Solutions = new(BuildSolutionTable);

    public static IReadOnlyList<Cell> Cells(Position position) =>
        Layouts[LayoutIndex(position.Horizontal, position.Vertical)];

    public static Position Apply(Position position, Move move)
    {
        int layout = LayoutIndex(position.Horizontal, position.Vertical);
        int moveIndex = (int)move.Axis * 3 + move.Destination + 1;
        int[] destination = Destinations[layout][moveIndex];

        int colors = 0;
        for (int index = 0; index < TileCount; index++)
            colors |= position.ColorAt(index) << (destination[index] * 2);

        var rearranged = new Position(colors);
        return move.Axis == Axis.Horizontal
            ? rearranged.WithOffsets(move.Destination, position.Vertical)
            : rearranged.WithOffsets(position.Horizontal, move.Destination);
    }

    public static int CountMatches(Position position)
    {
        var cells = Cells(position);
        var target = Cells(Goal);
        int matches = 0;
        for (int index = 0; index < cells.Count; index++)
        {
            for (int goalIndex = 0; goalIndex < target.Count; goalIndex++)
            {
                if (cells[index] == target[goalIndex] && position.ColorAt(index) == Goal.ColorAt(goalIndex))
                    matches++;
            }
        }
        return matches;
    }

    public static Position NewPosition(bool introductory, Position current)
    {
        Position[] pool = introductory ? Solutions.Value.Introductory : Solutions.Value.Standard;
        int index = Random.Shared.Next(pool.Length);
        if (pool[index] == current && pool.Length > 1)
            index = (index + 1) % pool.Length;
        return pool[index];
    }

    public static Move? Hint(Position position) =>
        position == Goal ? null : Solutions.Value.Steps[position].Move;

    private static int LayoutIndex(int horizontal, int vertical) =>
        (horizontal + 1) * 3 + vertical + 1;

    private static Position CreatePosition(int[] colors)
    {
        int key = 0;
        for (int index = 0; index < colors.Length; index++)
            key |= colors[index] << (index * 2);
        return new Position(key).WithOffsets(0, 0);
    }

    private static Cell[][] CreateLayouts()
    {
        var layouts = new Cell[9][];
        for (int horizontal = -1; horizontal <= 1; horizontal++)
        for (int vertical = -1; vertical <= 1; vertical++)
        {
            var cells = new List<Cell>(TileCount);
            for (int y = 2; y <= 3; y++)
            for (int x = 1 + horizontal; x <= 4 + horizontal; x++)
                cells.Add(new Cell(x, y));

            for (int y = 1 + vertical; y <= 4 + vertical; y++)
            {
                if (y is 2 or 3)
                    continue;
                cells.Add(new Cell(2, y));
                cells.Add(new Cell(3, y));
            }
            layouts[LayoutIndex(horizontal, vertical)] = cells.ToArray();
        }
        return layouts;
    }

    private static int[][][] CreateDestinations()
    {
        var transitions = new int[9][][];
        for (int horizontal = -1; horizontal <= 1; horizontal++)
        for (int vertical = -1; vertical <= 1; vertical++)
        {
            int index = LayoutIndex(horizontal, vertical);
            var mappings = new int[Moves.Count][];
            for (int m = 0; m < Moves.Count; m++)
            {
                Move move = Moves[m];
                var target = move.Axis == Axis.Horizontal
                    ? Layouts[LayoutIndex(move.Destination, vertical)]
                    : Layouts[LayoutIndex(horizontal, move.Destination)];
                int delta = move.Destination - (move.Axis == Axis.Horizontal ? horizontal : vertical);
                mappings[m] = Layouts[index]
                    .Select(cell => Array.IndexOf(target, cell.Slide(move.Axis, delta)))
                    .ToArray();
            }
            transitions[index] = mappings;
        }
        return transitions;
    }

    private static SolutionTable BuildSolutionTable()
    {
        // Search outward from the goal. All shifts are reversible, including the two-cell ones.
        var steps = new Dictionary<Position, Step> { [Goal] = new(default, 0) };
        var queue = new Queue<Position>();
        queue.Enqueue(Goal);
        var introductory = new List<Position>();
        var standard = new List<Position>();

        while (queue.TryDequeue(out Position position))
        {
            byte distance = (byte)(steps[position].Distance + 1);
            foreach (Move move in Moves)
            {
                int originalOffset = position.Offset(move.Axis);
                if (move.Destination == originalOffset)
                    continue;
                Position next = Apply(position, move);
                var reverse = new Move(move.Axis, originalOffset);
                if (!steps.TryAdd(next, new Step(reverse, distance)))
                    continue;

                queue.Enqueue(next);
                if (next.Horizontal != 0 || next.Vertical != 0)
                    continue;
                if (distance is >= 8 and <= 10)
                    introductory.Add(next);
                if (distance is >= 14 and <= 19)
                    standard.Add(next);
            }
        }

        return new SolutionTable(steps, introductory.ToArray(), standard.ToArray());
    }

    private readonly record struct Step(Move Move, byte Distance);
    private sealed record SolutionTable(
        Dictionary<Position, Step> Steps, Position[] Introductory, Position[] Standard);
}
