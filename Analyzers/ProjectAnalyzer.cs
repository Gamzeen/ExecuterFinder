using ExecuterFinder.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

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
                string classNamespace = classNode.Ancestors().OfType<BaseNamespaceDeclarationSyntax>()
                                                 .FirstOrDefault()?.Name.ToString() ?? "(global)";

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
                            ? (methodNode.ParameterList.Parameters[0].Type?.ToString() ?? "")
                            : ""
                    };

                    // 1) BOAExecuter ve diğer metot çağrıları (mevcut akış)
                    foreach (var invocation in methodNode.DescendantNodes().OfType<InvocationExpressionSyntax>())
                    {
                        var exprString = invocation.ToString();

                        // BOAExecuter<,>
                        if (exprString.StartsWith("BOAExecuter<"))
                        {
                            var callInfo = ParseExecuterCall(invocation, exprString, root, methodNode);
                            if (callInfo != null)
                                methodInfo.ExecuterCalls.Add(callInfo);
                        }

                        // Diğer çağrılar
                        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
                        {
                            var methodName = memberAccess.Name.Identifier.Text;
                            var expr = memberAccess.Expression;

                            ITypeSymbol typeSymbol = TryResolveTypeSymbol(semanticModel, expr);
                            string fullTypeNameFromCode = null;
                            if (typeSymbol == null)
                                fullTypeNameFromCode = TryResolveTypeNameFromCode(root, classNode, methodNode, expr);

                            if (IsSkippableHelperCall(methodName)) continue;

                            if (typeSymbol != null)
                            {
                                if (IsFrameworkOrBuiltin(typeSymbol)) continue;

                                string fullTypeName = typeSymbol.ToDisplayString();
                                SplitFullName(fullTypeName, out var ns, out var cls);
                                AddInvoke(methodInfo, ns, cls, methodName);
                            }
                            else if (!string.IsNullOrWhiteSpace(fullTypeNameFromCode))
                            {
                                SplitFullName(fullTypeNameFromCode, out var ns, out var cls);
                                if (string.IsNullOrEmpty(ns)) continue;
                                if (ns.StartsWith("System") || ns.StartsWith("Microsoft")) continue;

                                AddInvoke(methodInfo, ns, cls, methodName);
                            }
                        }
                    }

                    // 2) SP isimlerini çıkar
                    ExtractSpNamesFromMethod(methodNode, semanticModel, methodInfo);

                    // 3) Tekilleştir
                    if (methodInfo.InvokedMethods?.Count > 0)
                    {
                        methodInfo.InvokedMethods = methodInfo.InvokedMethods
                            .GroupBy(x => (x.Namespace, x.ClassName, x.MethodName))
                            .Select(g => g.First()).ToList();
                    }
                    if (methodInfo.StoredProcedures?.Count > 0)
                    {
                        methodInfo.StoredProcedures = methodInfo.StoredProcedures.Distinct().ToList();
                    }

                    classInfo.Methods.Add(methodInfo);
                }

                allClassInfos.Add(classInfo);
            }
        }

        return allClassInfos;
    }

    // --- SP isimleri (sadece ad) ---
    private static void ExtractSpNamesFromMethod(MethodDeclarationSyntax methodNode, SemanticModel model, MethodInfo methodInfo)
    {
        // A) DBLayer.GetDBCommand(..., "Schema.SpName")
        foreach (var inv in methodNode.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (inv.Expression is MemberAccessExpressionSyntax ma &&
                ma.Name.Identifier.Text == "GetDBCommand")
            {
                var args = inv.ArgumentList?.Arguments;
                if (args != null && args.Value.Count >= 2)
                {
                    var second = args.Value[1].Expression;
                    if (second is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.StringLiteralExpression))
                    {
                        var sp = lit.Token.ValueText?.Trim();
                        if (!string.IsNullOrWhiteSpace(sp))
                            methodInfo.StoredProcedures.Add(sp);
                    }
                }
            }
        }

        // B) SqlCommand("Schema.SpName") veya cmd.CommandText = "EXEC dbo.SpName ..."
        //    (genişletilmiş kapsama)
        var creations = methodNode.DescendantNodes()
            .OfType<ObjectCreationExpressionSyntax>()
            .Where(o =>
            {
                var t = model.GetTypeInfo(o);
                var tn = t.Type?.ToDisplayString();
                return tn == "System.Data.SqlClient.SqlCommand" ||
                       tn == "Microsoft.Data.SqlClient.SqlCommand";
            });

        foreach (var obj in creations)
        {
            if (obj.ArgumentList?.Arguments.Count > 0)
            {
                var firstArg = obj.ArgumentList.Arguments[0].Expression;
                if (firstArg is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.StringLiteralExpression))
                {
                    TryAddSp(methodInfo.StoredProcedures, lit.Token.ValueText);
                }
            }
        }

        var assignments = methodNode.DescendantNodes().OfType<AssignmentExpressionSyntax>();
        foreach (var assign in assignments)
        {
            if (assign.Left is MemberAccessExpressionSyntax leftMa &&
                leftMa.Name.Identifier.Text == "CommandText")
            {
                if (assign.Right is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.StringLiteralExpression))
                {
                    TryAddSp(methodInfo.StoredProcedures, lit.Token.ValueText);
                }
            }
        }
    }

    private static void TryAddSp(List<string> list, string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return;

        string trimmed = raw.Trim();

        // EXEC kalıbı
        var mExec = Regex.Match(trimmed,
            @"\bexec(?:ute)?\s+((?:\[[^\]]+\]|\w+)(?:\.(?:\[[^\]]+\]|\w+))?)",
            RegexOptions.IgnoreCase);
        if (mExec.Success)
        {
            list.Add(NormalizeSpName(mExec.Groups[1].Value));
            return;
        }

        // Düz isim: SpName | dbo.SpName | [dbo].[SpName]
        var mPlain = Regex.Match(trimmed,
            @"^((?:\[[^\]]+\]|\w+)(?:\.(?:\[[^\]]+\]|\w+))?)$");
        if (mPlain.Success)
        {
            list.Add(NormalizeSpName(mPlain.Groups[1].Value));
        }
    }

    private static string NormalizeSpName(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return s;
        return s.Replace("[", "").Replace("]", "").Trim();
    }

    // --- mevcut yardımcılar ---
    private static void AddInvoke(MethodInfo methodInfo, string ns, string className, string methodName)
    {
        if (string.IsNullOrWhiteSpace(className)) return;

        methodInfo.InvokedMethods ??= new List<InvokeMethod>();
        methodInfo.InvokedMethods.Add(new InvokeMethod
        {
            ClassName = className,
            MethodName = methodName,
            Namespace = ns ?? ""
        });
    }

    private static ITypeSymbol TryResolveTypeSymbol(SemanticModel model, ExpressionSyntax expr)
    {
        if (expr == null) return null;

        var tinfo = model.GetTypeInfo(expr);
        if (tinfo.Type != null && tinfo.Type.Kind != SymbolKind.ErrorType)
            return tinfo.Type;

        var sinfo = model.GetSymbolInfo(expr);
        var sym = sinfo.Symbol ?? sinfo.CandidateSymbols.FirstOrDefault();
        if (sym is ILocalSymbol ls && ls.Type?.Kind != SymbolKind.ErrorType) return ls.Type;
        if (sym is IFieldSymbol fs && fs.Type?.Kind != SymbolKind.ErrorType) return fs.Type;
        if (sym is IPropertySymbol ps && ps.Type?.Kind != SymbolKind.ErrorType) return ps.Type;
        if (sym is IParameterSymbol prs && prs.Type?.Kind != SymbolKind.ErrorType) return prs.Type;

        return null;
    }

    private static string TryResolveTypeNameFromCode(SyntaxNode root, ClassDeclarationSyntax classNode, MethodDeclarationSyntax methodNode, ExpressionSyntax expr)
    {
        string ident = (expr as IdentifierNameSyntax)?.Identifier.Text;
        if (string.IsNullOrWhiteSpace(ident)) return null;

        var localDecl = methodNode.DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .FirstOrDefault(v => v.Identifier.Text == ident && v.Initializer?.Value is ObjectCreationExpressionSyntax);
        if (localDecl?.Initializer?.Value is ObjectCreationExpressionSyntax objCreate1)
        {
            var typeName = objCreate1.Type?.ToString();
            return NormalizeTypeName(typeName);
        }

        var assign = methodNode.DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .FirstOrDefault(a => a.Left is IdentifierNameSyntax id && id.Identifier.Text == ident &&
                                 a.Right is ObjectCreationExpressionSyntax);
        if (assign?.Right is ObjectCreationExpressionSyntax objCreate2)
        {
            var typeName = objCreate2.Type?.ToString();
            return NormalizeTypeName(typeName);
        }

        var fieldDecl = classNode.DescendantNodes().OfType<FieldDeclarationSyntax>()
            .SelectMany(fd => fd.Declaration.Variables.Select(v => (fd, v)))
            .FirstOrDefault(p => p.v.Identifier.Text == ident);
        if (fieldDecl.fd != null)
        {
            var t = fieldDecl.fd.Declaration.Type?.ToString();
            if (!string.IsNullOrWhiteSpace(t)) return NormalizeTypeName(t);
        }

        var propDecl = classNode.DescendantNodes().OfType<PropertyDeclarationSyntax>()
            .FirstOrDefault(pd => pd.Identifier.Text == ident);
        if (propDecl != null)
        {
            var t = propDecl.Type?.ToString();
            if (!string.IsNullOrWhiteSpace(t)) return NormalizeTypeName(t);
        }

        return null;
    }

    private static string NormalizeTypeName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (raw.StartsWith("global::")) raw = raw.Substring("global::".Length);
        return raw.Trim();
    }

    private static void SplitFullName(string fullTypeName, out string ns, out string className)
    {
        ns = "";
        className = fullTypeName ?? "";
        var lastDot = (fullTypeName ?? "").LastIndexOf('.');
        if (lastDot > 0)
        {
            ns = fullTypeName.Substring(0, lastDot);
            className = fullTypeName.Substring(lastDot + 1);
        }
    }

    private static bool IsFrameworkOrBuiltin(ITypeSymbol t)
    {
        if (t == null) return true;

        if (t is IArrayTypeSymbol) return true;

        if (t.SpecialType == SpecialType.System_String) return true;
        if (t.SpecialType != SpecialType.None) return true;

        if (t.IsAnonymousType) return true;

        var ns = t.ContainingNamespace?.ToString() ?? "";
        if (ns.StartsWith("System") || ns.StartsWith("Microsoft")) return true;

        var asm = t.ContainingAssembly?.Name ?? "";
        if (asm == "mscorlib" || asm.StartsWith("System") || asm.StartsWith("Microsoft")) return true;

        return false;
    }

    private static bool IsSkippableHelperCall(string methodName)
    {
        if (string.IsNullOrEmpty(methodName)) return true;
        switch (methodName)
        {
            case "ToString":
            case "Split":
            case "Count":
            case "FindAll":
            case "AddRange":
            case "Trim":
            case "FirstOrDefault":
            case "Where":
            case "Select":
            case "Any":
            case "All":
            case "First":
            case "Last":
            case "Contains":
                return true;
            default:
                return false;
        }
    }

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
                    .Where(a => a.Left.ToString() == $"{requestVarName}.MethodName" &&
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
        return exprString.Substring(start + 1, i - start - 1);
    }

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
