using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
namespace Library.Tools;
public sealed record MethodMetric(string Name, int Lines, int Complexity, double Volume, double Mi);
public sealed record ClassMetric(string Name, MethodMetric[] Methods, int Coupling, int Lcom1);
public sealed record Finding(string Rule, string Location, string Status, string Description);
/// <summary>Воспроизводимый синтаксический анализ Roslyn; не семантический CBO.</summary>
public static class CodeQuality
{
    public static ClassMetric Measure(string source, string name)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot();
        var type = root.DescendantNodes().OfType<ClassDeclarationSyntax>().Single(x => x.Identifier.ValueText == name);
        var methods = type.Members.OfType<MethodDeclarationSyntax>().ToArray();
        var ignored = new HashSet<string> { "Task", "List", "Stream", "Guid", "CancellationToken", "Exception" };
        int coupling = type.DescendantNodes().OfType<TypeSyntax>()
            .Where(x => x.Parent is ParameterSyntax or VariableDeclarationSyntax or ObjectCreationExpressionSyntax or BaseTypeSyntax)
            .Select(x => x.ToString()).Where(x => x.Length > 0 && char.IsUpper(x[0]) && !ignored.Contains(x) && x != name).Distinct().Count();
        var fields = type.Members.OfType<FieldDeclarationSyntax>().SelectMany(x => x.Declaration.Variables)
            .Select(x => x.Identifier.ValueText).ToHashSet();
        if (type.ParameterList is not null)
            fields.UnionWith(type.ParameterList.Parameters.Select(x => x.Identifier.ValueText));
        var access = methods.Select(x => x.DescendantNodes().OfType<IdentifierNameSyntax>()
            .Select(y => y.Identifier.ValueText).Where(fields.Contains).ToHashSet()).ToArray();
        int shared = 0, separate = 0;
        for (int i = 0; i < access.Length; i++) for (int j = i + 1; j < access.Length; j++)
            if (access[i].Overlaps(access[j])) shared++; else separate++;
        return new(name, methods.Select(Method).ToArray(), coupling, Math.Max(0, separate - shared));
    }
    public static MethodMetric Method(MethodDeclarationSyntax method)
    {
        int cc = 1;
        foreach (var node in method.DescendantNodes())
        {
            if (node is IfStatementSyntax or ForStatementSyntax or ForEachStatementSyntax or
                WhileStatementSyntax or DoStatementSyntax or CatchClauseSyntax or
                ConditionalExpressionSyntax or CaseSwitchLabelSyntax) cc++;
            if (node is BinaryExpressionSyntax b && (b.IsKind(SyntaxKind.LogicalAndExpression) ||
                b.IsKind(SyntaxKind.LogicalOrExpression) || b.IsKind(SyntaxKind.CoalesceExpression))) cc++;
        }
        SyntaxNode body = (SyntaxNode?)method.Body ?? (SyntaxNode?)method.ExpressionBody ?? method;
        int lines = body.ToString().Split('\n').Count(x => x.Trim().Length > 0 && !x.TrimStart().StartsWith("//"));
        var tokens = body.DescendantTokens().ToArray();
        bool Operand(SyntaxToken t) => t.IsKind(SyntaxKind.IdentifierToken) || t.IsKind(SyntaxKind.StringLiteralToken) || t.IsKind(SyntaxKind.NumericLiteralToken);
        int n = tokens.Where(Operand).Select(x => x.Text).Distinct().Count() + tokens.Where(x => !Operand(x)).Select(x => x.Text).Distinct().Count();
        double volume = tokens.Length * Math.Log2(Math.Max(2, n));
        double mi = Math.Clamp((171 - 5.2 * Math.Log(Math.Max(1, volume)) - .23 * cc - 16.2 * Math.Log(Math.Max(1, lines))) * 100 / 171, 0, 100);
        return new(method.Identifier.ValueText, lines, cc, Math.Round(volume, 2), Math.Round(mi, 2));
    }
    public static Finding[] Audit(string source)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot();var list = new List<Finding>();
        foreach (var m in root.DescendantNodes().OfType<MethodDeclarationSyntax>().Where(x => x.Modifiers.Any(SyntaxKind.PublicKeyword)))
        {
            string name = m.Identifier.ValueText;
            if (!Regex.IsMatch(name, "^[A-Z][A-Za-z0-9]*$")) list.Add(new("AUD-01", name, "Не соответствует", "Имя метода не PascalCase"));
            if (!m.GetLeadingTrivia().ToFullString().Contains("<summary>")) list.Add(new("AUD-02", name, "Требует доработки", "Нет XML summary"));
            if (Method(m).Complexity > 10 || Method(m).Lines > 30) list.Add(new("AUD-03", name, "Требует доработки", "Сложный или длинный метод"));
        }
        if (source.Contains("Path.Combine(Root, entry.FullName)")) list.Add(new("AUD-04", "ZIP", "Не соответствует", "Непроверенный путь"));
        if (source.Contains("role == \"Reader\"")) list.Add(new("AUD-05", "role", "Не соответствует", "Reader допущен к записи"));
        if (source.Contains("private string Root")) list.Add(new("AUD-06", "Root", "Не соответствует", "Приватное поле не _camelCase"));
        if (!source.Contains("Authorize")) list.Add(new("AUD-07", "Controller", "Не соответствует", "Нет серверной политики"));
        return list.ToArray();
    }
}
