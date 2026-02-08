using SpawnDev.BlazorJS;
using System.Diagnostics;
using System.Reflection;

namespace SpawnDev.Blazor.UnitTesting
{
    public class UnitTestRunner
    {
        BlazorJSRuntime JS;
        public UnitTestRunner(BlazorJSRuntime js)
        {
            JS = js;
            if(JS.IsBrowser)
            {
                // becomes accessible in javascript as window.UnitTestRunner
                JS.Set(nameof(UnitTestRunner), new { });
                // becomes accessible in javascript as window.UnitTestRunner.ResetTests()
                JS.Set($"{nameof(UnitTestRunner)}.ResetTests", ResetTests);
                // becomes accessible in javascript as window.UnitTestRunner.CancelTests()
                JS.Set($"{nameof(UnitTestRunner)}.CancelTests", CancelTests);
                // becomes accessible in javascript as await window.UnitTestRunner.RunTests()
                JS.Set($"{nameof(UnitTestRunner)}.RunTests", new Func<Task>(RunTests));
                // becomes accessible in javascript as await window.UnitTestRunner.RunTestsByClass("WebGPUTests")
                JS.Set($"{nameof(UnitTestRunner)}.RunTestsByClass", new Func<string, Task>(RunTestsByClass));
                // becomes accessible in javascript as await window.UnitTestRunner.RunTestByName("WebGPUTests", "AddKernelTest")
                JS.Set($"{nameof(UnitTestRunner)}.RunTestByName", new Func<string, string, Task>(RunTestByName));
                // becomes accessible in javascript as window.UnitTestRunner.GetState()
                JS.Set($"{nameof(UnitTestRunner)}.GetState", new Func<string>(GetState));
                // becomes accessible in javascript as window.UnitTestRunner.GetResults()
                JS.Set($"{nameof(UnitTestRunner)}.GetResults", new Func<object>(GetResults));
            }
        }



        public event Action TestStatusChanged  =  default!;
        public TestState State { get; private set; } = TestState.None;
        /// <summary>
        /// Default timeout in milliseconds for each test. Default is 30000 (30 seconds).
        /// Set to 0 to disable timeout.
        /// Can also be set per-test via TestMethodAttribute.Timeout.
        /// </summary>
        public int DefaultTimeoutMs { get; set; } = 30000;
        public List<UnitTest> Tests { get; private set; } = new List<UnitTest>();
        public IEnumerable<Type> UnitTestTypes { get; private set; }
        public void SetTestTypes(IEnumerable<Type> unitTestTypes)
        {
            if (State == TestState.Running)
            {
                throw new Exception("Unit test types cannot be set while tests are running");
            }
            UnitTestTypes = unitTestTypes.Distinct().ToList();
            Tests.Clear();
            foreach (Type unitTestType in UnitTestTypes)
            {
                var methods = unitTestType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(o => o.GetParameters().Length == 0)
                    // When a derived class hides a base method with 'new',
                    // GetMethods returns both. Group by name and keep only the
                    // most-derived version (DeclaringType closest to unitTestType).
                    .GroupBy(m => m.Name)
                    .Select(g => g.OrderByDescending(m =>
                        GetTypeDepth(m.DeclaringType!, unitTestType)).First())
                    .ToList();
                foreach (var method in methods)
                {
                    var testMethodAttr = method.GetCustomAttribute<TestMethodAttribute>();
                    if (testMethodAttr == null) continue;
                    Tests.Add(new UnitTest(unitTestType, method));
                }
            }
            State = TestState.None;
            _ = FireStateChangeEvent();
        }

        /// <summary>
        /// Returns how many levels deep <paramref name="type"/> is in the
        /// inheritance chain rooted at <paramref name="root"/>.
        /// Used to prefer most-derived method when 'new' hides a base method.
        /// </summary>
        private static int GetTypeDepth(Type type, Type root)
        {
            int depth = 0;
            var t = root;
            while (t != null)
            {
                if (t == type) return depth;
                t = t.BaseType;
                depth--;
            }
            return depth; // fallback
        }

        public delegate void UnitTestResolverEventDelegate(UnitTestResolverEvent resolverEvent);
        public event UnitTestResolverEventDelegate OnUnitTestResolverEvent;

        public void ResetTests()
        {
            Tests.ForEach(o => o.Reset());
            State = TestState.None;
        }

        CancellationTokenSource? cancellationTokenSource { get; set; } = new CancellationTokenSource();

        public void CancelTests()
        {
            cancellationTokenSource?.Cancel();
            cancellationTokenSource = null;
        }
        Dictionary<Type, object> _instances = new Dictionary<Type, object>();
        object? GetTestTypeInstance(Type testType)
        {
            if (_instances.TryGetValue(testType, out var instance)) return instance;
            object? ret = null;
            var ev = new UnitTestResolverEvent(testType);
            OnUnitTestResolverEvent?.Invoke(ev);
            ret = ev.TypeInstance != null ? ev.TypeInstance : Activator.CreateInstance(testType);
            _instances[testType] = ret;
            return ret;
        }
        public async Task RunTests(UnitTest test)
        {
            var method = test.TestMethod;
            var testInstance = GetTestTypeInstance(test.TestType);
            test.Reset();
            test.State = TestState.Running;
            await FireStateChangeEvent();
            var sw = new Stopwatch();
            sw.Start();
            // Determine timeout: per-test attribute overrides default
            var testMethodAttr = method.GetCustomAttribute<TestMethodAttribute>();
            var timeoutMs = testMethodAttr?.Timeout > 0 ? testMethodAttr.Timeout : DefaultTimeoutMs;
            try
            {
                var ret = method.Invoke(testInstance, null);
                if (ret is Task task)
                {
                    if (timeoutMs > 0)
                    {
                        // Race the test task against a timeout
                        var timeoutTask = Task.Delay(timeoutMs);
                        var completed = await Task.WhenAny(task, timeoutTask);
                        if (completed == timeoutTask)
                        {
                            throw new TimeoutException($"Test exceeded timeout of {timeoutMs}ms");
                        }
                        ret = await task.GetResult();
                    }
                    else
                    {
                        ret = await task.GetResult();
                    }
                }
                test.Result = TestResult.Success;
                if (ret is string retStr && !string.IsNullOrEmpty(retStr))
                {
                    test.ResultText = retStr;
                }
            }
            catch (UnsupportedTestException ex)
            {
                test.StackTrace = "";
                test.ResultText = ex.Message ?? "";
                test.Result = TestResult.Unsupported;
            }
            catch (TimeoutException ex)
            {
                test.StackTrace = "";
                test.Error = ex.Message;
                test.Result = TestResult.Error;
            }
            catch (Exception ex)
            {
                test.StackTrace = ex.StackTrace ?? "";
                test.Error = ex.InnerException != null ? ex.InnerException.ToString() : ex.ToString();
                test.Result = TestResult.Error;
            }
            if (string.IsNullOrEmpty(test.ResultText)) test.ResultText = test.Result.ToString();
            test.State = TestState.Done;
            test.Duration = Math.Round(sw.Elapsed.TotalMilliseconds);
            await FireStateChangeEvent();
        }
        public async Task RunTests()
        {
            if (State == TestState.Done)
            {
                ResetTests();
            }
            if (State != TestState.None) return;
            using var tokenSource = new CancellationTokenSource();
            cancellationTokenSource = tokenSource;
            var token = cancellationTokenSource.Token;
            State = TestState.Running;
            await FireStateChangeEvent();
            foreach (var test in Tests)
            {
                if (token.IsCancellationRequested) break;
                if (test.State != TestState.None) continue;
                await RunTests(test);
            }
            cancellationTokenSource = null;
            State = TestState.Done;
            await FireStateChangeEvent();
            LogResults();
        }

        /// <summary>
        /// Runs all tests matching a test class name.
        /// Accessible from JS as: await window.UnitTestRunner.RunTestsByClass("WebGPUTests")
        /// </summary>
        public async Task RunTestsByClass(string className)
        {
            if (State == TestState.Running) return;
            if (State == TestState.Done) ResetTests();
            var matchingTests = Tests.Where(t => t.TestTypeName.Equals(className, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matchingTests.Count == 0)
            {
                Console.WriteLine($"[UnitTest] No tests found for class: {className}");
                return;
            }
            using var tokenSource = new CancellationTokenSource();
            cancellationTokenSource = tokenSource;
            var token = cancellationTokenSource.Token;
            State = TestState.Running;
            await FireStateChangeEvent();
            foreach (var test in matchingTests)
            {
                if (token.IsCancellationRequested) break;
                await RunTests(test);
            }
            cancellationTokenSource = null;
            State = TestState.Done;
            await FireStateChangeEvent();
            LogResults();
        }

        /// <summary>
        /// Runs a single test by class and method name.
        /// Accessible from JS as: await window.UnitTestRunner.RunTestByName("WebGPUTests", "AddKernelTest")
        /// </summary>
        public async Task RunTestByName(string className, string methodName)
        {
            if (State == TestState.Running) return;
            var test = Tests.FirstOrDefault(t =>
                t.TestTypeName.Equals(className, StringComparison.OrdinalIgnoreCase) &&
                t.TestMethodName.Equals(methodName, StringComparison.OrdinalIgnoreCase));
            if (test == null)
            {
                Console.WriteLine($"[UnitTest] Test not found: {className}.{methodName}");
                return;
            }
            State = TestState.Running;
            await FireStateChangeEvent();
            await RunTests(test);
            State = TestState.Done;
            await FireStateChangeEvent();
            LogResults();
        }

        /// <summary>
        /// Gets the current test runner state as a string.
        /// Accessible from JS as: window.UnitTestRunner.GetState()
        /// Returns: "None", "Running", or "Done"
        /// </summary>
        public string GetState() => State.ToString();

        /// <summary>
        /// Gets structured test results.
        /// Accessible from JS as: window.UnitTestRunner.GetResults()
        /// Returns an object with summary counts and per-test details.
        /// </summary>
        public object GetResults()
        {
            var completedTests = Tests.Where(t => t.State == TestState.Done).ToList();
            return new
            {
                state = State.ToString(),
                total = Tests.Count,
                passed = completedTests.Count(t => t.Result == TestResult.Success),
                failed = completedTests.Count(t => t.Result == TestResult.Error),
                skipped = completedTests.Count(t => t.Result == TestResult.Unsupported),
                pending = Tests.Count(t => t.State == TestState.None),
                totalDuration = completedTests.Sum(t => t.Duration),
                tests = completedTests.Select(t => new
                {
                    className = t.TestTypeName,
                    method = t.TestMethodName,
                    result = t.Result.ToString(),
                    duration = t.Duration,
                    error = t.Result == TestResult.Error ? t.Error : null,
                    resultText = t.ResultText
                }).ToArray()
            };
        }

        /// <summary>
        /// Logs a summary of test results to the browser console.
        /// </summary>
        private void LogResults()
        {
            var completedTests = Tests.Where(t => t.State == TestState.Done).ToList();
            var passed = completedTests.Count(t => t.Result == TestResult.Success);
            var failed = completedTests.Count(t => t.Result == TestResult.Error);
            var skipped = completedTests.Count(t => t.Result == TestResult.Unsupported);
            var totalDuration = completedTests.Sum(t => t.Duration);
            Console.WriteLine($"[UnitTest] DONE: {passed}/{completedTests.Count} passed, {failed} failed, {skipped} skipped ({totalDuration}ms)");
            foreach (var test in completedTests.Where(t => t.Result == TestResult.Error))
            {
                Console.WriteLine($"[UnitTest] FAIL: {test.TestTypeName}.{test.TestMethodName} - {test.Error}");
            }
        }

        async Task FireStateChangeEvent()
        {
            TestStatusChanged?.Invoke();
            await Task.Delay(100);
            // give Blazor UI sufficient time to update the UI to show the current state
        }
    }
}
