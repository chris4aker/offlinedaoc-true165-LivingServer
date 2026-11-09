using System.Data.SQLite;
using DOL.GS;

namespace OfflineDaoc.Launcher;

/// <summary>
/// The realm card's goal: a plain number box ("2,000"). Click or tab into it, type a number, press Enter and it saves
/// at once. Escape (or leaving the box) puts the saved number back; bad input reverts with a short reason.
/// </summary>
internal sealed class RealmGoalEditor : Panel
{
    public enum GoalMark { None, Applied, Waiting, Problem }

    private readonly TextBox _box;
    private readonly ToolTip _reasonTip = new() { ToolTipIcon = ToolTipIcon.Warning };
    private readonly System.Windows.Forms.Timer _flash = new() { Interval = 900 };
    private readonly Color _normalBack = DaocTheme.Tint(59, 53, 43);
    private readonly string _realmName;
    private ToolTip? _hoverTip;
    private int _goal;
    private GoalMark _mark;
    private bool _submitting;
    public eRealm Realm { get; }

    /// <summary>Saves a typed goal. Returns null when saved, "" when the user cancelled, or a short reason.</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Func<RealmGoalEditor, int, Task<string?>>? Submit { get; set; }

    public RealmGoalEditor(eRealm realm, Color accent)
    {
        Realm = realm;
        _realmName = PopulationGoalSettings.RealmName(realm);
        Height = 24;
        BackColor = Color.Transparent;
        _box = new TextBox
        {
            Dock = DockStyle.Fill,
            TextAlign = HorizontalAlignment.Center,
            Font = new Font("Georgia", 11f, FontStyle.Bold),
            BackColor = _normalBack,
            ForeColor = DaocTheme.GoldLight,
            BorderStyle = BorderStyle.FixedSingle,
            MaxLength = 7,
            Text = "—",
            AccessibleName = $"Bots online target for {_realmName}",
            // out of the tab order: when a focused button is disabled (START SERVER after a click) Windows moves focus
            // to the next tab stop, which was this box - it then sat highlighted (owner 2026-11-09). Clicking still works.
            TabStop = false,
        };
        Controls.Add(_box);
        _box.Enter += (_, _) => BeginInvoke(_box.SelectAll);
        _box.MouseUp += (_, _) => { if (_box.SelectionLength == 0 && _box.Focused && _box.Text == _goal.ToString("N0")) _box.SelectAll(); };
        _box.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await CommitAsync(); }
            else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; Revert(); }
        };
        _box.Leave += (_, _) => { if (!_submitting) Revert(); };
        _flash.Tick += (_, _) => { _flash.Stop(); _box.BackColor = _normalBack; };
    }

    /// <summary>The hover tooltip; status is "Applied ✓ 12:34:56", "Saved, waiting for the server…" or empty.</summary>
    public void SetToolTip(ToolTip tip, string status)
    {
        _hoverTip = tip;
        string text = $"Bots online target for {_realmName} - type a number and press Enter" +
                      (status.Length > 0 ? Environment.NewLine + status : "");
        if (!string.Equals(tip.GetToolTip(_box), text, StringComparison.Ordinal))
            tip.SetToolTip(_box, text);
    }

    /// <summary>
    /// Shows the saved number in full, always in the standard gold text. The state is in the tooltip; the box
    /// flashes briefly when a new number locks in (saved, and again when the server applies it).
    /// </summary>
    public void ShowGoal(int goal, GoalMark mark)
    {
        bool lockedIn = mark == GoalMark.Applied && (_mark == GoalMark.Waiting || goal != _goal);
        _goal = goal;
        _mark = mark;
        if (!_box.Focused && !_submitting && _box.Text != goal.ToString("N0"))
            _box.Text = goal.ToString("N0");
        if (lockedIn) Flash();
    }

    private void Flash()
    {
        _box.BackColor = DaocTheme.StoneLight;
        _flash.Stop();
        _flash.Start();
    }

    private void Revert()
    {
        _box.Text = _goal.ToString("N0");
        if (_box.Focused) _box.SelectAll();
    }

    private void ShowReason(string reason)
    {
        _reasonTip.ToolTipTitle = _realmName;
        _reasonTip.Show(reason, _box, 0, _box.Height + 2, 3500);
    }

    private async Task CommitAsync()
    {
        string digits = new(_box.Text.Where(c => !char.IsWhiteSpace(c) && c is not (',' or '.' or '_' or '\'')).ToArray());
        if (!int.TryParse(digits, out int value) || value < 0)
        {
            Revert();
            ShowReason($"Type a whole number from 0 to {PopulationGoalSettings.MaxPerRealm:N0}.");
            return;
        }
        if (value == _goal)
        {
            Revert();
            return;
        }
        if (Submit == null || _submitting) return;
        _submitting = true;
        string? reason;
        try { reason = await Submit(this, value); }
        finally { _submitting = false; }
        if (reason == null)
        {
            _goal = value;
            _box.Text = value.ToString("N0");
            Flash();
            if (_box.Focused) _box.SelectAll();
            return;
        }
        Revert();
        if (reason.Length > 0) ShowReason(reason);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _flash.Dispose();
            _reasonTip.Dispose();
        }
        base.Dispose(disposing);
    }
}

/// <summary>The World Population tab: goals, live and archived counts per level bracket, and the advanced settings.</summary>
internal sealed class WorldPopulationPanel : UserControl
{
    internal static readonly (int Min, int Max, string Name)[] Brackets =
        [(1, 9, "1–9"), (10, 19, "10–19"), (20, 29, "20–29"), (30, 39, "30–39"), (40, 49, "40–49"), (50, 50, "50")];
    private const int FirstBracketColumn = 3;

    private readonly Label _apply = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Georgia", 9.5f, FontStyle.Bold), Padding = new Padding(10, 0, 0, 0) };
    private readonly Label _summary = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Georgia", 8.5f), ForeColor = DaocTheme.Parchment, Padding = new Padding(10, 0, 0, 0) };
    private readonly DataGridView _grid = new();
    private readonly RuneButton _advancedToggle = new() { Text = "▸  ADVANCED SETTINGS", Accent = DaocTheme.Tint(126, 104, 67), ShowOrnaments = false };
    private readonly Panel _advanced = new() { Height = 172, Visible = false, BackColor = DaocTheme.PanelAlt, Padding = new Padding(12, 8, 12, 8) };
    private readonly NumericUpDown _level50Cap = new() { Minimum = 0, Maximum = 100, Width = 70 };
    private readonly NumericUpDown _floor = new() { Minimum = 0, Maximum = 20, Width = 70 };
    private readonly ComboBox _rotation = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    private readonly CheckBox _hornArchived = new() { Text = "The Horn of Rallying may also call level 50s who are logged out", AutoSize = true, ForeColor = DaocTheme.Parchment, Font = new Font("Georgia", 9f, FontStyle.Bold) };
    private readonly RuneButton _saveAdvanced = new() { Text = "SAVE", Accent = DaocTheme.Tint(110, 128, 70), ShowOrnaments = false, Width = 90, Height = 26 };
    private readonly RuneButton _undoAdvanced = new() { Text = "UNDO", Accent = DaocTheme.Tint(126, 104, 67), ShowOrnaments = false, Width = 90, Height = 26 };
    private readonly Label _advancedNote = new() { AutoSize = true, ForeColor = DaocTheme.Muted, Font = new Font("Georgia", 8.5f, FontStyle.Italic), Margin = new Padding(10, 7, 0, 0) };
    private PopulationGoalSettings _saved = PopulationGoalSettings.Default;
    private bool _loading;
    private bool _advancedLoaded;   // the first load always fills the inputs
    private readonly Dictionary<(int Row, int Column), string> _cellTips = [];

    /// <summary>Raised with the edited advanced settings (goals untouched); MainForm saves them.</summary>
    public event Action<int, int, string, bool>? AdvancedSaveRequested;

    public WorldPopulationPanel()
    {
        Dock = DockStyle.Fill;
        BackColor = DaocTheme.Panel;
        ForeColor = DaocTheme.Text;
        // One scrolling column, so every part stays reachable at the smallest window size.
        var scroller = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = DaocTheme.Panel, Padding = new Padding(7) };
        var stack = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, BackColor = DaocTheme.Panel,
        };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Row(Control control, int height)
        {
            control.Dock = DockStyle.Fill;
            control.Height = height;
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            stack.Controls.Add(control, 0, stack.RowStyles.Count - 1);
        }
        Row(new Label
        {
            Text = "⚑  WORLD POPULATION  •  how many bots are online, per realm and level. What they do is set in Bot Goals Setting.",
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Georgia", 9.5f, FontStyle.Bold),
            ForeColor = DaocTheme.GoldLight,
            BackColor = DaocTheme.StoneDark,
            Padding = new Padding(11, 0, 0, 0),
            BorderStyle = BorderStyle.Fixed3D,
        }, 38);
        Row(_apply, 26);
        Row(_summary, 38);
        ConfigureGrid();
        Row(_grid, 136);
        _advancedToggle.Margin = new Padding(0, 6, 0, 4);
        _advancedToggle.Click += (_, _) =>
        {
            _advanced.Visible = !_advanced.Visible;
            _advancedToggle.Text = (_advanced.Visible ? "▾" : "▸") + "  ADVANCED SETTINGS";
        };
        var toggleRow = new Panel { BackColor = DaocTheme.Panel };
        _advancedToggle.Dock = DockStyle.Left;
        _advancedToggle.Width = 230;
        toggleRow.Controls.Add(_advancedToggle);
        Row(toggleRow, 34);
        BuildAdvanced();
        Row(_advanced, 172);
        Row(new Label
        {
            ForeColor = DaocTheme.Muted,
            Font = new Font("Georgia", 8.25f, FontStyle.Italic),
            Padding = new Padding(4, 6, 4, 0),
            Text = "Type a realm's number in its card and press Enter. Level cells show live bots/that bracket's target; \"low\" means below its minimum.\n" +
                "Bots always start at level 1. Bots that log out keep their level and come back later; empty upper brackets pass their share down until bots level up.\n" +
                "A realm's number saves the moment you press Enter, even while the server runs, and stays until you type a new one.",
        }, 64);
        scroller.Controls.Add(stack);
        Controls.Add(scroller);
    }

    private void ConfigureGrid()
    {
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.RowHeadersVisible = false;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _grid.BackgroundColor = DaocTheme.StoneDark;
        _grid.BorderStyle = BorderStyle.Fixed3D;
        _grid.GridColor = DaocTheme.Tint(78, 70, 56);
        _grid.EnableHeadersVisualStyles = false;
        _grid.ShowCellToolTips = true;
        _grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = DaocTheme.Tint(61, 54, 43), ForeColor = DaocTheme.GoldLight, SelectionBackColor = DaocTheme.Tint(61, 54, 43),
            Font = new Font("Georgia", 8.25f, FontStyle.Bold), Padding = new Padding(2), Alignment = DataGridViewContentAlignment.MiddleCenter,
            WrapMode = DataGridViewTriState.True,   // long headers wrap instead of being cut off
        };
        _grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = DaocTheme.Tint(35, 31, 26), ForeColor = DaocTheme.Text, SelectionBackColor = DaocTheme.Tint(35, 31, 26),
            SelectionForeColor = DaocTheme.Text, Padding = new Padding(2), Font = new Font("Georgia", 8.5f),
            Alignment = DataGridViewContentAlignment.MiddleCenter,
            WrapMode = DataGridViewTriState.True,   // a narrow window wraps a cell instead of cutting it off
        };
        _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _grid.ColumnHeadersHeight = 34;
        _grid.ScrollBars = ScrollBars.None;
        _grid.RowTemplate.Height = 32;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        void Add(string header, float weight)
        {
            int index = _grid.Columns.Add("c" + _grid.Columns.Count, header);
            _grid.Columns[index].FillWeight = weight;
            _grid.Columns[index].SortMode = DataGridViewColumnSortMode.NotSortable;
        }
        Add("Realm", 80); Add("Target", 65); Add("Live", 65);
        foreach (var bracket in Brackets) Add("Lv " + bracket.Name, 80);
        Add("New /h", 65); Add("Returned /h", 80); Add("Retired /h", 75);
        foreach (string realm in new[] { "Albion", "Midgard", "Hibernia" })
            _grid.Rows.Add(realm);
        _grid.CellToolTipTextNeeded += (_, e) =>
        {
            if (_cellTips.TryGetValue((e.RowIndex, e.ColumnIndex), out string? tip)) e.ToolTipText = tip;
        };
        // Realm names in the same colours as the Active Population list, also when the cell is selected.
        for (int row = 0; row < 3; row++)
        {
            DataGridViewCellStyle style = _grid.Rows[row].Cells[0].Style;
            style.ForeColor = style.SelectionForeColor = row switch
            {
                0 => DaocTheme.Tint(225, 116, 105), 1 => DaocTheme.Tint(124, 161, 215), _ => DaocTheme.Tint(112, 178, 112),
            };
        }
    }

    private void BuildAdvanced()
    {
        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 5, BackColor = Color.Transparent };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 4; i++) table.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Label Caption(string text) => new() { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = DaocTheme.Parchment, Font = new Font("Georgia", 9f, FontStyle.Bold) };
        Label Help(string text) => new() { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = DaocTheme.Muted, Font = new Font("Georgia", 8.25f, FontStyle.Italic) };
        foreach (Control input in new Control[] { _level50Cap, _floor, _rotation })
        {
            input.BackColor = DaocTheme.Tint(59, 53, 43);
            input.ForeColor = DaocTheme.Text;
            input.Font = new Font("Georgia", 9f);
            input.Margin = new Padding(0, 3, 0, 0);
        }
        _rotation.Items.AddRange(PopulationGoalSettings.RotationValues.Cast<object>().ToArray());
        table.Controls.Add(Caption("Level 50s may fill up to (% of each goal)"), 0, 0);
        table.Controls.Add(_level50Cap, 1, 0);
        table.Controls.Add(Help("Default 35%. The rest goes to levels 1–49."), 2, 0);
        table.Controls.Add(Caption("Every level bracket keeps at least (%)"), 0, 1);
        table.Controls.Add(_floor, 1, 1);
        table.Controls.Add(Help("Default 4% (never fewer than 2 bots). Five floors plus the level-50 cap must fit in 100%."), 2, 1);
        table.Controls.Add(Caption("Rotation pace"), 0, 2);
        table.Controls.Add(_rotation, 1, 2);
        table.Controls.Add(Help("How often bots log out and others log in. Off keeps the same bots online."), 2, 2);
        table.Controls.Add(_hornArchived, 0, 3);
        table.SetColumnSpan(_hornArchived, 3);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, BackColor = Color.Transparent, Padding = new Padding(0, 4, 0, 0) };
        buttons.Controls.Add(_saveAdvanced);
        buttons.Controls.Add(_undoAdvanced);
        buttons.Controls.Add(_advancedNote);
        table.Controls.Add(buttons, 0, 4);
        table.SetColumnSpan(buttons, 3);
        _advanced.Controls.Add(table);
        _level50Cap.ValueChanged += (_, _) => AdvancedChanged();
        _floor.ValueChanged += (_, _) => AdvancedChanged();
        _rotation.SelectedIndexChanged += (_, _) => AdvancedChanged();
        _hornArchived.CheckedChanged += (_, _) => AdvancedChanged();
        _undoAdvanced.Click += (_, _) => LoadAdvanced(_saved, force: true);
        _saveAdvanced.Click += (_, _) => AdvancedSaveRequested?.Invoke((int)_level50Cap.Value, (int)_floor.Value,
            _rotation.SelectedItem?.ToString() ?? PopulationGoalSettings.RotationNormal, _hornArchived.Checked);
    }

    private PopulationGoalSettings EditedAdvanced() => _saved with
    {
        Level50MaxPercent = (int)_level50Cap.Value,
        BracketFloorPercent = (int)_floor.Value,
        Rotation = _rotation.SelectedItem?.ToString() ?? PopulationGoalSettings.RotationNormal,
        HornCallsArchived = _hornArchived.Checked,
    };

    public bool HasUnsavedAdvanced => EditedAdvanced() != _saved;

    private void AdvancedChanged()
    {
        if (_loading) return;
        PopulationGoalSettings edited = EditedAdvanced();
        string? problem = edited.Validate();
        bool dirty = edited != _saved;
        _saveAdvanced.Enabled = dirty && problem == null;
        _undoAdvanced.Enabled = dirty;
        _advancedNote.Text = problem ?? (dirty ? "Unsaved changes." : "Saved.");
        _advancedNote.ForeColor = problem != null || dirty ? DaocTheme.GoldLight : DaocTheme.Muted;
    }

    /// <summary>Loads the saved settings into the advanced inputs unless the user is editing them.</summary>
    public void LoadAdvanced(PopulationGoalSettings saved, bool force = false)
    {
        bool dirty = HasUnsavedAdvanced;
        _saved = saved;
        // Never overwrite an input the user is typing in.
        if (!force && _advancedLoaded && (dirty || _advanced.ContainsFocus))
        {
            AdvancedChanged();
            return;
        }
        _loading = true;
        try
        {
            _level50Cap.Value = Math.Clamp(saved.Level50MaxPercent, 0, 100);
            _floor.Value = Math.Clamp(saved.BracketFloorPercent, 0, 20);
            _rotation.SelectedIndex = saved.RotationStep;
            _hornArchived.Checked = saved.HornCallsArchived;
        }
        finally { _loading = false; _advancedLoaded = true; }
        AdvancedChanged();
    }

    public void ShowApply(string text, Color color)
    {
        _apply.Text = text;
        _apply.ForeColor = color;
    }

    /// <summary>
    /// Fills the realm rows: target, live, and live against each level bracket's share. report is null when the
    /// server gives no status (stopped or an older server). Offline counts appear only in the bracket tooltips.
    /// </summary>
    public void ShowRealms(PopulationGoalSettings goals, PopulationStatusReport? report, string summary)
    {
        _summary.Text = summary;
        _cellTips.Clear();
        eRealm[] realms = [eRealm.Albion, eRealm.Midgard, eRealm.Hibernia];
        for (int row = 0; row < 3; row++)
        {
            DataGridViewRow gridRow = _grid.Rows[row];
            RealmPopulationStatus? status = report?.For(realms[row]);
            gridRow.Cells[1].Value = goals.GoalFor(realms[row]).ToString("N0");
            if (status == null)
            {
                for (int c = 2; c < _grid.Columns.Count; c++) gridRow.Cells[c].Value = "—";
                continue;
            }
            gridRow.Cells[2].Value = status.Live.ToString("N0");
            _cellTips[(row, 2)] = $"{status.LoggingIn:N0} logging in, {status.LoggingOut:N0} logging out right now.";
            for (int b = 0; b < Brackets.Length; b++)
            {
                DataGridViewCell cell = gridRow.Cells[FirstBracketColumn + b];
                BracketStatus? bracket = status.Brackets?.FirstOrDefault(x => x != null && x.MinLevel == Brackets[b].Min);
                if (bracket == null)
                {
                    cell.Value = "—";
                    continue;
                }
                // "low" marks a bracket below its minimum; colour stays the standard text colour.
                cell.Value = $"{bracket.Live:N0}/{bracket.Target:N0}" + (bracket.Live < bracket.Floor ? " low" : "");
                _cellTips[(row, FirstBracketColumn + b)] =
                    $"Levels {Brackets[b].Name}: {bracket.Live:N0} live, target {bracket.Target:N0}, at least {bracket.Floor:N0}." +
                    $"\n{bracket.Archived:N0} offline bots of these levels can come back.";
            }
            int next = FirstBracketColumn + Brackets.Length;
            gridRow.Cells[next].Value = status.CreatedLastHour.ToString("N0");
            gridRow.Cells[next + 1].Value = status.ReturnedLastHour.ToString("N0");
            gridRow.Cells[next + 2].Value = status.RetiredLastHour.ToString("N0");
            _cellTips[(row, next)] = "New level-1 bots created in the last hour.";
            _cellTips[(row, next + 1)] = "Bots that came back after a break, in the last hour.";
            _cellTips[(row, next + 2)] = "Bots deleted forever in the last hour (the saved-bot cap or a launcher delete).";
        }
    }
}

/// <summary>Database and file work for the population UI. Runs on background threads.</summary>
internal static class PopulationLauncherData
{
    internal const string RetireCommand = "Retire";
    internal const string ArchiveFileName = "bot-archive.sqlite3.db";

    internal sealed record SavedBot(long BotId, string Name, int Realm, string RaceName, int Gender, int ClassId, string ClassName,
        int Level, string ZoneName, bool Archived);

    internal static string NotHired(SQLiteConnection connection, SQLiteTransaction? transaction, string botIdColumn)
    {
        using var command = connection.CreateCommand();
        if (transaction != null) command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name='offline_hired_companions'";
        return command.ExecuteScalar() == null ? string.Empty
            : $" AND {botIdColumn} NOT IN (SELECT BotId FROM offline_hired_companions)";
    }

    /// <summary>Saved (warm) non-hired, non-retiring bots per realm: one grouped count (index 1-3).</summary>
    internal static int[] SavedCounts(SQLiteConnection connection)
    {
        var counts = new int[4];
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Realm, COUNT(*) FROM offline_world_bots WHERE IsRetired=0" + NotHired(connection, null, "BotId") + " GROUP BY Realm";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            int realm = reader.GetInt32(0);
            if (realm is >= 1 and <= 3) counts[realm] = reader.GetInt32(1);
        }
        return counts;
    }

    /// <summary>
    /// First run without a goals file: each realm's goal is its current bot count, so nothing changes until the owner
    /// edits it. An empty world gets the defaults (500 a realm).
    /// </summary>
    internal static PopulationGoalSettings FirstRunGoals(int[] savedCounts)
    {
        PopulationGoalSettings goals = PopulationGoalSettings.Default;
        if (savedCounts.Skip(1).Sum() == 0) return goals;
        return goals with
        {
            Albion = Math.Clamp(savedCounts[1], 0, PopulationGoalSettings.MaxPerRealm),
            Midgard = Math.Clamp(savedCounts[2], 0, PopulationGoalSettings.MaxPerRealm),
            Hibernia = Math.Clamp(savedCounts[3], 0, PopulationGoalSettings.MaxPerRealm),
        };
    }

    /// <summary>
    /// The population master switch: the old launcher turned it on when it made bots. With goals the server makes
    /// the bots, so a goal above 0 turns it on (one small update, only when it is off).
    /// </summary>
    internal static void EnsurePopulationEnabled(string database)
    {
        if (!File.Exists(database)) return;
        using var connection = new SQLiteConnection($"Data Source={database};Version=3;Pooling=False;Default Timeout=3");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE ServerProperty SET Value='true' WHERE `Key`='population_enabled' AND lower(Value)<>'true'";
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// On-demand search of saved bots that are not live: warm rows in the world database, then the cold archive file
    /// if it has searchable columns. At most limit rows in total.
    /// </summary>
    internal static List<SavedBot> SearchSaved(string database, string text, int realm, int limit, out string note)
    {
        note = string.Empty;
        var results = new List<SavedBot>();
        // LIKE pattern with \ as the escape character, so % and _ in the text match literally.
        string like = "%" + text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
        if (File.Exists(database))
        {
            using var connection = new SQLiteConnection($"Data Source={database};Version=3;Read Only=True;Pooling=False;Default Timeout=5");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT BotId, Name, Realm, RaceName, Gender, ClassId, ClassName, Level, COALESCE(ZoneName,'—') FROM offline_world_bots " +
                "WHERE IsOnline=0 AND IsRetired=0" + NotHired(connection, null, "BotId") +
                (realm > 0 ? " AND Realm=@realm" : "") +
                " AND (Name LIKE @q ESCAPE '\\' OR ClassName LIKE @q ESCAPE '\\' OR ZoneName LIKE @q ESCAPE '\\' OR RaceName LIKE @q ESCAPE '\\')" +
                " ORDER BY Name LIMIT @limit";
            command.Parameters.AddWithValue("@q", like);
            command.Parameters.AddWithValue("@realm", realm);
            command.Parameters.AddWithValue("@limit", limit);
            using var reader = command.ExecuteReader();
            while (reader.Read())
                results.Add(new SavedBot(reader.GetInt64(0), reader.GetString(1), reader.GetInt32(2), reader.GetString(3), reader.GetInt32(4),
                    reader.GetInt32(5), reader.GetString(6), reader.GetInt32(7), reader.GetString(8), false));
        }
        if (results.Count >= limit) { note = $"First {limit:N0} matches shown; narrow the search."; return results; }

        string archive = Path.Combine(Path.GetDirectoryName(database) ?? ".", ArchiveFileName);
        if (!File.Exists(archive)) return results;
        try
        {
            using var connection = new SQLiteConnection($"Data Source={archive};Version=3;Read Only=True;Pooling=False;Default Timeout=5");
            connection.Open();
            string? table = null;
            HashSet<string> columns = [];
            using (var tables = connection.CreateCommand())
            {
                tables.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name";
                using var reader = tables.ExecuteReader();
                var names = new List<string>();
                while (reader.Read()) names.Add(reader.GetString(0));
                reader.Close();
                foreach (string name in names)
                {
                    var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    using var info = connection.CreateCommand();
                    info.CommandText = $"PRAGMA table_info([{name.Replace("]", "]]")}])";
                    using var infoReader = info.ExecuteReader();
                    while (infoReader.Read()) found.Add(infoReader.GetString(1));
                    if (found.Contains("BotId") && found.Contains("Name") && found.Contains("Realm") && found.Contains("Level"))
                    {
                        table = name;
                        columns = found;
                        break;
                    }
                }
            }
            if (table == null) { note = "The archive file has no searchable name list."; return results; }
            string Col(string name, string fallback) => columns.Contains(name) ? name : fallback;
            using var search = connection.CreateCommand();
            search.CommandText = $"SELECT BotId, Name, Realm, Level, {Col("ClassId", "0")}, {Col("ClassName", "''")}, {Col("RaceName", "''")}, {Col("Gender", "0")} " +
                $"FROM [{table.Replace("]", "]]")}] WHERE Name LIKE @q ESCAPE '\\'" + (realm > 0 ? " AND Realm=@realm" : "") +
                (columns.Contains("State") ? " AND State IN (0,1)" : "") + (columns.Contains("Online") ? " AND Online=0" : "") +
                " ORDER BY Name LIMIT @limit";
            search.Parameters.AddWithValue("@q", like);
            search.Parameters.AddWithValue("@realm", realm);
            search.Parameters.AddWithValue("@limit", limit - results.Count);
            using var rows = search.ExecuteReader();
            var seen = results.Select(r => r.BotId).ToHashSet();
            while (rows.Read())
            {
                long id = rows.GetInt64(0);
                if (!seen.Add(id)) continue;
                results.Add(new SavedBot(id, rows.GetString(1), rows.GetInt32(2), Convert.ToString(rows.GetValue(6)) ?? "", Convert.ToInt32(rows.GetValue(7)),
                    Convert.ToInt32(rows.GetValue(4)), Convert.ToString(rows.GetValue(5)) ?? "", rows.GetInt32(3), "Archived", true));
            }
            if (results.Count >= limit) note = $"First {limit:N0} matches shown; narrow the search.";
        }
        catch (Exception ex) when (ex is SQLiteException or IOException or InvalidCastException)
        {
            note = "Archive search failed: " + ex.Message;
        }
        return results;
    }

    /// <summary>
    /// Queues the server's retirement of these bots (a 'Retire' command each). The server logs a live bot out at a
    /// safe moment, then deletes it with its items, coins, Exchange listings and guild row. Hired companions are
    /// never touched. Returns how many commands were queued.
    /// </summary>
    internal static int QueueRetire(string database, IReadOnlyCollection<long> botIds)
    {
        using var connection = new SQLiteConnection($"Data Source={database};Version=3;Pooling=False;Default Timeout=10");
        connection.Open();
        using var transaction = connection.BeginTransaction();
        string hired = NotHired(connection, transaction, "@id");
        string now = DateTime.UtcNow.ToString("O");
        int queued = 0;
        using var mark = connection.CreateCommand();
        mark.Transaction = transaction;
        mark.CommandText = "UPDATE offline_world_bots SET IsRetired=1, Activity='Retirement requested', CurrentGoal='', LastUpdateUtc=@now WHERE BotId=@id AND IsRetired=0" + hired;
        var markId = mark.Parameters.AddWithValue("@id", 0L);
        mark.Parameters.AddWithValue("@now", now);
        using var queue = connection.CreateCommand();
        queue.Transaction = transaction;
        queue.CommandText = "INSERT INTO offline_bot_commands (BotId, CommandType, RequestedUtc, State) SELECT @id, @type, @now, 'Pending' " +
            "WHERE 1=1" + hired + " AND NOT EXISTS (SELECT 1 FROM offline_bot_commands WHERE BotId=@id AND CommandType=@type AND State IN ('Pending','Processing'))";
        var queueId = queue.Parameters.AddWithValue("@id", 0L);
        queue.Parameters.AddWithValue("@type", RetireCommand);
        queue.Parameters.AddWithValue("@now", now);
        foreach (long id in botIds)
        {
            markId.Value = id;
            mark.ExecuteNonQuery();
            queueId.Value = id;
            queued += queue.ExecuteNonQuery();
        }
        transaction.Commit();
        return queued;
    }

    /// <summary>
    /// Bots that live only in the cold archive file (no row in the world database). Read-only; empty when the
    /// archive file is missing or has no bot list.
    /// </summary>
    internal static List<long> ArchivedOnlyIds(string database)
    {
        var ids = new List<long>();
        string archive = Path.Combine(Path.GetDirectoryName(database) ?? ".", ArchiveFileName);
        if (!File.Exists(archive) || !File.Exists(database)) return ids;
        var warm = new HashSet<long>();
        using (var world = new SQLiteConnection($"Data Source={database};Version=3;Read Only=True;Pooling=False;Default Timeout=5"))
        {
            world.Open();
            using var command = world.CreateCommand();
            command.CommandText = "SELECT BotId FROM offline_world_bots";
            using var reader = command.ExecuteReader();
            while (reader.Read()) warm.Add(reader.GetInt64(0));
        }
        using var connection = new SQLiteConnection($"Data Source={archive};Version=3;Read Only=True;Pooling=False;Default Timeout=5");
        connection.Open();
        using (var check = connection.CreateCommand())
        {
            check.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='bots'";
            if (Convert.ToInt32(check.ExecuteScalar()) == 0) return ids;
        }
        using var select = connection.CreateCommand();
        select.CommandText = "SELECT BotId FROM bots WHERE State IN (0,1)";
        using var rows = select.ExecuteReader();
        while (rows.Read())
        {
            long id = rows.GetInt64(0);
            if (!warm.Contains(id)) ids.Add(id);
        }
        return ids;
    }

    /// <summary>
    /// Delete all: a 'Retire' command for every saved non-hired bot in the world database and for every bot that
    /// lives only in the archive. Returns how many commands were queued.
    /// </summary>
    internal static int QueueRetireAll(string database, IReadOnlyCollection<long> archivedOnly)
    {
        using var connection = new SQLiteConnection($"Data Source={database};Version=3;Pooling=False;Default Timeout=10");
        connection.Open();
        using var transaction = connection.BeginTransaction();
        string now = DateTime.UtcNow.ToString("O");
        string hiredB = NotHired(connection, transaction, "b.BotId"), hired = NotHired(connection, transaction, "BotId");
        int queued;
        using (var queue = connection.CreateCommand())
        {
            queue.Transaction = transaction;
            queue.CommandText = $"""
                INSERT INTO offline_bot_commands (BotId, CommandType, RequestedUtc, State)
                SELECT b.BotId, @type, @now, 'Pending'
                FROM offline_world_bots b
                WHERE 1=1{hiredB}
                  AND NOT EXISTS (
                      SELECT 1 FROM offline_bot_commands c
                      WHERE c.BotId=b.BotId AND c.CommandType=@type AND c.State IN ('Pending','Processing'))
                """;
            queue.Parameters.AddWithValue("@type", RetireCommand);
            queue.Parameters.AddWithValue("@now", now);
            queued = queue.ExecuteNonQuery();
        }
        using (var mark = connection.CreateCommand())
        {
            mark.Transaction = transaction;
            mark.CommandText = "UPDATE offline_world_bots SET IsRetired=1, Activity='Retirement requested', CurrentGoal='', LastUpdateUtc=@now WHERE IsRetired=0" + hired;
            mark.Parameters.AddWithValue("@now", now);
            mark.ExecuteNonQuery();
        }
        if (archivedOnly.Count > 0)
        {
            using var cold = connection.CreateCommand();
            cold.Transaction = transaction;
            cold.CommandText = "INSERT INTO offline_bot_commands (BotId, CommandType, RequestedUtc, State) SELECT @id, @type, @now, 'Pending' " +
                "WHERE NOT EXISTS (SELECT 1 FROM offline_bot_commands WHERE BotId=@id AND CommandType=@type AND State IN ('Pending','Processing'))";
            var id = cold.Parameters.AddWithValue("@id", 0L);
            cold.Parameters.AddWithValue("@type", RetireCommand);
            cold.Parameters.AddWithValue("@now", now);
            foreach (long botId in archivedOnly)
            {
                id.Value = botId;
                queued += cold.ExecuteNonQuery();
            }
        }
        transaction.Commit();
        return queued;
    }
}
