using ExecuterFinder.Models;
class Program
{
    static async Task Main(string[] args)
    {
        //string rootFolder = args.Length > 0 ? args[0] : "/Users/gamzenurdemir/Documents/BOA/BOA.Loans.Dealer";
        string rootFolder = args.Length > 0 ? args[0] : "/Users/gamzenurdemir/Documents/boa-codes-for-executer-extraction/orc-integ-ralation";
        //string rootFolder = args.Length > 0 ? args[0] : "/Users/gamzenurdemir/Documents/BOA/BOA.Kernel.Loans/RetailFinance";
        //string rootFolder = args.Length > 0 ? args[0] : "/Users/gamzenurdemir/Documents/BOA";
        //string rootFolder = args.Length > 0 ? args[0] : "/Users/gamzenurdemir/Documents/BOA-Treasury";
        //string rootFolder = args.Length > 0 ? args[0] : "/Users/gamzenurdemir/Documents/BOA-BusinessModule-KernelRetailFinance";
        //string rootFolder = args.Length > 0 ? args[0] :"/Users/gamzenurdemir/Documents/BOA-Treasury/BOA.Treasury.FX";
        //string rootFolder = args.Length > 0 ? args[0] :"/Users/gamzenurdemir/Documents/Main";
        var classInfos = ProjectAnalyzer.AnalyzeProject(rootFolder);

        // Konsol çıktısı (SP dahil)
        foreach (var ci in classInfos)
        {
            Console.WriteLine($"\nANALYZING CLASS: {ci.Name} \nIn file: {ci.FilePath} \nNamespace: {ci.Namespace}");

            foreach (var m in ci.Methods)
            {
                Console.WriteLine($"|-> FOUND METHOD: \n\tMethod Name: {m.Name}  \n\tRequest Type: {m.RequestType} \n\tResponse Type: {m.ResponseType}");

                foreach (var ec in m.ExecuterCalls)
                {
                    Console.WriteLine($"\n\t|-> FOUND EXECUTER CALL:  \n\t\t BOAExecuter<{ec.RequestType}, {ec.ResponseType}>.Execute({ec.RequestVariableName})  \n\t\t Request Type: {ec.RequestType}   \n\t\t Response Type: {ec.ResponseType} \n\t\t MethodName: {ec.MethodName}");
                }

                foreach (var im in m.InvokedMethods)
                {
                    Console.WriteLine($"\n\t|-> FOUND INVOKED BOA METHOD: \n\t\t Method Name :{im.MethodName} \n\t\t Namespace :{im.Namespace} \n\t\t Class Name : {im.ClassName} ");
                }

                if (m.StoredProcedures != null && m.StoredProcedures.Count > 0)
                {
                    foreach (var sp in m.StoredProcedures.Distinct())
                    {
                        Console.WriteLine($"\n\t|-> FOUND STORED PROCEDURE: {sp}");
                    }
                }
            }
        }

        #region Couchbase
        await using (var cb = await CouchbaseService.ConnectAsync(
            connectionString: "couchbase://localhost",
            username: "admin",
            password: "password",
            bucketName: "codegraph",
            scopeName: "code",
            collectionName: "classes",
            ensureProvision: true      
        ))
        {
            await cb.UpsertClassesAsync(classInfos);
            Console.WriteLine("✅ ClassInfo verileri Couchbase'e yazıldı.");
        }
        #endregion



        
        #region Neo4j
        // --- Neo4j yazımı (istemezsen yoruma al) ---
        var neo4jService = new Neo4jService("bolt://localhost:7687", "neo4j", "password");

        // Bağlantıyı hemen test et
        await neo4jService.VerifyAsync();

        try
        {


            foreach (var ci in classInfos)
            {
                await neo4jService.CreateClassNodeAsync(ci.Name, ci.Namespace);

                foreach (var m in ci.Methods)
                {
                    await neo4jService.CreateMethodNodeAsync(m.Name, ci.Name, ci.Namespace, m.RequestType, m.ResponseType);
                    await neo4jService.CreateClassHasMethodRelationAsync(ci.Name, ci.Namespace, m.Name);
                }
            }

            foreach (var ci in classInfos)
            {
                foreach (var m in ci.Methods)
                {
                    string srcNamespace = ci.Namespace;
                    string srcClass = ci.Name;
                    string srcMethod = m.Name;

                    foreach (var im in m.InvokedMethods)
                    {
                        await neo4jService.CreateMethodCallsMethodRelationAsync(
                            srcNamespace, srcClass, srcMethod,
                            im.Namespace, im.ClassName, im.MethodName,
                            "CALLS"
                        );
                    }

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

                    if (m.StoredProcedures != null && m.StoredProcedures.Count > 0)
                    {
                        foreach (var sp in m.StoredProcedures.Distinct())
                        {
                            await neo4jService.CreateStoredProcedureNodeAsync(sp);
                            await neo4jService.CreateMethodExecutesStoredProcedureRelationAsync(srcNamespace, srcClass, srcMethod, sp);
                        }
                    }
                }
            }
        }

        catch (System.Exception)
        {

            Console.WriteLine($"Neo4j hata aldı ve program sonlandırıldı.");
            throw;
        }
        #endregion
        Console.WriteLine($"Program başarıyla tamamlandı.");

    }
}
