using Neo4j.Driver;
using System.Threading.Tasks;

public class Neo4jService
{
    private readonly IDriver _driver;
    public Neo4jService(string uri, string user, string password)
    {
        _driver = GraphDatabase.Driver(uri, AuthTokens.Basic(user, password));
    }

    public async Task CreateClassNodeAsync(string className, string namespaceName)
    {
        var query = "MERGE (c:Class {name: $className, namespace: $namespaceName})";
        await using var session = _driver.AsyncSession();
        await session.RunAsync(query, new { className, namespaceName });
    }

    public async Task CreateMethodNodeAsync(string methodName, string className, string namespaceName)
    {
        var query = "MERGE (m:Method {name: $methodName, className: $className, namespace: $namespaceName})";
        await using var session = _driver.AsyncSession();
        await session.RunAsync(query, new { methodName, className, namespaceName });
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
}
