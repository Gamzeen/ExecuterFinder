using ExecuterFinder.Models;
class Program
{
    static async Task Main(string[] args)
    {
        //string rootFolder = args.Length > 0 ? args[0] : "/Users/gamzenurdemir/Documents/BOA/BOA.Loans.Dealer";//"/Users/gamzenurdemir/Documents/boa-codes-for-executer-extraction/orc-integ-ralation";
        string rootFolder = args.Length > 0 ? args[0] : "/Users/gamzenurdemir/Documents/boa-codes-for-executer-extraction/orc-integ-ralation";
        //string rootFolder = args.Length > 0 ? args[0] : "/Users/gamzenurdemir/Documents/BOA/BOA.Kernel.Loans/RetailFinance";
        var classInfos = ProjectAnalyzer.AnalyzeProject(rootFolder);
        
        //TODO: Console
        foreach (var ci in classInfos)
        {
            //Console.WriteLine($"Class: {ci.Name} ({ci.FilePath})");
            Console.WriteLine($"\nANALYZING CLASS: {ci.Name} \nIn file: {ci.FilePath} \nNamespace: {ci.Namespace}");

            foreach (var m in ci.Methods)
            {
                //Console.WriteLine($"Method: {m.Name}({m.RequestType}) => {m.ResponseType}");
                Console.WriteLine($"|-> FOUND METHOD: \n\tMethod Name: {m.Name}  \n\tRequest Type: {m.RequestType} \n\tResponse Type: {m.ResponseType}");

                foreach (var ec in m.ExecuterCalls)
                {
                    //Console.WriteLine($"Executer Call: BOAExecuter<{ec.RequestType}, {ec.ResponseType}>.Execute({ec.RequestVariableName}) [MethodName: {ec.MethodName}]");
                    Console.WriteLine($"\n\t|-> FOUND EXECUTER CALL:  \n\t\t BOAExecuter<{ec.RequestType}, {ec.ResponseType}>.Execute({ec.RequestVariableName})  \n\t\t Request Type: {ec.RequestType}   \n\t\t Response Type: {ec.ResponseType} \n\t\t MethodName: {ec.MethodName}");
       
                }
                foreach (var ec in m.InvokedMethods)
                {
                    //Console.WriteLine($"Invoked Method: {ec.Namespace}.{ec.ClassName}.{ec.MethodName}");
                    Console.WriteLine($"\n\t|-> FOUND INVOKED BOA METHOD: \n\t\t Method Name :{ec.MethodName} \n\t\t Namespace :{ec.Namespace} \n\t\t Class Name : {ec.ClassName} ");
                           
                }
            }
        }


        var neo4jService = new Neo4jService("bolt://localhost:7687", "neo4j", "password");
        // 1. Tüm Class ve Method nodelarını oluştur
        foreach (var ci in classInfos)
        {
            await neo4jService.CreateClassNodeAsync(ci.Name, ci.Namespace);

            foreach (var m in ci.Methods)
            {
                await neo4jService.CreateMethodNodeAsync(m.Name, ci.Name, ci.Namespace,m.RequestType, m.ResponseType);
                await neo4jService.CreateClassHasMethodRelationAsync(ci.Name, ci.Namespace, m.Name);
            }
        }

        // 2. Methodlar arası relationları kur (calls, executes)
        foreach (var ci in classInfos)
        {
            foreach (var m in ci.Methods)
            {
                string srcNamespace = ci.Namespace;
                string srcClass = ci.Name;
                string srcMethod = m.Name;

                // INVOKED METHODS
                foreach (var im in m.InvokedMethods)
                {
                    string tgtNamespace = im.Namespace;
                    string tgtClass = im.ClassName;
                    string tgtMethod = im.MethodName;
                    await neo4jService.CreateMethodCallsMethodRelationAsync(
                        srcNamespace, srcClass, srcMethod,
                        tgtNamespace, tgtClass, tgtMethod,
                        "CALLS"
                    );
                }

                // EXECUTER METHODS (ExecuterCallInfo'da target class yoksa sadece method bağlayabilirsin)
                foreach (var ex in m.ExecuterCalls)
                {
                    if (!string.IsNullOrEmpty(ex.MethodName))
                    {
                        await neo4jService.CreateExecuterRelationBySignatureAsync(
                            srcNamespace, srcClass, srcMethod,
                            ex.MethodName, ex.RequestType, ex.ResponseType
                        );
                    }
                }
            }
        }

    }
}
