using Neo4j.Driver;
using System.Threading.Tasks;

public class Neo4jService
{
    private readonly IDriver _driver;
    public Neo4jService(string uri, string user, string password)
    {
        _driver = GraphDatabase.Driver(uri, AuthTokens.Basic(user, password));
    }
    
    public async Task VerifyAsync()
    {
        await using var session = _driver.AsyncSession();
        var result = await session.RunAsync("RETURN 1 AS ok");
        var record = await result.SingleAsync();
        Console.WriteLine($"✅ Neo4j connection verified, ping result: {record["ok"]}");
    }

    public async Task CreateClassNodeAsync(string className, string namespaceName)
    {
        var query = "MERGE (c:Class {name: $className, namespace: $namespaceName})";
        await using var session = _driver.AsyncSession();
        await session.RunAsync(query, new { className, namespaceName });
    }

    public async Task CreateMethodNodeAsync(string methodName, string className, string namespaceName,
        string requestType, string responseType)
    {
        var query = @"
MERGE (m:Method {name:$methodName, className:$className, namespace:$namespaceName})
ON CREATE SET m.requestType=$requestType, m.responseType=$responseType
ON MATCH  SET m.requestType=coalesce(m.requestType,$requestType),
            m.responseType=coalesce(m.responseType,$responseType)";
        await using var session = _driver.AsyncSession();
        await session.RunAsync(query, new { methodName, className, namespaceName, requestType, responseType });
    }

    public async Task CreateClassHasMethodRelationAsync(string className, string namespaceName, string methodName)
    {
        var query = @"
MATCH (c:Class {name: $className, namespace: $namespaceName})
MATCH (m:Method {name: $methodName, className: $className, namespace: $namespaceName})
MERGE (c)-[:HAS_METHOD]->(m)";
        await using var session = _driver.AsyncSession();
        await session.RunAsync(query, new { className, namespaceName, methodName });
    }

    public async Task CreateMethodCallsMethodRelationAsync(
        string srcNamespace, string srcClass, string srcMethod,
        string tgtNamespace, string tgtClass, string tgtMethod,
        string relationType = "CALLS")
    {
        var query = $@"
MATCH (src:Method {{namespace: $srcNamespace, className: $srcClass, name: $srcMethod}})
MATCH (tgt:Method {{namespace: $tgtNamespace, className: $tgtClass, name: $tgtMethod}})
MERGE (src)-[:{relationType}]->(tgt)";
        await using var session = _driver.AsyncSession();
        await session.RunAsync(query, new
        {
            srcNamespace, srcClass, srcMethod,
            tgtNamespace, tgtClass, tgtMethod
        });
    }

    public async Task CreateExecuterRelationBySignatureAsync(
        string srcNamespace, string srcClass, string srcMethod,
        string targetMethodName, string targetRequestType, string targetResponseType)
    {
        var query = @"
MATCH (src:Method {namespace:$srcNamespace, className:$srcClass, name:$srcMethod})
MATCH (tgt:Method {name:$targetMethodName})
WHERE tgt.requestType = $targetRequestType AND tgt.responseType = $targetResponseType
MERGE (src)-[:EXECUTES {via:'BOAExecuter'}]->(tgt)";
        await using var session = _driver.AsyncSession();
        await session.RunAsync(query, new {
            srcNamespace, srcClass, srcMethod,
            targetMethodName, targetRequestType, targetResponseType
        });
    }

    // --- SP grafı (opsiyonel) ---
    public async Task CreateStoredProcedureNodeAsync(string spName)
    {
        var query = @"MERGE (s:StoredProcedure {name:$spName})";
        await using var session = _driver.AsyncSession();
        await session.RunAsync(query, new { spName });
    }

    public async Task CreateMethodExecutesStoredProcedureRelationAsync(
        string srcNamespace, string srcClass, string srcMethod,
        string spName)
    {
        var query = @"
        MATCH (m:Method {namespace:$srcNamespace, className:$srcClass, name:$srcMethod})
        MERGE (s:StoredProcedure {name:$spName})
        MERGE (m)-[:EXECUTES_SP]->(s)";
        await using var session = _driver.AsyncSession();
        await session.RunAsync(query, new { srcNamespace, srcClass, srcMethod, spName });
    }

    public async Task EnsureMethodNodeAsync(string methodName, string className, string namespaceName)
    {
        var query = @"
        MERGE (m:Method {name:$methodName, className:$className, namespace:$namespaceName})";
        await using var session = _driver.AsyncSession();
        await session.RunAsync(query, new { methodName, className, namespaceName });
    }

}
