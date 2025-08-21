namespace ExecuterFinder.Models;

public class ExecuterCallInfo
{
    public string RequestType { get; set; }
    public string ResponseType { get; set; }
    public string MethodName { get; set; }
    public string RequestVariableName { get; set; }
    public string? ClassName { get; set; }
    public string? Namespace { get; set; }
    public bool IsExternal { get; set; } = false;
}