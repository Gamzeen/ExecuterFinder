using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ExecuterFinder.Models; 

namespace ExecuterFinder.Analyzers
{
    public class BOAExecuterAnalyzer
    {
        public List<ExecuterCallInfo> AnalyzeExecuterCalls(string code)
        {
            var tree = CSharpSyntaxTree.ParseText(code);
            var root = tree.GetCompilationUnitRoot();
            var calls = new List<ExecuterCallInfo>();

            var invocationNodes = root.DescendantNodes().OfType<InvocationExpressionSyntax>();

            foreach (var invocation in invocationNodes)
            {
                if (invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
                    memberAccess.Name.Identifier.Text == "Execute" &&
                    memberAccess.Expression is GenericNameSyntax genericName &&
                    genericName.Identifier.Text == "BOAExecuter" &&
                    genericName.TypeArgumentList.Arguments.Count == 2)
                {
                    var requestType = genericName.TypeArgumentList.Arguments[0].ToString();
                    var responseType = genericName.TypeArgumentList.Arguments[1].ToString();

                    var arg = invocation.ArgumentList.Arguments.FirstOrDefault();
                    var requestVar = arg?.Expression.ToString();

                    var methodName = FindMethodNameForRequestVariable(root, requestVar);

                    calls.Add(new ExecuterCallInfo
                    {
                        RequestType = requestType,
                        ResponseType = responseType,
                        MethodName = methodName,
                        RequestVariableName = requestVar
                    });
                }
            }

            return calls;
        }

        private string FindMethodNameForRequestVariable(SyntaxNode root, string variableName)
        {
            var variableDecls = root.DescendantNodes().OfType<VariableDeclaratorSyntax>()
                .Where(v => v.Identifier.Text == variableName);

            foreach (var variable in variableDecls)
            {
                if (variable.Initializer?.Value is ObjectCreationExpressionSyntax objInit &&
                    objInit.Initializer is InitializerExpressionSyntax init)
                {
                    foreach (var expr in init.Expressions.OfType<AssignmentExpressionSyntax>())
                    {
                        if (expr.Left.ToString() == "MethodName" &&
                            expr.Right is LiteralExpressionSyntax literal)
                        {
                            return literal.Token.ValueText;
                        }
                    }
                }
            }

            return null;
        }
    }
}
