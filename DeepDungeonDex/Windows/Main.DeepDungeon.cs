using System.Numerics;
using Dalamud.Utility;

namespace DeepDungeonDex.Windows;
public partial class Main
{
    public void DrawDeepDungeonData()
    {
        ImGui.TextUnformatted($"{_currentMob.Name}{(_config.Debug ? $" ({_currentNpcNameId})" : "")}");
        ImGui.TextUnformatted($"{_locale.GetLocale(AggroName(_currentMob.Aggro))}\t");
        ImGui.SameLine();
        PrintTextWithColor(_locale.GetLocale(ThreatName(_currentMob.Threat)), _currentMob.Threat.GetColor());
        ImGui.NewLine();
        ImGui.TextUnformatted(_locale.GetLocale("Vulns"));
        ImGui.SameLine();
        DrawDDWeakness(_currentMob.IsWeaknessUnknown ? UnknownDDWeakness() : _currentMob.Weakness);
        if (!string.IsNullOrWhiteSpace(_currentMob.JoinedProcessedDescription))
        {
            ImGui.NewLine();
            ImGui.TextUnformatted(NotesLabel(_locale.GetLocale("Notes")));
            var size = ImGui.GetWindowContentRegionMax();
            var desc = _currentMob.ProcessedDescription;
            var wrapChanged = WrapInputsChanged(size.X);
            if (desc.Length == 0 || (wrapChanged && Math.Abs(size.X - _currentMob.LastProcessedWidth) > float.Epsilon))
                _currentMob.ProcessDescription(size.X);
            foreach (var s in desc)
            {
                // TODO: change this to handle SeString to handle direct lookups of values
                ImGui.TextUnformatted(s);
            }
        }

        if (_config.ClickThrough)
        {
            ImGui.NewLine();
            ImGui.TextUnformatted(_locale.GetLocale("DataButtonsHidden"));
            return;
        }

        switch (_currentMob.IsGenerated)
        {
            // ReSharper disable once InvertIf
            case true when ImGui.Button(_locale.GetLocale("CreateDataIssue")):
                Util.OpenLink(IssueUrl);
                break;
            case false when ImGui.Button(_locale.GetLocale("CorrectDataIssue")):
            {
                var url = $"{IssueUrl}&text={Uri.EscapeDataString($"Current Weakness: {_currentMob.Weakness:F}")}";
                Util.OpenLink(url);
                break;
            }
        }
    }

    private Mob? _wrapMob;
    private string[][]? _wrapSource;
    private float _wrapWidth;
    private nint _wrapFont;
    private float _wrapFontSize;

    // LastProcessedWidth 從沒被寫入，原本等於每幀重新斷行。斷行結果只由這隻怪的 Description、寬度、目前字型與字級決定，
    // 四者都沒變就沿用上次的結果（逐字相同）。
    private unsafe bool WrapInputsChanged(float width)
    {
        var font = (nint)ImGui.GetFont().Handle;
        var fontSize = ImGui.GetFontSize();
        var source = _currentMob.Description;
        if (ReferenceEquals(_wrapMob, _currentMob) && ReferenceEquals(_wrapSource, source)
            && _wrapWidth == width && _wrapFont == font && _wrapFontSize == fontSize)
            return false;
        _wrapMob = _currentMob;
        _wrapSource = source;
        _wrapWidth = width;
        _wrapFont = font;
        _wrapFontSize = fontSize;
        return true;
    }

    private bool _hasAggroName;
    private Aggro _aggroNameFor;
    private string _aggroName = "";
    private bool _hasThreatName;
    private Threat _threatNameFor;
    private string _threatName = "";

    // 等同 value.ToString()，只是不每幀裝箱。
    private string AggroName(Aggro value)
    {
        if (!_hasAggroName || value != _aggroNameFor)
        {
            _aggroName = value.ToString();
            _aggroNameFor = value;
            _hasAggroName = true;
        }
        return _aggroName;
    }

    private string ThreatName(Threat value)
    {
        if (!_hasThreatName || value != _threatNameFor)
        {
            _threatName = value.ToString();
            _threatNameFor = value;
            _hasThreatName = true;
        }
        return _threatName;
    }

    private bool _hasNotesLabel;
    private string? _notesLabelFor;
    private string _notesLabel = "";

    // 與 notes + ":\n" 逐字相同；翻譯沒變就不每幀重接。
    private string NotesLabel(string notes)
    {
        if (!_hasNotesLabel || !string.Equals(notes, _notesLabelFor, StringComparison.Ordinal))
        {
            _notesLabel = notes + ":\n";
            _notesLabelFor = notes;
            _hasNotesLabel = true;
        }
        return _notesLabel;
    }

    private void DrawDDWeakness(Weakness weakness)
    {
        var size = new Vector2(24 * _config.FontSize / 16f, 32 * _config.FontSize / 16f);
        DrawWeaknessIcon(15004, size, weakness, Weakness.Stun);
        ImGui.SameLine();
        DrawWeaknessIcon(15002, size, weakness, Weakness.Heavy);
        ImGui.SameLine();
        DrawWeaknessIcon(15009, size, weakness, Weakness.Slow);
        ImGui.SameLine();
        DrawWeaknessIcon(15013, size, weakness, Weakness.Sleep);
        ImGui.SameLine();
        DrawWeaknessIcon(15003, size, weakness, Weakness.Bind);

        // ReSharper disable once InvertIf
        if (ShowUndeadIconByArea() || weakness.HasFlag(Weakness.Undead))
        {
            ImGui.SameLine();
            DrawWeaknessIcon(15461, size, weakness, Weakness.Undead);
        }
    }

    // 資料沒寫弱點的非頭目：畫法與查無資料的怪相同（每格疊問號），不可畫成全部不吃。
    // 不死那格只在本來就會畫它的地方改成未知，不多畫也不少畫。
    private Weakness UnknownDDWeakness() =>
        ShowUndeadIconByArea() ? Weakness.AllUnknown | Weakness.UndeadUnknown : Weakness.AllUnknown;

    private bool ShowUndeadIconByArea() =>
        _currentMob.Id is not (>= 7262 and <= 7610) && _clientState.TerritoryType is >= 561 and <= 565 or >= 593 and <= 607;
}
