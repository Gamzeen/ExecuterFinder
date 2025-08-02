using ExecuterFinder.Models;
class Program
{
    static void Main(string[] args)
    {
        string rootFolder = args.Length > 0 ? args[0] : "/Users/gamzenurdemir/Documents/boa-codes-for-executer-extraction";
        var classInfos = ProjectAnalyzer.AnalyzeProject(rootFolder);
        
        //TODO: Console
        /*
        foreach (var ci in classInfos)
        {
            Console.WriteLine($"Class: {ci.Name} ({ci.FilePath})");
            foreach (var m in ci.Methods)
            {
                Console.WriteLine($"Method: {m.Name}({m.RequestType}) => {m.ResponseType}");
                foreach (var ec in m.ExecuterCalls)
                {
                    Console.WriteLine($"Executer Call: BOAExecuter<{ec.RequestType}, {ec.ResponseType}>.Execute({ec.RequestVariableName}) [MethodName: {ec.MethodName}]");
                }
            }
        }

        */
    }
}
