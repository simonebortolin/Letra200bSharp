using CommandLine;

namespace letra200bsharp.Console
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            var parserResult = Parser.Default.ParseArguments<Options>(args);

            await parserResult.MapResult(async o =>
            {
                try
                {
                    var imageBytes = await File.ReadAllBytesAsync(o.Image);
                    var job = LetraHelper.CreateJob(imageBytes);
                    var result = await LetraPrinter.PrintAsync(o.Address, job);
                    System.Console.WriteLine(result.Printed ? $"Printed: {result.Message}" : $"Error: {result.Message}");
                }
                catch (Exception ex)
                {
                    System.Console.WriteLine($"Error: {ex.Message}");
                }
            }, errs =>
            {
                foreach (var err in errs)
                {
                    System.Console.WriteLine(err);
                }
                return Task.CompletedTask;
            });
        }
    }
}
