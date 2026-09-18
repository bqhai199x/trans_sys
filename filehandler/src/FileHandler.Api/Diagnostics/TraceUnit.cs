namespace FileHandler.Api.Diagnostics;

/// <summary>
/// Captures one translation unit while suppressing unrelated iterations.
/// </summary>
internal sealed class TraceUnit : TraceScope
{

    /// <summary>
    /// Optional final snapshot of unit-local validation or output.
    /// </summary>
    private readonly Func<object?>? _result;

    /// <summary>
    /// Actual tree parent, excluding hidden iteration scopes.
    /// </summary>
    private readonly TraceNode? _treeParent;

    /// <summary>
    /// Whether candidate was skipped because it contained no translation unit.
    /// </summary>
    private bool _discarded;

    /// <summary>
    /// Creates scope using current request selection.
    /// </summary>
    /// <param name="index">Zero-based index in imported texts.</param>
    /// <param name="phase">Processing phase.</param>
    /// <param name="result">Optional final unit-local snapshot.</param>
    internal TraceUnit(int index, string phase, Func<object?>? result)
        : base(DebugTrace.Current?.Session!, DebugTrace.Current, (DebugTrace.Current?.Level ?? 0) + 1)
    {
        _result = result;
        var parent = Parent;
        while (parent?.Node.Excluded == true) parent = parent.Parent;
        _treeParent = parent?.Node;
        Node = new TraceNode { Type = "item", Index = index + 1, UnitIndex = index, Target = phase, Excluded = Session is null || !Session.Selects(index) };
        if (!Node.Excluded) Session?.AddUnit(_treeParent, Node);
        if (phase != "extract") Confirm();
        if (Session is not null) DebugTrace.Current = this;
    }

    /// <summary>
    /// Marks this scope as a real translation unit.
    /// </summary>
    /// <returns>No return value.</returns>
    internal void Confirm()
    {
        if (!Node.Excluded) Session.Found(Node.UnitIndex!.Value);
    }

    /// <summary>
    /// Discards a successfully inspected candidate with no translatable content.
    /// </summary>
    /// <returns>No return value.</returns>
    internal void Discard() => _discarded = true;

    /// <summary>
    /// Captures a complete unit-local state snapshot when selected.
    /// </summary>
    /// <param name="name">State label.</param>
    /// <param name="value">Snapshot factory, skipped for unselected indices.</param>
    /// <returns>No return value.</returns>
    public override void State(string name, Func<object?> value)
    {
        if (!Node.Excluded) Session.AddState(Node, name, value);
    }

    /// <summary>
    /// Captures final state, removes empty extraction candidates, and restores parent.
    /// </summary>
    /// <returns>No return value.</returns>
    public override void Dispose()
    {
        try
        {
            if (_result is not null) State("result", _result);
            if (_discarded && Session is not null) Session.RemoveUnit(_treeParent, Node);
        }
        finally { DebugTrace.Current = Parent; }
    }
}
