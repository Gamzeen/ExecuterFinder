using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ExecuterFinder.Models; // BURASI ÖNEMLİ

namespace ExecuterFinder.Resolvers
{
    public class MethodMatchResult
    {
        public string ClassName { get; set; }
        public string MethodName { get; set; }
        public string FilePath { get; set; }
    }

    public class OrchestrationMethodResolver
    {
        public List<MethodMatchResult> FindMatchingMethods(string rootPath, ExecuterCallInfo call)
        {
            var results = new List<MethodMatchResult>();
            var allCsFiles = Directory.GetFiles(rootPath, "*.cs", SearchOption.AllDirectories);

            foreach (var file in allCsFiles)
            {
                var code = File.ReadAllText(file);
                var tree = CSharpSyntaxTree.ParseText(code);
                var root = tree.GetCompilationUnitRoot();

                var classNodes = root.DescendantNodes().OfType<ClassDeclarationSyntax>();

                foreach (var classNode in classNodes)
                {
                    if (!classNode.Identifier.Text.Contains(GetClassNameFromRequest(call.RequestType)))
                        continue;

                    var methods = classNode.Members.OfType<MethodDeclarationSyntax>();
                    foreach (var method in methods)
                    {
                        if (method.Identifier.Text == call.MethodName)
                        {
                            results.Add(new MethodMatchResult
                            {
                                ClassName = classNode.Identifier.Text,
                                MethodName = method.Identifier.Text,
                                FilePath = file
                            });
                        }
                    }
                }
            }

            return results;
        }

        private string GetClassNameFromRequest(string requestType)
        {
            return requestType.Replace("Request", "");
        }
    }
}
