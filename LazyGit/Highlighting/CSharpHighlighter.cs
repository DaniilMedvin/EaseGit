using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LazyGit.Highlighting;

/// <summary>
/// Подсветка C# на основе синтаксического дерева Roslyn, без семантической модели.
/// Типы, методы и переменные определяются по позиции в коде, поэтому код может быть
/// фрагментом (например, частью diff) и не компилироваться.
/// </summary>
public static class CSharpHighlighter
{
    private static readonly HashSet<SyntaxKind> ControlKeywords =
    [
        SyntaxKind.IfKeyword, SyntaxKind.ElseKeyword, SyntaxKind.SwitchKeyword, SyntaxKind.CaseKeyword,
        SyntaxKind.ForKeyword, SyntaxKind.ForEachKeyword, SyntaxKind.WhileKeyword, SyntaxKind.DoKeyword,
        SyntaxKind.BreakKeyword, SyntaxKind.ContinueKeyword, SyntaxKind.ReturnKeyword, SyntaxKind.GotoKeyword,
        SyntaxKind.ThrowKeyword, SyntaxKind.TryKeyword, SyntaxKind.CatchKeyword, SyntaxKind.FinallyKeyword,
        SyntaxKind.YieldKeyword,
    ];

    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Preview);

    public static IReadOnlyList<HighlightSpan> Highlight(string text)
    {
        var root = CSharpSyntaxTree.ParseText(text, ParseOptions).GetRoot();
        var variables = CollectVariableNames(root);
        var spans = new List<HighlightSpan>();

        foreach (var token in root.DescendantTokens(descendIntoTrivia: true))
        {
            AddTrivia(spans, token.LeadingTrivia);
            var cls = Classify(token, variables);
            if (cls != TokenClass.Plain && token.Span.Length > 0)
                spans.Add(new HighlightSpan(token.SpanStart, token.Span.Length, cls));
            AddTrivia(spans, token.TrailingTrivia);
        }

        // При одинаковом начале первым идёт более длинный span (trivia целиком, а не токен внутри неё).
        spans.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : b.Length.CompareTo(a.Length));
        return RemoveOverlaps(spans);
    }

    private static TokenClass Classify(SyntaxToken token, HashSet<string> variables)
    {
        var kind = token.Kind();
        if (ControlKeywords.Contains(kind))
            return TokenClass.ControlKeyword;
        if (SyntaxFacts.IsKeywordKind(kind))
            return TokenClass.Keyword;
        if (kind == SyntaxKind.NumericLiteralToken)
            return TokenClass.Number;
        if (IsStringToken(kind))
            return TokenClass.String;
        if (kind == SyntaxKind.IdentifierToken)
            return ClassifyIdentifier(token, variables);
        if (SyntaxFacts.IsPunctuation(kind))
            return TokenClass.Punctuation;
        return TokenClass.Plain;
    }

    private static TokenClass ClassifyIdentifier(SyntaxToken token, HashSet<string> variables)
    {
        var parent = token.Parent;
        switch (parent)
        {
            case RecordDeclarationSyntax record:
                return record.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword) ? TokenClass.Struct : TokenClass.Class;
            case ClassDeclarationSyntax or DelegateDeclarationSyntax or ConstructorDeclarationSyntax or DestructorDeclarationSyntax:
                return TokenClass.Class;
            case StructDeclarationSyntax:
                return TokenClass.Struct;
            case InterfaceDeclarationSyntax:
                return TokenClass.Interface;
            case EnumDeclarationSyntax:
                return TokenClass.Enum;
            case TypeParameterSyntax:
                return TokenClass.TypeParameter;
            case MethodDeclarationSyntax or LocalFunctionStatementSyntax:
                return TokenClass.Method;
            case ParameterSyntax or VariableDeclaratorSyntax { Parent.Parent: LocalDeclarationStatementSyntax or ForStatementSyntax or UsingStatementSyntax or FixedStatementSyntax }
                or SingleVariableDesignationSyntax or ForEachStatementSyntax or CatchDeclarationSyntax:
                return TokenClass.Variable;
            case SimpleNameSyntax name:
                return ClassifyName(name, variables);
        }

        return TokenClass.Plain;
    }

    private static TokenClass ClassifyName(SimpleNameSyntax name, HashSet<string> variables)
    {
        var text = name.Identifier.ValueText;

        // var, dynamic, nameof и т.п. — контекстные ключевые слова в виде идентификаторов.
        if (name is IdentifierNameSyntax && IsContextualKeywordName(name, text))
            return TokenClass.Keyword;

        if (IsInvoked(name))
            return TokenClass.Method;

        if (name.Parent is TypeParameterConstraintClauseSyntax)
            return TokenClass.TypeParameter;

        if (IsInTypeContext(name))
            return TypeClassFromName(text);

        // Console.WriteLine, Theme.Accent: идентификатор с заглавной буквы слева от точки считаем типом.
        if (name.Parent is MemberAccessExpressionSyntax access && access.Expression == name && char.IsUpper(text[0]))
            return TypeClassFromName(text);

        var isMemberName = name.Parent is MemberAccessExpressionSyntax m && m.Name == name;
        if (!isMemberName && variables.Contains(text))
            return TokenClass.Variable;

        return TokenClass.Plain;
    }

    private static bool IsContextualKeywordName(SimpleNameSyntax name, string text) => text switch
    {
        "var" or "dynamic" or "nint" or "nuint" => IsInTypeContext(name),
        "nameof" => name.Parent is InvocationExpressionSyntax,
        _ => false,
    };

    private static bool IsInvoked(SimpleNameSyntax name) => name.Parent switch
    {
        InvocationExpressionSyntax invocation => invocation.Expression == name,
        MemberAccessExpressionSyntax access => access.Name == name && access.Parent is InvocationExpressionSyntax inv && inv.Expression == access,
        MemberBindingExpressionSyntax binding => binding.Name == name && binding.Parent is InvocationExpressionSyntax,
        _ => false,
    };

    private static bool IsInTypeContext(SimpleNameSyntax name)
    {
        // Для A.B.C тип — вся квалифицированная цепочка, проверяем её корень.
        ExpressionSyntax node = name;
        while (node.Parent is QualifiedNameSyntax qualified && qualified.Right == node)
            node = qualified;

        if (node.Parent is AttributeSyntax)
            return true;
        if (node.Parent is UsingDirectiveSyntax or NamespaceDeclarationSyntax or FileScopedNamespaceDeclarationSyntax)
            return false;
        if (node.Parent is QualifiedNameSyntax)
            return false;

        return SyntaxFacts.IsInTypeOnlyContext(node)
               || node.Parent is ObjectCreationExpressionSyntax or TypeArgumentListSyntax or SimpleBaseTypeSyntax
                   or TypeConstraintSyntax or ArrayTypeSyntax or NullableTypeSyntax or TupleElementSyntax;
    }

    /// <summary>Интерфейсы по соглашению называются IName, параметры типа — T или TName.</summary>
    private static TokenClass TypeClassFromName(string text)
    {
        if (text.Length >= 2 && text[0] == 'I' && char.IsUpper(text[1]))
            return TokenClass.Interface;
        if (text == "T" || (text.Length >= 2 && text[0] == 'T' && char.IsUpper(text[1]) && !text.Skip(1).All(char.IsUpper)))
            return TokenClass.TypeParameter;
        return TokenClass.Class;
    }

    /// <summary>Имена параметров и локальных переменных, объявленных в тексте.</summary>
    private static HashSet<string> CollectVariableNames(SyntaxNode root)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in root.DescendantNodes())
        {
            var identifier = node switch
            {
                ParameterSyntax p => p.Identifier,
                VariableDeclaratorSyntax { Parent.Parent: LocalDeclarationStatementSyntax or ForStatementSyntax or UsingStatementSyntax } v => v.Identifier,
                SingleVariableDesignationSyntax d => d.Identifier,
                ForEachStatementSyntax f => f.Identifier,
                CatchDeclarationSyntax c => c.Identifier,
                _ => default,
            };
            if (identifier.ValueText.Length > 0)
                names.Add(identifier.ValueText);
        }

        return names;
    }

    private static void AddTrivia(List<HighlightSpan> spans, SyntaxTriviaList trivia)
    {
        foreach (var item in trivia)
        {
            var cls = item.Kind() switch
            {
                SyntaxKind.SingleLineCommentTrivia or SyntaxKind.MultiLineCommentTrivia
                    or SyntaxKind.SingleLineDocumentationCommentTrivia or SyntaxKind.MultiLineDocumentationCommentTrivia => TokenClass.Comment,
                SyntaxKind.DisabledTextTrivia => TokenClass.DisabledText,
                var k when SyntaxFacts.IsPreprocessorDirective(k) => TokenClass.Preprocessor,
                _ => TokenClass.Plain,
            };
            if (cls != TokenClass.Plain && item.Span.Length > 0)
                spans.Add(new HighlightSpan(item.SpanStart, item.Span.Length, cls));
        }
    }

    private static bool IsStringToken(SyntaxKind kind) => kind is
        SyntaxKind.StringLiteralToken or SyntaxKind.CharacterLiteralToken or SyntaxKind.Utf8StringLiteralToken
        or SyntaxKind.SingleLineRawStringLiteralToken or SyntaxKind.MultiLineRawStringLiteralToken
        or SyntaxKind.Utf8SingleLineRawStringLiteralToken or SyntaxKind.Utf8MultiLineRawStringLiteralToken
        or SyntaxKind.InterpolatedStringStartToken or SyntaxKind.InterpolatedStringEndToken
        or SyntaxKind.InterpolatedStringTextToken or SyntaxKind.InterpolatedSingleLineRawStringStartToken
        or SyntaxKind.InterpolatedMultiLineRawStringStartToken or SyntaxKind.InterpolatedRawStringEndToken;

    /// <summary>
    /// Токены внутри структурированной trivia (doc-комментарии, директивы) пересекаются со span самой trivia.
    /// Оставляем внешний span целиком.
    /// </summary>
    private static List<HighlightSpan> RemoveOverlaps(List<HighlightSpan> sorted)
    {
        var result = new List<HighlightSpan>(sorted.Count);
        var end = 0;
        foreach (var span in sorted)
        {
            if (span.Start < end)
                continue;
            result.Add(span);
            end = span.End;
        }

        return result;
    }
}
