using Couchbase;
using Couchbase.KeyValue;
using Couchbase.Management.Buckets;
using Couchbase.Management.Collections;
using ExecuterFinder.Models;

public sealed class CouchbaseService : IAsyncDisposable
{
    private readonly ICluster _cluster;
    private readonly IBucket _bucket;
    private readonly ICouchbaseCollection _collection;

    private CouchbaseService(ICluster cluster, IBucket bucket, ICouchbaseCollection collection)
    {
        _cluster = cluster;
        _bucket = bucket;
        _collection = collection;
    }

    public static async Task<CouchbaseService> ConnectAsync(
        string connectionString = "couchbase://localhost",
        string username = "admin",
        string password = "password",
        string bucketName = "codegraph",
        string scopeName = "_default",
        string collectionName = "_default",
        bool ensureProvision = true)
    {
        var cluster = await Cluster.ConnectAsync(connectionString, username, password);

        if (ensureProvision)
        {
            await EnsureBucketAsync(cluster, bucketName);
            await cluster.WaitUntilReadyAsync(TimeSpan.FromSeconds(20));
            var bucket = await cluster.BucketAsync(bucketName);
            await EnsureScopeAndCollectionAsync(bucket, scopeName, collectionName);
            var collection = bucket.Scope(scopeName).Collection(collectionName);
            return new CouchbaseService(cluster, bucket, collection);
        }
        else
        {
            var bucket = await cluster.BucketAsync(bucketName);
            var scope = bucket.Scope(scopeName);
            var collection = scope.Collection(collectionName);
            return new CouchbaseService(cluster, bucket, collection);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cluster.DisposeAsync();
    }

    // --- Public API ---

    /// <summary>Her ClassInfo için tek bir JSON dokümanı upsert eder.
    /// Key formatı: "{namespace}::{className}"</summary>
    public async Task UpsertClassesAsync(IEnumerable<ClassInfo> classes)
    {
        var tasks = new List<Task>();
        foreach (var ci in classes)
        {
            var ns = Sanitize(ci.Namespace);
            var cls = Sanitize(ci.Name);
            var key = $"{ns}::{cls}";

            var doc = new
            {
                docType = "ClassInfo",
                @namespace = ci.Namespace ?? "(global)",
                className = ci.Name,
                classType = ci.ClassType,
                filePath = ci.FilePath,
                methods = ci.Methods,
                createdAt = DateTimeOffset.UtcNow
            };

            tasks.Add(_collection.UpsertAsync(key, doc));
        }
        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// BOAExecuter hedefini (MethodName + RequestType + ResponseType) ile koleksiyonda arar,
    /// bulursa (namespace, className) döner.
    /// </summary>
    public async Task<(string Namespace, string ClassName)?> ResolveExecuterTargetAsync(ExecuterCallInfo ex)
    {
        if (string.IsNullOrWhiteSpace(ex.MethodName) ||
            string.IsNullOrWhiteSpace(ex.RequestType) ||
            string.IsNullOrWhiteSpace(ex.ResponseType))
            return null;

        // Basit N1QL: methods[*].name = ex.MethodName AND request/response eşleşmesi
        // Not: production'da index eklemen önerilir.
        var cluster = _cluster;
        var stmt = @"
SELECT c.`namespace` AS ns, c.className AS cls
FROM `" + _bucket.Name + @"`._default._default c
UNNEST c.methods m
WHERE m.name = $mname
  AND m.requestType = $req
  AND m.responseType = $resp
LIMIT 1";
        var q = await cluster.QueryAsync<dynamic>(stmt, options =>
        {
            options.Parameter("mname", ex.MethodName);
            options.Parameter("req", ex.RequestType);
            options.Parameter("resp", ex.ResponseType);
        });

        await foreach (var row in q)
        {
            string ns = row.ns;
            string cls = row.cls;
            return (ns, cls);
        }
        return null;
    }

    /// <summary>
    /// Pre‑enrich ile çözülen executer hedeflerini ilgili dokümanların
    /// methods[i].executerCalls[j] path'lerine sub‑doc patch olarak yazar.
    /// </summary>
    public async Task ApplyExecuterResolutionsAsync(
        IEnumerable<ClassInfo> classes,
        IReadOnlyDictionary<(string method, string req, string resp), (string ns, string cls)> resolved,
        ISet<(string ns, string cls)> localClasses)
    {
        foreach (var ci in classes)
        {
            var key = $"{Sanitize(ci.Namespace)}::{Sanitize(ci.Name)}";
            var specs = new List<MutateInSpec>();

            for (int mi = 0; mi < ci.Methods.Count; mi++)
            {
                var m = ci.Methods[mi];
                for (int ei = 0; ei < m.ExecuterCalls.Count; ei++)
                {
                    var ex = m.ExecuterCalls[ei];
                    if (string.IsNullOrWhiteSpace(ex.MethodName) ||
                        string.IsNullOrWhiteSpace(ex.RequestType) ||
                        string.IsNullOrWhiteSpace(ex.ResponseType))
                        continue;

                    var sig = (ex.MethodName, ex.RequestType, ex.ResponseType);
                    if (!resolved.TryGetValue(sig, out var tgt))
                        continue;

                    var basePath = $"methods[{mi}].executerCalls[{ei}]";
                    specs.Add(MutateInSpec.Upsert($"{basePath}.namespace", tgt.ns));
                    specs.Add(MutateInSpec.Upsert($"{basePath}.className", tgt.cls));
                    var isExt = !localClasses.Contains((tgt.ns, tgt.cls));
                    specs.Add(MutateInSpec.Upsert($"{basePath}.isExternal", isExt));
                }
            }

            if (specs.Count > 0)
            {
                await _collection.MutateInAsync(key, specs);
                Console.WriteLine($"[CB-Update] {key} için {specs.Count/3} executer çözümü JSON'a yazıldı.");
            }
        }
    }

    // --- Helpers ---
    private static string Sanitize(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "(global)";
        return s.Trim().Replace(" ", "");
    }

    private static async Task EnsureBucketAsync(ICluster cluster, string bucketName)
    {
        var bm = cluster.Buckets;
        var existing = await bm.GetAllBucketsAsync();
        if (!existing.ContainsKey(bucketName))
        {
            await bm.CreateBucketAsync(new BucketSettings
            {
                Name = bucketName,
                BucketType = BucketType.Couchbase,
                RamQuotaMB = 256,
                FlushEnabled = true
            });
        }
    }

    private static async Task EnsureScopeAndCollectionAsync(IBucket bucket, string scopeName, string collectionName)
    {
        var cm = bucket.Collections;
        var manifest = await cm.GetAllScopesAsync();

        var scopeExists = manifest.Any(s => s.Name == scopeName);
        if (!scopeExists) await cm.CreateScopeAsync(scopeName);

        var collExists = manifest
            .FirstOrDefault(s => s.Name == scopeName)?
            .Collections.Any(c => c.Name == collectionName) == true;

        if (!collExists) await cm.CreateCollectionAsync(new CollectionSpec(scopeName, collectionName));
    }
}
