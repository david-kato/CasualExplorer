using CasualExplorer.Models;

namespace CasualExplorer.Services;

// Display coordinates only: no filesystem, persistence or external dependencies.
public sealed class GravityLayout
{
    public const double CardWidth = 176;
    public const double CardHeight = 96;
    public const double SettleBudgetSeconds = 0.55;
    public const double InitialSettleBudgetSeconds = 0.8;
    public const double PresentationDurationSeconds = InitialSettleBudgetSeconds;
    public IReadOnlyList<GravityNode> Nodes { get; }
    public double Width { get; private set; }
    public double Height { get; private set; }
    private int _quietSteps;
    private double _motionSeconds;
    private double _settleBudget = SettleBudgetSeconds;
    private bool _presentation;

    public GravityLayout(IEnumerable<FileItem> files)
    {
        Nodes = files.Take(ReadOnlyFileCatalog.DisplayLimit).Select((f, i) => new GravityNode(f, i)).ToArray();
        Reset(1000, 600);
    }

    public void Reset(double width, double height)
    {
        Width = Math.Max(1000, double.IsFinite(width) ? width : 1000);
        Height = Math.Max(600, double.IsFinite(height) ? height : 600);
        int columns = Math.Max(1, (int)((Width - 32) / (CardWidth + 16)));
        int rows = (int)Math.Ceiling(Nodes.Count / (double)columns);
        Height = Math.Max(Height, rows * (CardHeight + 16) + 32);
        var grouped = Nodes.OrderBy(n => n.File.Kind, StringComparer.CurrentCultureIgnoreCase).ToArray();
        for (int i = 0; i < grouped.Length; i++)
        {
            grouped[i].TargetX = 16 + i % columns * (CardWidth + 16);
            grouped[i].TargetY = 16 + i / columns * (CardHeight + 16);
        }
        for (int i = 0; i < Nodes.Count; i++)
        {
            var node = Nodes[i];
            // Start in newest-first positions; springs reveal the kind grouping.
            node.X = 16 + i % columns * (CardWidth + 16);
            node.Y = 16 + i / columns * (CardHeight + 16);
            node.Vx = node.Vy = 0;
            node.IsPinned = false;
            node.StartX = node.X; node.StartY = node.Y;
        }
        _quietSteps = 0;
        _motionSeconds = 0;
        _settleBudget = SettleBudgetSeconds;
        _presentation = true;
    }

    public void PrepareInitialLayout(double width, double height)
    {
        Reset(width, height);
    }

    public void Drag(int index, double x, double y)
    {
        var node = Nodes[index];
        if (!double.IsFinite(x) || !double.IsFinite(y)) return;
        _presentation = false;
        node.X = Math.Clamp(x, 0, Width - CardWidth);
        node.Y = Math.Clamp(y, 0, Height - CardHeight);
        node.Vx = node.Vy = 0;
        _quietSteps = 0;
        _motionSeconds = 0;
        _settleBudget = SettleBudgetSeconds;
    }

    public bool Step(double elapsedSeconds)
    {
        double dt = double.IsFinite(elapsedSeconds) ? Math.Clamp(elapsedSeconds, 0, 0.05) : 0;
        // Two bounded solver steps per display update, without raising the
        // 30fps rendering limit. Stop prolonged movement so text is readable.
        bool pinned = Nodes.Any(n => n.IsPinned);
        if (_presentation && !pinned)
        {
            _motionSeconds += dt;
            double t = Math.Clamp(_motionSeconds / PresentationDurationSeconds, 0, 1);
            // Quintic easing has zero velocity AND acceleration at both ends.
            // Initial preview and Reset share exactly this trajectory; no final
            // jump from a partially completed spring motion to the target slots.
            double eased = t * t * t * (t * (t * 6 - 15) + 10);
            foreach (var node in Nodes)
            {
                node.X = node.StartX + (node.TargetX - node.StartX) * eased;
                node.Y = node.StartY + (node.TargetY - node.StartY) * eased;
                node.Vx = node.Vy = 0;
            }
            if (t < 1) return true;
            RestoreSlots(); _presentation = false;
            return false;
        }
        _motionSeconds = pinned ? 0 : _motionSeconds + dt;
        bool moving = StepOnce(dt);
        moving = StepOnce(dt);
        if (!pinned && _motionSeconds >= _settleBudget)
        {
            RestoreSlots();
            return false;
        }
        return moving;
    }

    private bool StepOnce(double dt)
    {
        double movement = 0;
        foreach (var node in Nodes)
        {
            if (node.IsPinned) continue;
            double ax = (node.TargetX - node.X) * 13;
            double ay = (node.TargetY - node.Y) * 13;
            foreach (var other in Nodes)
            {
                if (ReferenceEquals(node, other)) continue;
                double dx = node.X - other.X, dy = node.Y - other.Y;
                if (Math.Abs(dx) < CardWidth + 4 && Math.Abs(dy) < CardHeight + 4)
                {
                    // Rectangular repulsion on the shallow overlap axis.
                    double ox = CardWidth + 4 - Math.Abs(dx), oy = CardHeight + 4 - Math.Abs(dy);
                    if (ox < oy) ax += (dx == 0 ? (node.Index < other.Index ? -1 : 1) : Math.Sign(dx)) * ox * 8;
                    else ay += (dy == 0 ? (node.Index < other.Index ? -1 : 1) : Math.Sign(dy)) * oy * 8;
                }
            }
            double damping = Math.Exp(-7 * dt);
            node.Vx = Math.Clamp((node.Vx + ax * dt) * damping, -360, 360);
            node.Vy = Math.Clamp((node.Vy + ay * dt) * damping, -360, 360);
            double nextX = Math.Clamp(node.X + node.Vx * dt, 0, Width - CardWidth);
            double nextY = Math.Clamp(node.Y + node.Vy * dt, 0, Height - CardHeight);
            movement = Math.Max(movement, Math.Abs(nextX - node.X) + Math.Abs(nextY - node.Y));
            node.X = nextX; node.Y = nextY;
        }
        _quietSteps = movement < 0.06 && !Nodes.Any(n => n.IsPinned) ? _quietSteps + 1 : 0;
        // A low velocity is not proof of a collision-free layout: attraction and
        // repulsion can balance while cards overlap after a drag. Before saying
        // stable, restore the already bounded, disjoint kind-group slots.
        if (_quietSteps >= 15 && HasOverlap())
        {
            RestoreSlots();
        }
        return _quietSteps < 15;
    }

    private void RestoreSlots()
    {
        foreach (var node in Nodes)
        {
            node.X = node.TargetX; node.Y = node.TargetY;
            node.Vx = node.Vy = 0;
        }
        _quietSteps = 15;
    }

    private bool HasOverlap()
    {
        for (int i = 0; i < Nodes.Count; i++)
            for (int j = i + 1; j < Nodes.Count; j++)
                if (Math.Abs(Nodes[i].X - Nodes[j].X) < CardWidth + 4 &&
                    Math.Abs(Nodes[i].Y - Nodes[j].Y) < CardHeight + 4) return true;
        return false;
    }
}

public sealed class GravityNode
{
    public GravityNode(FileItem file, int index) { File = file; Index = index; }
    public int Index { get; }
    public FileItem File { get; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Vx { get; set; }
    public double Vy { get; set; }
    public double TargetX { get; set; }
    public double TargetY { get; set; }
    public double StartX { get; set; }
    public double StartY { get; set; }
    public bool IsPinned { get; set; }
}
