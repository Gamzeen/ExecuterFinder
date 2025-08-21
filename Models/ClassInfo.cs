namespace ExecuterFinder.Models;

public class ClassInfo
{
    public string Name { get; set; }
    public string ClassType { get; set; } // class, interface , model, etc.
    public string FilePath { get; set; }
    public string Namespace { get; set; }
    public List<MethodInfo> Methods { get; set; } = new();
}