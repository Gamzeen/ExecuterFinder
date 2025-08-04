namespace ExecuterFinder.Models;

public class ClassInfo
{
    public string Name { get; set; }
    public string ClassType { get; set; } // class, interface , model, etc.
    public string FilePath { get; set; }
    public string Namespace { get; set; }
    public List<MethodInfo> Methods { get; set; } = new();
}

public class MethodInfo
{
    public string Name { get; set; }
    public string ResponseType { get; set; }
    public string RequestType { get; set; }
    public List<ExecuterCallInfo> ExecuterCalls { get; set; } = new();
    public List<InvokeMethod> InvokedMethods { get; set; } = new();
}

public class InvokeMethod
{
    public string Namespace { get; set; }      // BOA.Business.Kernel.Loans.RetailFinance
    public string ClassName { get; set; }      // DealerTransaction
    public string MethodName { get; set; }     // GetDealerOngoingApplication
}

public class ExecuterCallInfo
{
    public string RequestType { get; set; }
    public string ResponseType { get; set; }
    public string MethodName { get; set; }
    public string RequestVariableName { get; set; }
}