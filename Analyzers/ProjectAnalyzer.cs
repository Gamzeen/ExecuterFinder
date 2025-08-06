using ExecuterFinder.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public static class ProjectAnalyzer
{
    public static List<ClassInfo> AnalyzeProject(string rootFolder)
    {
        var allClassInfos = new List<ClassInfo>();
        var allCsFiles = Directory.GetFiles(rootFolder, "*.cs", SearchOption.AllDirectories);

        foreach (var file in allCsFiles)
        {
            var code = File.ReadAllText(file);
            var syntaxTree = CSharpSyntaxTree.ParseText(code);

            var refs = new List<MetadataReference>
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location)
            };

            var compilation = CSharpCompilation.Create("Analysis", new[] { syntaxTree }, refs);
            var semanticModel = compilation.GetSemanticModel(syntaxTree);

            var root = syntaxTree.GetCompilationUnitRoot();

            foreach (var classNode in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                // NAMESPACE'İ BUL
                string classNamespace = "";
                var namespaceNode = classNode.Ancestors().OfType<NamespaceDeclarationSyntax>().FirstOrDefault();
                if (namespaceNode != null)
                    classNamespace = namespaceNode.Name.ToString();
                else
                    classNamespace = "(global)";

                var classInfo = new ClassInfo
                {
                    Name = classNode.Identifier.Text,
                    FilePath = file,
                    ClassType = "class",
                    Namespace = classNamespace
                };

                foreach (var methodNode in classNode.DescendantNodes().OfType<MethodDeclarationSyntax>())
                {
                    var methodInfo = new MethodInfo
                    {
                        Name = methodNode.Identifier.Text,
                        ResponseType = methodNode.ReturnType.ToString(),
                        RequestType = methodNode.ParameterList.Parameters.Count > 0
                            ? methodNode.ParameterList.Parameters[0].Type?.ToString() ?? ""
                            : ""
                    };

                    
                    foreach (var invocation in methodNode.DescendantNodes().OfType<InvocationExpressionSyntax>())
                    {
                        var exprString = invocation.ToString();

                        // 1. BOAExecuter<TRequest, TResponse>.Execute() kontrolü
                        if (exprString.StartsWith("BOAExecuter<"))
                        {
                            var callInfo = ParseExecuterCall(invocation, exprString, root, methodNode);
                            if (callInfo != null)
                                methodInfo.ExecuterCalls.Add(callInfo);
                        }

                        // 2. İş class'ı method çağrıları (semantic check)
                        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
                        {
                            var methodName = memberAccess.Name.Identifier.Text;
                            var exprSymbolInfo = semanticModel.GetSymbolInfo(memberAccess.Expression);
                            var exprSymbol = exprSymbolInfo.Symbol;

                            ITypeSymbol? typeSymbol = null;
                            if (exprSymbol is ILocalSymbol localSym)
                                typeSymbol = localSym.Type;
                            else if (exprSymbol is IParameterSymbol paramSym)
                                typeSymbol = paramSym.Type;
                            else if (exprSymbol is IFieldSymbol fieldSym)
                                typeSymbol = fieldSym.Type;
                            else if (exprSymbol is IPropertySymbol propSym)
                                typeSymbol = propSym.Type;

                            if (typeSymbol != null)
                            {
                                string fullTypeName = typeSymbol.ToDisplayString();
                                string assemblyName = typeSymbol.ContainingAssembly.Name;
                                bool isFramework = assemblyName.StartsWith("System") || assemblyName == "mscorlib";

                                if (!isFramework && methodName != "InitializeGenericResponse")
                                {
                                    if (methodInfo.InvokedMethods == null)
                                        methodInfo.InvokedMethods = new List<InvokeMethod>();

                                    int lastDot = fullTypeName.LastIndexOf(".");
                                    string ns = lastDot > 0 ? fullTypeName.Substring(0, lastDot) : "";
                                    string className = lastDot > 0 ? fullTypeName.Substring(lastDot + 1) : fullTypeName;

                                    methodInfo.InvokedMethods.Add(new InvokeMethod
                                    {
                                        ClassName = className,
                                        MethodName = methodName,
                                        Namespace = ns
                                    });
                                }
                            }
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
    /// Generic blokları eksiksiz parse eder.
    /// </summary>
    private static ExecuterCallInfo? ParseExecuterCall(InvocationExpressionSyntax invocation, string exprString, SyntaxNode root, MethodDeclarationSyntax parentMethod)
    {
        var genericStart = exprString.IndexOf('<');
        if (genericStart < 0) return null;

        var genericBlock = ExtractGenericBlock(exprString, genericStart);
        var genericTypes = SplitGenericTypes(genericBlock);

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

        return new ExecuterCallInfo
        {
            RequestType = requestType,
            ResponseType = responseType,
            MethodName = methodName,
            RequestVariableName = requestVarName
        };
    }

    /// <summary>
    /// Girilen stringte, açılış <'ten itibaren tüm generic bloğunu tam olarak çıkarır.
    /// </summary>
    private static string ExtractGenericBlock(string exprString, int start)
    {
        int depth = 0;
        int i = start;
        for (; i < exprString.Length; i++)
        {
            if (exprString[i] == '<') depth++;
            else if (exprString[i] == '>') depth--;
            if (depth == 0) break;
        }
        return exprString.Substring(start + 1, i - start - 1); // "<" hariç, ">" hariç
    }

    /// <summary>
    /// Generic blok içini dıştaki virgüllere göre ayırır. (İç içe genericlerde hata yapmaz.)
    /// </summary>
    private static List<string> SplitGenericTypes(string genericBlock)
    {
        var types = new List<string>();
        int depth = 0;
        int last = 0;
        for (int i = 0; i < genericBlock.Length; i++)
        {
            if (genericBlock[i] == '<') depth++;
            else if (genericBlock[i] == '>') depth--;
            else if (genericBlock[i] == ',' && depth == 0)
            {
                types.Add(genericBlock.Substring(last, i - last).Trim());
                last = i + 1;
            }
        }
        types.Add(genericBlock.Substring(last).Trim());
        return types;
    }
}
