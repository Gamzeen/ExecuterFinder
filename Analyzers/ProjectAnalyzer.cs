using ExecuterFinder.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

public static class ProjectAnalyzer
{
    public static List<ClassInfo> AnalyzeProject(string rootFolder)
    {
        var allClassInfos = new List<ClassInfo>();
        var allCsFiles = Directory.GetFiles(rootFolder, "*.cs", SearchOption.AllDirectories);

        foreach (var file in allCsFiles)
        {
            var code = File.ReadAllText(file);
            var tree = CSharpSyntaxTree.ParseText(code);
            var root = tree.GetCompilationUnitRoot();

            foreach (var classNode in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                var classInfo = new ClassInfo
                {
                    Name = classNode.Identifier.Text,
                    FilePath = file,
                    ClassType = "class"
                };

                Console.WriteLine($"\nANALYZING CLASS: {classInfo.Name} in file: {file}");

                foreach (var methodNode in classNode.DescendantNodes().OfType<MethodDeclarationSyntax>())
                {
                    // İlk parametrenin tipi genellikle "request"
                    string requestType = "";
                    if (methodNode.ParameterList.Parameters.Count > 0)
                        requestType = methodNode.ParameterList.Parameters[0].Type?.ToString() ?? "";

                    var methodInfo = new MethodInfo
                    {
                        Name = methodNode.Identifier.Text,
                        ResponseType = methodNode.ReturnType.ToString(),
                        RequestType = requestType
                    };
                    Console.WriteLine($"|-> FOUND METHOD: \n\tMethod Name: {methodInfo.Name} with \n\tRequest Type: {methodInfo.RequestType} and \n\tResponse Type: {methodInfo.ResponseType}");

                    // Executer çağrılarını bul
                    foreach (var invocation in methodNode.DescendantNodes().OfType<InvocationExpressionSyntax>())
                    {
                        var exprString = invocation.ToString();
                        // BOAExecuter<TRequest, TResponse>.Execute(xxx)
                        if (exprString.StartsWith("BOAExecuter<"))
                        {
                            var callInfo = ParseExecuterCall(invocation, exprString, root, methodNode);
                            if (callInfo != null)
                                methodInfo.ExecuterCalls.Add(callInfo);
                        }
                    }

                    classInfo.Methods.Add(methodInfo);
                }

                allClassInfos.Add(classInfo);
            }
        }
        return allClassInfos;
    }

    /// <summary>
    /// BOAExecuter çağrısından hem generic parametrelerini, hem de kullanılan değişkenin MethodName'ini bulur.
    /// </summary>
    private static ExecuterCallInfo? ParseExecuterCall(InvocationExpressionSyntax invocation, string exprString, SyntaxNode root, MethodDeclarationSyntax parentMethod)
    {
        // BOAExecuter<RequestType, ResponseType>.Execute(...)
        var genericStart = exprString.IndexOf('<');
        var genericEnd = exprString.IndexOf('>');
        if (genericStart < 0 || genericEnd < 0) return null;

        var genericTypes = exprString.Substring(genericStart + 1, genericEnd - genericStart - 1)
                            .Split(',').Select(x => x.Trim()).ToList();
        if (genericTypes.Count < 2) return null;

        string requestType = genericTypes[0];
        string responseType = genericTypes[1];

        string methodName = "";
        string requestVarName = "";

        // Argument bulma: BOAExecuter<...>.Execute(someVar)
        if (invocation.ArgumentList.Arguments.Count > 0)
        {
            var arg = invocation.ArgumentList.Arguments[0];
            requestVarName = arg.ToString();

            // 1. Önce variable initializer (var someVar = new ... { MethodName = "..." }) içinde ara
            var variableDecls = root.DescendantNodes().OfType<VariableDeclaratorSyntax>()
                .Where(v => v.Identifier.Text == requestVarName);

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
                            methodName = literal.Token.ValueText;
                            break;
                        }
                    }
                }
            }

            // 2. Eğer bulamazsa, method body'de someVar.MethodName = "..." atamalarını da tara
            if (string.IsNullOrEmpty(methodName) && parentMethod != null)
            {
                var assignments = parentMethod.DescendantNodes()
                    .OfType<AssignmentExpressionSyntax>()
                    .Where(a =>
                        a.Left.ToString() == $"{requestVarName}.MethodName" &&
                        a.Right is LiteralExpressionSyntax);

                foreach (var assignment in assignments)
                {
                    if (assignment.Right is LiteralExpressionSyntax literal)
                    {
                        methodName = literal.Token.ValueText;
                        break;
                    }
                }
            }
        }

        Console.WriteLine($"\n\t|-> FOUND EXECUTER CALL:  \n\t\t BOAExecuter<{requestType}, {responseType}>.Execute({requestVarName})  \n\t\t Request Type: {requestType}   \n\t\t Response Type: {responseType} \n\t\t MethodName: {methodName}");
        return new ExecuterCallInfo
        {
            RequestType = requestType,
            ResponseType = responseType,
            MethodName = methodName,
            RequestVariableName = requestVarName
        };
    }
}
