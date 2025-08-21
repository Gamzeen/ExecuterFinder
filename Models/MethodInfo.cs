namespace ExecuterFinder.Models;

public class MethodInfo
{
    public string Name { get; set; }
    public string ResponseType { get; set; }
    public string RequestType { get; set; }
    public List<ExecuterCallInfo> ExecuterCalls { get; set; } = new();
    public List<InvokeMethod> InvokedMethods { get; set; } = new();
    public List<string> StoredProcedures { get; set; } = new();
}