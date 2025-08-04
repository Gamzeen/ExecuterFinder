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

            // 1. Gerekli referansları ekle (System, LINQ, vs.)
            var refs = new List<MetadataReference>
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location)
            };
            // Eğer projenin kendi derlenmiş dll'leri varsa buraya ekle: (ÖRNEK)
            // refs.Add(MetadataReference.CreateFromFile(@"C:\projelerim\BOA.Business.Kernel.Loans.RetailFinance.dll"));

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
                Console.WriteLine($"\nANALYZING CLASS: {classInfo.Name} \nIn file: {classInfo.FilePath} \nNamespace: {classInfo.Namespace}");

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
                    Console.WriteLine($"|-> FOUND METHOD: \n\tMethod Name: {methodInfo.Name}  \n\tRequest Type: {methodInfo.RequestType} \n\tResponse Type: {methodInfo.ResponseType}");

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

                    // --- Sadece iş class'ı method çağrılarını semantic ile bul ---
                    foreach (var invocation in methodNode.DescendantNodes().OfType<InvocationExpressionSyntax>())
                    {
                        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
                        {
                            var methodName = memberAccess.Name.Identifier.Text;

                            // Çağıran değişkenin tipini semantic ile bul
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
                                string fullTypeName = typeSymbol.ToDisplayString(); // Tam namespace ile
                                string assemblyName = typeSymbol.ContainingAssembly.Name;

                                // Sadece System/Framework class'larını atla, diğerlerini ekle
                                bool isFramework = assemblyName.StartsWith("System") || assemblyName == "mscorlib";

                                if (!isFramework&& methodName != "InitializeGenericResponse")
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
                                    Console.WriteLine($"\n\t|-> FOUND INVOKED BOA METHOD: \n\t\t Method Name :{methodName} \n\t\t Namespace :{ns} \n\t\t Class Name : {className} ");
                           
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
