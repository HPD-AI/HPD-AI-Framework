namespace HPD.TUI.Forms;

public sealed class FormModel
{
    private readonly List<IFormField> _fields = [];

    public IReadOnlyList<IFormField> Fields => _fields;

    public int ActiveFieldIndex { get; set; }

    public bool IsDirty => _fields.Any(static formField => formField.IsDirty);

    public FormModel Add(IFormField field)
    {
        _fields.Add(field ?? throw new ArgumentNullException(nameof(field)));
        return this;
    }

    public IFormField? ActiveField => _fields.Count == 0 ? null : _fields[Math.Clamp(ActiveFieldIndex, 0, _fields.Count - 1)];
}
