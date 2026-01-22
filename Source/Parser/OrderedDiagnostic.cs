namespace LanguageCore.Parser;

class OrderedDiagnosticCollection : IEnumerable<OrderedDiagnostic>
{
    readonly List<OrderedDiagnostic> _diagnostics;

    public OrderedDiagnosticCollection()
    {
        _diagnostics = new();
    }

    public void Add(int importance, Diagnostic diagnostic, ImmutableArray<OrderedDiagnostic> subdiagnostic)
    {
        Add(new OrderedDiagnostic(importance, diagnostic, subdiagnostic));
    }

    public void Add(int importance, Diagnostic diagnostic)
    {
        Add(new OrderedDiagnostic(importance, diagnostic, ImmutableArray<OrderedDiagnostic>.Empty));
    }

    public void Add(OrderedDiagnostic diagnostic)
    {
        int index = _diagnostics.BinarySearch(diagnostic);
        _diagnostics.Insert(index < 0 ? ~index : index, diagnostic);
    }

    static Diagnostic Compile(OrderedDiagnostic diagnostic) => new(
        diagnostic.Diagnostic.Level,
        diagnostic.Diagnostic.Message,
        diagnostic.Diagnostic.Position,
        diagnostic.Diagnostic.File,
        false,
        diagnostic.SubDiagnostics.ToImmutableArray(Compile)
    );

    public ImmutableArray<Diagnostic> Compile()
    {
        if (_diagnostics.Count == 0) return ImmutableArray<Diagnostic>.Empty;
        int max = _diagnostics.Max(v => v.Importance);
        ImmutableArray<Diagnostic>.Builder result = ImmutableArray.CreateBuilder<Diagnostic>(_diagnostics.Count);
        for (int i = 0; i < _diagnostics.Count; i++)
        {
            if (_diagnostics[i].Importance < max) continue;
            result.Add(Compile(_diagnostics[i]));
        }
        return result.DrainToImmutable();
    }

    public IEnumerator<OrderedDiagnostic> GetEnumerator() => _diagnostics.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _diagnostics.GetEnumerator();
}

readonly struct OrderedDiagnostic : IComparable<OrderedDiagnostic>
{
    public int Importance { get; }
    public Diagnostic Diagnostic { get; }
    public ImmutableArray<OrderedDiagnostic> SubDiagnostics { get; }

    public OrderedDiagnostic(int importance, Diagnostic diagnostic)
    {
        Importance = importance;
        Diagnostic = diagnostic;
        SubDiagnostics = ImmutableArray<OrderedDiagnostic>.Empty;
    }

    public OrderedDiagnostic(int importance, Diagnostic diagnostic, ImmutableArray<OrderedDiagnostic> subdiagnostics)
    {
        Importance = importance;
        Diagnostic = diagnostic;
        SubDiagnostics = subdiagnostics;
    }

    public OrderedDiagnostic(int importance, Diagnostic diagnostic, params OrderedDiagnostic[] subdiagnostic)
        : this(importance, diagnostic, subdiagnostic.ToImmutableArray()) { }

    public int CompareTo(OrderedDiagnostic other) => Importance.CompareTo(other.Importance);
}
