namespace ExecuterFinder.Models
{
    public class ClassInfo
    {
        public string Name { get; set; }
        public string FilePath { get; set; }
        public List<MethodInfo> Methods { get; set; } = new();
        public string ClassType { get; set; } // class, interface vs.
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
        public string ClassName { get; set; } // Örn: BOA.Business.Kernel.Loans.RetailFinance.RetailLimitHistory
        public string MethodName { get; set; } // Örn: GetRetailLimitHistoryByPersonIdApplicationId
    }

}
