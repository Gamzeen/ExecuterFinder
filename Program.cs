using ExecuterFinder.Models;
class Program
{
    static async Task Main(string[] args)
    {
        //string rootFolder = args.Length > 0 ? args[0] : "/Users/gamzenurdemir/Documents/BOA/BOA.Loans.Dealer";
        string rootFolder = args.Length > 0 ? args[0] : "/Users/gamzenurdemir/Documents/boa-codes-for-executer-extraction/orc-integ-ralation";
        //string rootFolder = args.Length > 0 ? args[0] : "/Users/gamzenurdemir/Documents/boa-codes-for-executer-extraction/boa-code-4-extruction";
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
        // --- PRE‑ENRICH: Couchbase ile hedefleri çöz ve modele işle ---
        // local class set (external tespiti için)
        var localClasses = new HashSet<(string ns, string cls)>(
            classInfos.Select(ci => (ci.Namespace ?? "", ci.Name ?? "")));

        await using var cb = await CouchbaseService.ConnectAsync();

        // (İlk koşuda Couchbase snapshot istersen açabilirsin)
        await cb.UpsertClassesAsync(classInfos);

        var resolvedTargets = new Dictionary<(string method, string req, string resp), (string ns, string cls)>();

        foreach (var ci in classInfos)
        {
            foreach (var m in ci.Methods)
            {
                foreach (var ex in m.ExecuterCalls)
                {
                    if (string.IsNullOrWhiteSpace(ex.MethodName) ||
                        string.IsNullOrWhiteSpace(ex.RequestType) ||
                        string.IsNullOrWhiteSpace(ex.ResponseType))
                        continue;

                    var key = (ex.MethodName, ex.RequestType, ex.ResponseType);
                    if (!resolvedTargets.TryGetValue(key, out var tgt))
                    {
                        var hit = await cb.ResolveExecuterTargetAsync(ex);
                        if (hit is null) continue;
                        tgt = (hit.Value.Namespace, hit.Value.ClassName);
                        resolvedTargets[key] = tgt;

                        Console.WriteLine($"[Executer-Resolve] {ex.MethodName} <{ex.RequestType},{ex.ResponseType}> -> {tgt.ns}.{tgt.cls}");
                    }

                    // modeli doldur
                    ex.Namespace = tgt.ns;
                    ex.ClassName = tgt.cls;
                    ex.IsExternal = !localClasses.Contains((tgt.ns, tgt.cls));
                }
            }
        }

        // Çözümü Couchbase JSON’larına da yansıt (opsiyonel ama önerilir)
        await cb.ApplyExecuterResolutionsAsync(classInfos, resolvedTargets, localClasses);

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
                        await neo4jService.EnsureMethodNodeAsync(im.MethodName, im.ClassName, im.Namespace);

                        await neo4jService.CreateMethodCallsMethodRelationAsync(
                            srcNamespace, srcClass, srcMethod,
                            im.Namespace, im.ClassName, im.MethodName,
                            "CALLS"
                        );
                    }

                    foreach (var ex in m.ExecuterCalls)
                    {
                        if (string.IsNullOrEmpty(ex.MethodName)) continue;

                        // 1) Önce owner’ı (ns/class) Couchbase’ten çözmeyi dene
                        var hit = await cb.ResolveExecuterTargetAsync(ex);

                        if (hit is { } t)
                        {
                            // (ÖNERİLEN) Eğer daha önce sadece imzaya göre bir node oluşturmuşsan,
                            // onu owner bilgisiyle upgrade et. (Yoksa MATCH etmez; sorun değil.)
                            await neo4jService.UpgradeMethodNodeOwnerAsync(
                                ex.MethodName, ex.RequestType, ex.ResponseType,
                                t.ClassName, t.Namespace
                            );

                            // 2) Hedef method node’unu tipleriyle garanti et (boşsa doldurur)
                            await neo4jService.CreateMethodNodeAsync(
                                ex.MethodName,
                                t.ClassName,
                                t.Namespace,
                                ex.RequestType,
                                ex.ResponseType
                            );

                            // 3) Tam kimlikle EXECUTES
                            await neo4jService.CreateMethodCallsMethodRelationAsync(
                                srcNamespace, srcClass, srcMethod,
                                t.Namespace, t.ClassName, ex.MethodName,
                                "EXECUTES"
                            );
                        }
                        else
                        {
                            // Owner çözülemedi → imzaya göre hedef node’u yine de oluştur
                            await neo4jService.EnsureMethodNodeBySignatureAsync(
                                ex.MethodName, ex.RequestType, ex.ResponseType
                            );

                            // İmzaya göre EXECUTES (mevcut fallback)
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
