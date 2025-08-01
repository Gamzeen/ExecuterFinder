using ExecuterFinder.Analyzers;
using ExecuterFinder.Models;
using ExecuterFinder.Resolvers;

class Program
{
    static void Main(string[] args)
    {
        var analyzer = new BOAExecuterAnalyzer();
        var integrationCode = File.ReadAllText("/Users/gamzenurdemir/Documents/boa-codes-for-executer-extraction/integration/LoansDealer-Integration-Loans-Dealer-DealerTransaction.cs");

        var calls = analyzer.AnalyzeExecuterCalls(integrationCode);

        var resolver = new OrchestrationMethodResolver();
        string orchestrationRoot = "/Users/gamzenurdemir/Documents/boa-codes-for-executer-extraction/orchestration";

        foreach (var call in calls)
        {
            var matches = resolver.FindMatchingMethods(orchestrationRoot, call);

            Console.WriteLine($"\n🔍 BOAExecuter Call:");
            Console.WriteLine($"  RequestType : {call.RequestType}");
            Console.WriteLine($"  MethodName  : {call.MethodName}");
            Console.WriteLine($"  ResponseType: {call.ResponseType}");

            foreach (var match in matches)
            {
                Console.WriteLine($"  ✅ Found: {match.ClassName}.{match.MethodName} in {match.FilePath}");
            }
        }
    }
}
