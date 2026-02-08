using Microsoft.Extensions.DependencyInjection;
using SpawnDev.BlazorJS;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace SpawnDev.Blazor.UnitTesting
{
    /// <summary>
    /// Background service that exposes agent-friendly helper methods on window.UnitTestService.
    /// Automatically started when BlazorJSRuntime.BlazorRunAsync() is called.
    /// </summary>
    public class UnitTestService : IBackgroundService
    {
        BlazorJSRuntime JS;
        IServiceProvider ServiceProvider;

        public UnitTestService(BlazorJSRuntime js, IServiceProvider serviceProvider)
        {
            JS = js;
            ServiceProvider = serviceProvider;
            if (JS.IsBrowser)
            {
                JS.Set(nameof(UnitTestService), new { });
                // Environment and diagnostics
                JS.Set($"{nameof(UnitTestService)}.GetEnvironmentInfo", new Func<object>(GetEnvironmentInfo));
                // Service introspection
                JS.Set($"{nameof(UnitTestService)}.GetRegisteredServices", new Func<string[]>(GetRegisteredServices));
                // Console logging bridge
                JS.Set($"{nameof(UnitTestService)}.Log", new Action<string>(Log));
                // Read a property from a registered service by type name and property name
                JS.Set($"{nameof(UnitTestService)}.GetServiceProperty", new Func<string, string, object?>(GetServiceProperty));
                // Check if the app is ready (all background services started)
                JS.Set($"{nameof(UnitTestService)}.IsReady", new Func<bool>(() => true));
            }
        }

        /// <summary>
        /// Returns runtime environment information.
        /// JS: window.UnitTestService.GetEnvironmentInfo()
        /// </summary>
        public object GetEnvironmentInfo()
        {
            return new
            {
                framework = RuntimeInformation.FrameworkDescription,
                osDescription = RuntimeInformation.OSDescription,
                osArchitecture = RuntimeInformation.OSArchitecture.ToString(),
                processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
                runtimeIdentifier = RuntimeInformation.RuntimeIdentifier,
                environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production",
                baseUrl = JS.IsBrowser ? JS.Get<string?>("location.href") : null,
                isSecureContext = JS.IsBrowser ? JS.Get<bool>("isSecureContext") : false,
                userAgent = JS.IsBrowser ? JS.Get<string?>("navigator.userAgent") : null,
                isCrossOriginIsolated = JS.IsBrowser ? JS.Get<bool>("crossOriginIsolated") : false,
            };
        }

        /// <summary>
        /// Returns the type names of all registered DI services.
        /// JS: window.UnitTestService.GetRegisteredServices()
        /// </summary>
        public string[] GetRegisteredServices()
        {
            var serviceCollection = ServiceProvider.GetService<IServiceCollection>();
            if (serviceCollection == null) return Array.Empty<string>();
            return serviceCollection.Select(sd => $"{sd.Lifetime}: {sd.ServiceType.Name}").Distinct().ToArray();
        }

        /// <summary>
        /// Logs a message to the browser console from JS.
        /// JS: window.UnitTestService.Log("message")
        /// </summary>
        public void Log(string message)
        {
            Console.WriteLine($"[AgentBridge] {message}");
        }

        /// <summary>
        /// Reads a property value from a registered DI service by type name and property name.
        /// JS: window.UnitTestService.GetServiceProperty("WebGPUAccelerator", "Name")
        /// </summary>
        public object? GetServiceProperty(string serviceTypeName, string propertyName)
        {
            var serviceCollection = ServiceProvider.GetService<IServiceCollection>();
            if (serviceCollection == null) return $"Error: IServiceCollection not registered";
            var serviceDescriptor = serviceCollection.FirstOrDefault(sd =>
                sd.ServiceType.Name.Equals(serviceTypeName, StringComparison.OrdinalIgnoreCase));
            if (serviceDescriptor == null) return $"Error: Service '{serviceTypeName}' not found";
            var service = ServiceProvider.GetService(serviceDescriptor.ServiceType);
            if (service == null) return $"Error: Service '{serviceTypeName}' could not be resolved";
            var prop = service.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop == null) return $"Error: Property '{propertyName}' not found on {service.GetType().Name}";
            try
            {
                var value = prop.GetValue(service);
                return value?.ToString();
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }
    }
}
