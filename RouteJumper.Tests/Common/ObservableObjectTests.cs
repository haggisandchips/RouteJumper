using System.ComponentModel;
using RouteJumper.Common;
using Xunit;

namespace RouteJumper.Tests.Common
{
    public class ObservableObjectTests
    {
        private sealed class TestModel : ObservableObject
        {
            private int _value;

            public int Value
            {
                get => _value;
                set => SetProperty(ref _value, value);
            }
        }

        [Fact]
        public void SetProperty_ChangedValue_RaisesPropertyChangedWithCorrectName()
        {
            var model = new TestModel();
            var raised = new List<string?>();
            model.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            model.Value = 5;

            Assert.Equal(new List<string?> { nameof(TestModel.Value) }, raised);
        }

        [Fact]
        public void SetProperty_SameValue_DoesNotRaisePropertyChanged()
        {
            var model = new TestModel { Value = 5 };
            var raised = false;
            model.PropertyChanged += (_, _) => raised = true;

            model.Value = 5;

            Assert.False(raised);
        }

        [Fact]
        public void SetProperty_ChangedValue_UpdatesBackingField()
        {
            var model = new TestModel();
            model.Value = 5;
            Assert.Equal(5, model.Value);
        }
    }
}
