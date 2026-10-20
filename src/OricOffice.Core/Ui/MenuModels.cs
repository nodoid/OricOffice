using System.Windows.Input;

namespace OricOffice.Core.Ui;

/// <summary>Icons drawn by the shared toolbar control (vector line art, no fonts).</summary>
public enum SuiteIcon
{
    None = 0,
    New,
    Open,
    Save,
    SaveAs,
    Cut,
    Copy,
    Paste,
    Sum,
    Clear,
    Trash,
    AddRow,
    AddColumn,
    AddField,
    SortAsc,
    SortDesc,
    Filter,
    Check,
    About,
    ZoomIn,
    ZoomOut,
    Field,
    Record,
}

/// <summary>A top-level menu ("File", "Edit", ...) with its drop-down items.</summary>
public sealed class MenuGroup
{
    public MenuGroup(string header, params MenuItemModel[] items)
    {
        Header = header;
        Items = new List<MenuItemModel>(items);
    }

    public string Header { get; }

    public List<MenuItemModel> Items { get; }
}

/// <summary>One drop-down item, a separator, or a sub-menu (with children).</summary>
public sealed class MenuItemModel
{
    public MenuItemModel(
        string? header = null,
        ICommand? command = null,
        Action? action = null,
        bool isSeparator = false,
        IReadOnlyList<MenuItemModel>? children = null)
    {
        Header = header;
        Command = command;
        Action = action;
        IsSeparator = isSeparator;
        Children = children;
    }

    public static MenuItemModel Separator() => new(isSeparator: true);

    public string? Header { get; }
    public ICommand? Command { get; }
    public Action? Action { get; }
    public bool IsSeparator { get; }
    public IReadOnlyList<MenuItemModel>? Children { get; }
    public bool IsEnabled { get; init; } = true;

    public bool HasChildren => Children is { Count: > 0 };
}

/// <summary>One toolbar button (icon + label + command).</summary>
public sealed class ToolButtonModel
{
    public ToolButtonModel(string text, SuiteIcon icon, ICommand? command)
    {
        Text = text;
        Icon = icon;
        Command = command;
    }

    public static ToolButtonModel Separator() => new(string.Empty, SuiteIcon.None, null) { IsSeparator = true };

    public string Text { get; }
    public SuiteIcon Icon { get; }
    public ICommand? Command { get; }
    public bool IsSeparator { get; init; }
}
