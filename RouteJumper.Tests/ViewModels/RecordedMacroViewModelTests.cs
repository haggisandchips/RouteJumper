using RouteJumper.Models;
using RouteJumper.ViewModels;
using Xunit;

namespace RouteJumper.Tests.ViewModels
{
    public class RecordedMacroViewModelTests
    {
        private static RecordedMacro Model(string name = "My Macro", string script = "UP\nDOWN") => new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            ScriptText = script,
            SourceProcessId = 42,
            SourceCommanderName = "Jameson",
            RecordedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        [Fact]
        public void Constructor_CopiesAllFieldsFromModel()
        {
            var model = Model();
            var vm = new RecordedMacroViewModel(model);

            Assert.Equal(model.Id, vm.Id);
            Assert.Equal(model.Name, vm.Name);
            Assert.Equal(model.ScriptText, vm.ScriptText);
            Assert.Equal(model.SourceProcessId, vm.SourceProcessId);
            Assert.Equal(model.SourceCommanderName, vm.SourceCommanderName);
            Assert.Equal(model.RecordedAtUtc, vm.RecordedAtUtc);
        }

        [Fact]
        public void Name_LeadingSpaceOnConstruction_IsTrimmed()
        {
            var vm = new RecordedMacroViewModel(Model(name: "  Leading"));
            Assert.Equal("Leading", vm.Name);
        }

        [Fact]
        public void Name_Setter_TrimsOnlyLeadingWhitespace()
        {
            var vm = new RecordedMacroViewModel(Model());
            vm.Name = "  has trailing space too  ";
            Assert.Equal("has trailing space too  ", vm.Name);
        }

        [Fact]
        public void ToString_ReturnsName()
        {
            var vm = new RecordedMacroViewModel(Model(name: "Refuel"));
            Assert.Equal("Refuel", vm.ToString());
        }

        [Fact]
        public void ToModel_RoundTripsAllFields()
        {
            var model = Model();
            var vm = new RecordedMacroViewModel(model);

            var roundTripped = vm.ToModel();

            Assert.Equal(model.Id, roundTripped.Id);
            Assert.Equal(model.Name, roundTripped.Name);
            Assert.Equal(model.ScriptText, roundTripped.ScriptText);
            Assert.Equal(model.SourceProcessId, roundTripped.SourceProcessId);
            Assert.Equal(model.SourceCommanderName, roundTripped.SourceCommanderName);
            Assert.Equal(model.RecordedAtUtc, roundTripped.RecordedAtUtc);
        }

        [Fact]
        public void ScriptText_Setter_RaisesPropertyChanged()
        {
            var vm = new RecordedMacroViewModel(Model());
            var raised = false;
            vm.PropertyChanged += (_, e) => raised |= e.PropertyName == nameof(RecordedMacroViewModel.ScriptText);

            vm.ScriptText = "UP\nUP";

            Assert.True(raised);
        }
    }
}
