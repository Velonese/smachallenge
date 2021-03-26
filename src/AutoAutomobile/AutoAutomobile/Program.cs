using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

[assembly: InternalsVisibleTo("AutoAutomobile.UnitTests")]
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]

namespace AutoAutomobile
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            // Adding some dependency injection services as a baseline to ease testing
            IServiceCollection services = new ServiceCollection();

            services.AddLogging(configure =>
            {
                configure.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
                configure.AddConsole(options =>
               {
                   options.IncludeScopes = true;
                   options.TimestampFormat = "hh:mm:ss ";
               });
            });
            // Adding the HttpClient provider so we don't need to manage clients
            services.AddHttpClient<SelfDrivingCar.ISelfDrivingCarService, SelfDrivingCar.Client.SelfDrivingCarRestClient>();
            services.AddTransient<IAutoStateProcessor, AutoStateProcessor>();
            services.AddTransient<AutoDriver>();

            // Building the provider
            var provider = services.BuildServiceProvider();

            // *Very* rudimentary argument parsing:
            // Always show "help"
            string courseString = args.FirstOrDefault() ?? "1";
            string userEmail = args.Skip(1).FirstOrDefault() ?? "test@test.com";
            string latencyString = args.Skip(2).FirstOrDefault() ?? "50";
            if (!int.TryParse(courseString, out int courseRequested) || courseRequested < 1 || courseRequested > 3)
            {
                ShowHelp();
                return;
            }
            if (!int.TryParse(latencyString, out int latencyCompensationMs) || courseRequested < 1 || courseRequested > 3)
            {
                ShowHelp();
                return;
            }

            var autoDriverInstance = provider.GetRequiredService<AutoDriver>();
            autoDriverInstance.StartDrivingAsync(courseRequested, userEmail, latencyCompensationMs).GetAwaiter().GetResult();

            ShowHelp();
        }

        private static void ShowHelp()
        {
            // Delay a bit in case logging is still happening. Makes the console output messy.
            Task.Delay(100).Wait();

            Console.WriteLine();
            Console.WriteLine("Usage: AutoAutomobile.exe <courseNumber> <userEmail> <latencyCompensationMs>");
            Console.WriteLine("\t<courseNumber>: Represents which course to execute via API. 1 2 or 3.");
            Console.WriteLine("\t<userEmail>: An email for the run. Defaults to 'test@test.com'");
            Console.WriteLine("\t<latencyCompensationMs>: A number of milliseconds to help compensate for latency in the API. Defaults to 50.");
        }
    }
}