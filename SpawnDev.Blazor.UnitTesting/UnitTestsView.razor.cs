using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using SpawnDev.BlazorJS;
using System.Reflection;

namespace SpawnDev.Blazor.UnitTesting
{
    public partial class UnitTestsView : IDisposable
    {
        UnitTestRunner unitTestService { get; set; } = default!;

        [Parameter]
        public IEnumerable<Type>? TestTypes { get; set; }

        [Parameter]
        public IEnumerable<Assembly>? TestAssemblies { get; set; }

        [Parameter]
        public Func<Type, object?>? TypeInstanceResolver { get; set; }

        [Inject]
        IServiceProvider ServiceProvider { get; set; } = default!;

        [Inject]
        IServiceCollection ServiceDescriptors { get; set; } = default!;

        [Inject]
        BlazorJSRuntime JS { get; set; } = default!;

        bool _beenInit = false;

        protected override void OnParametersSet()
        {
            base.OnParametersSet();
            if (_beenInit) LoadFromParams();
        }

        private void UnitTestService_OnUnitTestResolverEvent(UnitTestResolverEvent resolverEvent)
        {
            resolverEvent.TypeInstance = TypeInstanceResolver?.Invoke(resolverEvent.TestType);
            resolverEvent.TypeInstance ??= ServiceProvider.GetService(resolverEvent.TestType);
        }

        void LoadFromParams()
        {
            var types = new List<Type>();
            if (TestTypes != null) types.AddRange(TestTypes);
            if (TestAssemblies != null)
            {
                var testClassTypes = TestAssemblies.SelectMany(o => o.GetTypes()).Where(o => o.GetCustomAttribute<TestClassAttribute>() != null).ToList();
                types.AddRange(testClassTypes);
            }
            unitTestService.SetTestTypes(types);
        }

        protected override void OnInitialized()
        {
            base.OnInitialized();
            if (!_beenInit)
            {
                _beenInit = true;
                unitTestService = new UnitTestRunner(JS);
                unitTestService.OnUnitTestResolverEvent += UnitTestService_OnUnitTestResolverEvent;
                unitTestService.TestStatusChanged += UnitTestSet_TestStatusChanged;
                LoadFromParams();
            }
        }

        private void UnitTestSet_TestStatusChanged()
        {
            StateHasChanged();
        }
        protected override void OnAfterRender(bool firstRender)
        {
            if (!Rendered)
            {
                Rendered = true;
                StateHasChanged();
            }
        }
        bool Rendered = false;

        public void Dispose()
        {
            if (_beenInit)
            {
                _beenInit = false;
                unitTestService.CancelTests();
                unitTestService.TestStatusChanged -= UnitTestSet_TestStatusChanged;
            }
        }
    }
}

