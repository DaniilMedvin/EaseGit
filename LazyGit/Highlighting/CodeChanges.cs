using LazyGit.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LazyGit.Highlighting;

/// <summary>
/// Что изменилось в C#-файле на уровне кода: добавленные, удалённые и изменённые типы и члены (методы, свойства,
/// конструкторы, поля…). Сравнивается по токенам, поэтому правка пробелов, отступов и комментариев изменением не считается.
/// Только синтаксис, без компиляции: работает на любом файле, даже с ошибками.
/// </summary>
public static class CodeChanges
{
    private sealed record Member(string Kind, string What, string Name, string Code, bool IsType);

    /// <param name="oldText">Файл до изменения или null — файла не было.</param>
    /// <param name="newText">Файл после или null — файл удалён.</param>
    public static IReadOnlyList<CodeChange> Compare(string? oldText, string? newText)
    {
        var before = Members(oldText).ToDictionary(m => m.Kind + m.Name, m => m);
        var after = Members(newText).ToList();
        var changes = new List<CodeChange>();
        var addedTypes = new HashSet<string>();
        var removedTypes = new HashSet<string>();

        foreach (var member in after)
        {
            if (!before.TryGetValue(member.Kind + member.Name, out var old))
            {
                // Новый тип целиком — одна строка, без каждого его члена.
                if (ParentAdded(member.Name, addedTypes))
                    continue;
                if (member.IsType)
                    addedTypes.Add(member.Name);
                changes.Add(new CodeChange(CodeChangeKind.Added, member.What, member.Name));
            }
            else if (!member.IsType && old.Code != member.Code)
            {
                changes.Add(new CodeChange(CodeChangeKind.Modified, member.What, member.Name));
            }
        }

        var now = after.Select(m => m.Kind + m.Name).ToHashSet();
        foreach (var old in before.Values.Where(m => !now.Contains(m.Kind + m.Name)))
        {
            if (ParentAdded(old.Name, removedTypes))
                continue;
            if (old.IsType)
                removedTypes.Add(old.Name);
            changes.Add(new CodeChange(CodeChangeKind.Removed, old.What, old.Name));
        }

        return changes;
    }

    /// <summary>Член принадлежит уже учтённому (добавленному или удалённому целиком) типу.</summary>
    private static bool ParentAdded(string name, HashSet<string> types) =>
        types.Any(type => name.StartsWith(type + ".", StringComparison.Ordinal));

    private static IEnumerable<Member> Members(string? text)
    {
        if (string.IsNullOrEmpty(text))
            yield break;

        var root = CSharpSyntaxTree.ParseText(text).GetRoot();
        foreach (var node in root.DescendantNodes())
        {
            // «int a, b;» — каждое имя отдельным полем: добавили «c» — изменилось только оно.
            if (node is BaseFieldDeclarationSyntax field)
            {
                var isEvent = field is EventFieldDeclarationSyntax;
                var owner = Owner(field);
                foreach (var variable in field.Declaration.Variables)
                    yield return new Member(isEvent ? "event" : "field", isEvent ? T("событие", "event") : T("поле", "field"),
                        Qualify(owner, variable.Identifier.Text), Code(field.Declaration.Type) + " " + Code(variable), false);
                continue;
            }

            var member = Describe(node);
            if (member is not null)
                yield return member;
        }
    }

    private static Member? Describe(SyntaxNode node)
    {
        var owner = Owner(node);
        string Full(string name) => Qualify(owner, name);

        return node switch
        {
            BaseTypeDeclarationSyntax type => new Member("type", TypeWhat(type), Full(TypeName(type)), "", IsType: true),
            DelegateDeclarationSyntax d => new Member("delegate", T("делегат", "delegate"), Full(d.Identifier.Text), Code(d), false),
            MethodDeclarationSyntax m => new Member("method", T("метод", "method"), Full($"{m.Identifier.Text}{Generic(m.TypeParameterList)}({Parameters(m.ParameterList)})"), Code(m), false),
            ConstructorDeclarationSyntax c => new Member("ctor", T("конструктор", "constructor"), Full($"{c.Identifier.Text}({Parameters(c.ParameterList)})"), Code(c), false),
            DestructorDeclarationSyntax d => new Member("dtor", T("деструктор", "destructor"), Full($"~{d.Identifier.Text}()"), Code(d), false),
            PropertyDeclarationSyntax p => new Member("property", T("свойство", "property"), Full(p.Identifier.Text), Code(p), false),
            IndexerDeclarationSyntax i => new Member("indexer", T("индексатор", "indexer"), Full($"this[{Parameters(i.ParameterList)}]"), Code(i), false),
            EventDeclarationSyntax e => new Member("event", T("событие", "event"), Full(e.Identifier.Text), Code(e), false),
            OperatorDeclarationSyntax o => new Member("operator", T("оператор", "operator"), Full($"operator {o.OperatorToken.Text}({Parameters(o.ParameterList)})"), Code(o), false),
            ConversionOperatorDeclarationSyntax c => new Member("conversion", T("оператор", "operator"), Full($"{c.ImplicitOrExplicitKeyword.Text} operator {c.Type}"), Code(c), false),
            EnumMemberDeclarationSyntax e => new Member("enum-member", T("значение", "value"), Full(e.Identifier.Text), Code(e), false),
            _ => null,
        };
    }

    /// <summary>Типы, внутри которых объявлен узел: «Outer.Inner».</summary>
    private static string Owner(SyntaxNode node) =>
        string.Join(".", node.Ancestors().OfType<BaseTypeDeclarationSyntax>().Reverse().Select(TypeName));

    private static string Qualify(string owner, string name) => owner.Length == 0 ? name : $"{owner}.{name}";

    private static string TypeName(BaseTypeDeclarationSyntax type) =>
        type.Identifier.Text + (type is TypeDeclarationSyntax { TypeParameterList: { } list } ? Generic(list) : "");

    private static string TypeWhat(BaseTypeDeclarationSyntax type) => type switch
    {
        RecordDeclarationSyntax r when r.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword) => T("record struct", "record struct"),
        RecordDeclarationSyntax => T("record", "record"),
        StructDeclarationSyntax => T("структура", "struct"),
        InterfaceDeclarationSyntax => T("интерфейс", "interface"),
        EnumDeclarationSyntax => T("enum", "enum"),
        _ => T("класс", "class"),
    };

    private static string Generic(TypeParameterListSyntax? list) =>
        list is null ? "" : $"<{string.Join(", ", list.Parameters.Select(p => p.Identifier.Text))}>";

    /// <summary>Только типы параметров: «string, int» — по ним различаются перегрузки, имена параметров не важны.</summary>
    private static string Parameters(BaseParameterListSyntax list) =>
        string.Join(", ", list.Parameters.Select(p => p.Type?.ToString() ?? p.Identifier.Text));

    /// <summary>Код члена токенами через пробел: форматирование и комментарии не влияют на сравнение.</summary>
    private static string Code(SyntaxNode node) => string.Join(" ", node.DescendantTokens().Select(t => t.Text));
}
