using RouteJumper.Common;
using RouteJumper.Models;

namespace RouteJumper.ViewModels
{
    /// <summary>
    /// One named, recorded macro (SPEC §6.3) - Name and ScriptText are freely editable in the
    /// UI; ControlsViewModel persists the whole macro list whenever either changes.
    /// </summary>
    public class RecordedMacroViewModel : ObservableObject
    {
        private string _name;
        private string _scriptText;

        public RecordedMacroViewModel(RecordedMacro macro)
        {
            _name = macro.Name;
            _scriptText = macro.ScriptText;
            SourceProcessId = macro.SourceProcessId;
            SourceCommanderName = macro.SourceCommanderName;
            RecordedAtUtc = macro.RecordedAtUtc;
        }

        /// <summary>The instance this was originally recorded against - display-only (e.g. "Recorded against Cmdr X"). Playback is not restricted to this instance - any running instance can be selected as the play target.</summary>
        public int SourceProcessId { get; }

        /// <summary>Display-only; not used to re-match a source instance.</summary>
        public string SourceCommanderName { get; }

        public DateTime RecordedAtUtc { get; }

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public string ScriptText
        {
            get => _scriptText;
            set => SetProperty(ref _scriptText, value);
        }

        public RecordedMacro ToModel() => new()
        {
            Name = Name,
            ScriptText = ScriptText,
            SourceProcessId = SourceProcessId,
            SourceCommanderName = SourceCommanderName,
            RecordedAtUtc = RecordedAtUtc
        };
    }
}
