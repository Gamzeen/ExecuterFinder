using ExecuterFinder.Models;
class Program
{
    static void Main(string[] args)
    {
        string rootFolder = args.Length > 0 ? args[0] :"/Users/gamzenurdemir/Documents/boa-codes-for-executer-extraction/boa-code-4-extruction"; //"/Users/gamzenurdemir/Documents/boa-codes-for-executer-extraction/boa-code-4-extruction/relation-case";
        // "/Users/gamzenurdemir/Documents/boa-codes-for-executer-extraction/boa-codes/noname";//"/Users/gamzenurdemir/Documents/boa-codes-for-executer-extraction/boa-codes/copy";
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

    }
}
