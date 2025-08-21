using Couchbase;
using Couchbase.KeyValue;
using Couchbase.Management.Buckets;
using Couchbase.Management.Collections;
using System.Text.Json;
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
        string username = "Administrator",
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
            // Bucket hazır olmadan ilerlemeyelim
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

    /// <summary>
    /// Her ClassInfo için tek bir JSON dokümanı upsert eder.
    /// Key formatı: "{namespace}::{className}"
    /// </summary>
    public async Task UpsertClassesAsync(IEnumerable<ClassInfo> classes)
    {
        var tasks = new List<Task>();

        foreach (var ci in classes)
        {
            // Key: namespace::className  (boşlar ve boşluklar normalize ediliyor)
            var ns = Sanitize(ci.Namespace);
            var cls = Sanitize(ci.Name);
            var key = $"{ns}::{cls}";

            // JSON doküman – mevcut ClassInfo modelin birebir gömülüyor
            var doc = new
            {
                docType = "ClassInfo",
                @namespace = ci.Namespace ?? "(global)",
                className = ci.Name,
                classType = ci.ClassType,
                filePath = ci.FilePath,
                methods = ci.Methods, // MethodInfo listesi doğrudan JSON olur
                createdAt = DateTimeOffset.UtcNow
            };

            tasks.Add(_collection.UpsertAsync(key, doc));
        }

        // Paralel yaz (Couchbase SDK kendi bağlantı havuzunu yönetir)
        await Task.WhenAll(tasks);
    }

    // --- Helpers ---

    private static string Sanitize(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "(global)";
        // Key güvenliği için boşluk ve kontrol karakterlerini sadeleştirelim
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
        if (!scopeExists)
        {
            await cm.CreateScopeAsync(scopeName);
        }

        var collExists = manifest
            .FirstOrDefault(s => s.Name == scopeName)?
            .Collections.Any(c => c.Name == collectionName) == true;

        if (!collExists)
        {
            await cm.CreateCollectionAsync(new CollectionSpec(scopeName, collectionName));
        }
    }
}
