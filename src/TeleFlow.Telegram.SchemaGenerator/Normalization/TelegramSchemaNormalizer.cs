using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using TeleFlow.Telegram.SchemaGenerator.Input;
using TeleFlow.Telegram.SchemaGenerator.Models;

namespace TeleFlow.Telegram.SchemaGenerator.Normalization;

internal static class TelegramSchemaNormalizer
{
    private static readonly Regex ReturnsRegex = new(@"Returns?\s+(?<type>.+?)(?:\.|$)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex WillReturnRegex = new(@"Will return\s+(?<type>.+?)(?:\.|$)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex OnSuccessReturnsRegex = new(@"On success,\s+returns?\s+(?<type>.+?)(?:\.|$)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex OnSuccessReturnedRegex = new(@"On success,\s+(?:(?:the|a|an)\s+)?(?<type>.+?)\s+is returned", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ConditionalOnSuccessReturnedRegex = new(@"On success,\s+if.+?,\s+(?:(?:the|a|an)\s+)?(?<first>.+?)\s+is returned,\s+otherwise\s+(?:(?:the|a|an)\s+)?(?<second>.+?)\s+is returned", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex[] LiteralRegexes =
    [
        new(@"\balways\s+[“""](?<value>[^”""]+)[”""]", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"\balways\s+(?<value>[A-Za-z0-9_]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"\bmust be\s+[“""](?<value>[^”""]+)[”""]", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"\bmust be\s+(?<value>[A-Za-z0-9_]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase)
    ];
    private static readonly HashSet<string> PrimitiveExpressions =
    [
        "Integer",
        "Int",
        "String",
        "Boolean",
        "Float",
        "True"
    ];

    public static NormalizedTelegramSchema Normalize(RawTelegramApiSnapshot raw)
    {
        var sections = raw.Categories
            .SelectMany(static category => category.Sections
                .Where(static section => section.Classification is "method" or "type")
                .Select(section => (Category: category, Section: section)))
            .ToArray();

        var typeSections = sections
            .Where(static item => item.Section.Classification == "type")
            .Select(static item => item.Section)
            .ToArray();
        var knownTypeNames = typeSections
            .Select(static section => ToPascalCase(section.Title))
            .ToHashSet(StringComparer.Ordinal);

        var types = typeSections
            .Select(section => NormalizeType(section, knownTypeNames))
            .OrderBy(static item => item.Name, StringComparer.Ordinal)
            .ToArray();
        types = FinalizeNamedUnionTypes(types);

        var methods = sections
            .Where(static item => item.Section.Classification == "method")
            .Select(item => NormalizeMethod(item.Section, knownTypeNames))
            .OrderBy(static item => item.Name, StringComparer.Ordinal)
            .ToArray();

        var abstractions = BuildAbstractions(types, methods, knownTypeNames);

        return new NormalizedTelegramSchema(
            SchemaMetadataFactory.CreateNormalized(raw.Metadata),
            types,
            methods,
            abstractions);
    }

    private static NormalizedTelegramType NormalizeType(RawTelegramSection section, IReadOnlySet<string> knownTypeNames)
    {
        var typeName = ToPascalCase(section.Title);
        var fieldTable = GetFirstTable(section, "Field");
        var properties = fieldTable is null
            ? Array.Empty<NormalizedTelegramProperty>()
            : fieldTable.Rows.Select(row => NormalizeField(row, typeName, knownTypeNames)).ToArray();

        var paragraphs = GetParagraphs(section);
        var summary = paragraphs.Length == 0 ? string.Empty : paragraphs[0];
        var remarks = GetRemarks(section);
        var unionMembers = ExtractUnionMembers(section, knownTypeNames);
        var unionCases = unionMembers
            .Select(member => BuildUnionCase(member, knownTypeNames, discriminatorProperty: null))
            .ToArray();

        return new NormalizedTelegramType(
            typeName,
            section.Anchor,
            summary,
            remarks,
            DetermineTypeKind(typeName, properties, unionMembers),
            properties.Length == 0,
            unionMembers,
            unionCases,
            null,
            null,
            properties);
    }

    private static NormalizedTelegramMethod NormalizeMethod(RawTelegramSection section, IReadOnlySet<string> knownTypeNames)
    {
        var parameterTable = GetFirstTable(section, "Parameter");
        var parameters = parameterTable is null
            ? Array.Empty<NormalizedTelegramProperty>()
            : parameterTable.Rows.Select(row => NormalizeParameter(row, knownTypeNames)).ToArray();

        var summaryBlock = GetSummaryBlock(section);
        var summary = summaryBlock?.Text ?? string.Empty;
        var resultExpression = ExtractResultExpression(summaryBlock, knownTypeNames);
        var rawResultType = resultExpression.Text;
        var resultType = MapTypeExpression(resultExpression, forMethodResult: true);
        var remarks = GetRemarks(section);

        return new NormalizedTelegramMethod(
            ToPascalCase(section.Title),
            section.Anchor,
            section.Title,
            summary,
            remarks,
            rawResultType,
            ToNormalizedExpression(resultExpression),
            resultType,
            parameters);
    }

    private static NormalizedTelegramProperty NormalizeField(RawTelegramRow row, string containingTypeName, IReadOnlySet<string> knownTypeNames)
    {
        var telegramName = row.Cells[0].Text;
        var rawType = row.Cells[1].Text;
        var description = row.Cells[2].Text;
        var required = !description.StartsWith("Optional.", StringComparison.OrdinalIgnoreCase);
        var expression = ParsePropertyExpression(rawType, description, knownTypeNames);

        return new NormalizedTelegramProperty(
            MakePropertyNameSafe(ToPascalCase(telegramName), containingTypeName),
            telegramName,
            expression.Text,
            ToNormalizedExpression(expression),
            ApplyOptionality(MapTypeExpression(expression), required),
            required,
            ExtractLiteralValue(telegramName, rawType, required, description),
            TrimOptionalPrefix(description));
    }

    private static NormalizedTelegramProperty NormalizeParameter(RawTelegramRow row, IReadOnlySet<string> knownTypeNames)
    {
        var telegramName = row.Cells[0].Text;
        var rawType = row.Cells[1].Text;
        var requiredCell = row.Cells[2].Text;
        var description = row.Cells.Count > 3 ? row.Cells[3].Text : string.Empty;
        var required = requiredCell.Equals("Yes", StringComparison.OrdinalIgnoreCase);
        var expression = ParsePropertyExpression(rawType, description, knownTypeNames);

        return new NormalizedTelegramProperty(
            ToPascalCase(telegramName),
            telegramName,
            expression.Text,
            ToNormalizedExpression(expression),
            ApplyOptionality(MapTypeExpression(expression), required),
            required,
            ExtractLiteralValue(telegramName, rawType, required, description),
            TrimOptionalPrefix(description));
    }

    private static TelegramTypeExpression ExtractResultExpression(RawTelegramBlock? summaryBlock, IReadOnlySet<string> knownTypeNames)
    {
        if (summaryBlock is null || string.IsNullOrWhiteSpace(summaryBlock.Text))
        {
            return new UnresolvedTelegramTypeExpression(string.Empty, "The method does not contain a summary paragraph.");
        }

        var summary = summaryBlock.Text;
        var conditionalMatch = ConditionalOnSuccessReturnedRegex.Match(summary);
        if (conditionalMatch.Success)
        {
            var first = ResolveResultTypeCandidate(conditionalMatch.Groups["first"].Value, knownTypeNames, summaryBlock);
            var second = ResolveResultTypeCandidate(conditionalMatch.Groups["second"].Value, knownTypeNames, summaryBlock);
            return BuildUnionExpression(first, second, knownTypeNames);
        }

        var successReturnsMatch = OnSuccessReturnsRegex.Match(summary);
        if (successReturnsMatch.Success)
        {
            return ResolveResultTypeCandidate(successReturnsMatch.Groups["type"].Value, knownTypeNames, summaryBlock);
        }

        var successMatch = OnSuccessReturnedRegex.Match(summary);
        if (successMatch.Success)
        {
            return ResolveResultTypeCandidate(successMatch.Groups["type"].Value, knownTypeNames, summaryBlock);
        }

        var returnMatches = ReturnsRegex.Matches(summary);
        if (returnMatches.Count > 0)
        {
            return ResolveResultTypeCandidate(returnMatches[^1].Groups["type"].Value, knownTypeNames, summaryBlock);
        }

        var willReturnMatch = WillReturnRegex.Match(summary);
        if (willReturnMatch.Success)
        {
            return ResolveResultTypeCandidate(willReturnMatch.Groups["type"].Value, knownTypeNames, summaryBlock);
        }

        return new UnresolvedTelegramTypeExpression(summary, "The method summary does not contain a recognized return type pattern.");
    }

    private static TelegramTypeExpression ParsePropertyExpression(
        string rawType,
        string description,
        IReadOnlySet<string> knownTypeNames)
    {
        if (IsUploadCapableString(rawType, description))
        {
            return TelegramTypeExpressionParser.Parse("InputFile or String", knownTypeNames);
        }

        return TelegramTypeExpressionParser.Parse(rawType, knownTypeNames);
    }

    private static bool IsUploadCapableString(string rawType, string description)
    {
        return rawType.Equals("String", StringComparison.OrdinalIgnoreCase) &&
               (description.Contains("attach://", StringComparison.OrdinalIgnoreCase) ||
                description.Contains("multipart/form-data", StringComparison.OrdinalIgnoreCase) ||
                description.Contains("More information on Sending Files", StringComparison.OrdinalIgnoreCase));
    }

    private static TelegramTypeExpression ResolveResultTypeCandidate(
        string candidate,
        IReadOnlySet<string> knownTypeNames,
        RawTelegramBlock summaryBlock)
    {
        candidate = CleanResultTypeCandidate(candidate);
        if (candidate.StartsWith("Array of ", StringComparison.OrdinalIgnoreCase))
        {
            var inner = candidate["Array of ".Length..].Trim();
            var innerExpression = ResolveBestNamedExpression(inner, knownTypeNames, summaryBlock);
            return innerExpression is UnresolvedTelegramTypeExpression unresolved
                ? unresolved with { SourceText = candidate }
                : new ArrayTelegramTypeExpression(innerExpression);
        }

        return ResolveBestNamedExpression(candidate, knownTypeNames, summaryBlock);
    }

    private static TelegramTypeExpression ResolveBestNamedExpression(
        string candidate,
        IReadOnlySet<string> knownTypeNames,
        RawTelegramBlock summaryBlock)
    {
        var linkedExpression = ResolveLinkedResultExpression(candidate, knownTypeNames, summaryBlock);
        if (linkedExpression is not null)
        {
            return linkedExpression;
        }

        if (candidate.Equals("True", StringComparison.OrdinalIgnoreCase))
        {
            return new PrimitiveTelegramTypeExpression("Boolean");
        }

        if (candidate.Contains(" as ", StringComparison.OrdinalIgnoreCase))
        {
            candidate = candidate[(candidate.LastIndexOf(" as ", StringComparison.OrdinalIgnoreCase) + 4)..];
        }

        candidate = candidate.Trim().Trim('.');

        var tokens = knownTypeNames
            .Concat(PrimitiveExpressions)
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(static token => token.Length)
            .ToArray();

        foreach (var token in tokens)
        {
            if (candidate.Equals(token, StringComparison.OrdinalIgnoreCase) ||
                candidate.Equals(token + " object", StringComparison.OrdinalIgnoreCase) ||
                candidate.Equals(token + " objects", StringComparison.OrdinalIgnoreCase))
            {
                return TelegramTypeExpressionParser.Parse(token, knownTypeNames);
            }
        }

        return TelegramTypeExpressionParser.Parse(candidate, knownTypeNames);
    }

    private static TelegramTypeExpression? ResolveLinkedResultExpression(
        string candidate,
        IReadOnlySet<string> knownTypeNames,
        RawTelegramBlock summaryBlock)
    {
        var linkedTypes = summaryBlock.Inlines
            .Where(static inline => inline.Kind == "link" && !string.IsNullOrWhiteSpace(inline.Text))
            .Select(static inline => inline.Text.Trim())
            .Where(knownTypeNames.Contains)
            .Where(typeName => candidate.Contains(typeName, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return linkedTypes.Length switch
        {
            0 => null,
            1 => new NamedTelegramTypeExpression(linkedTypes[0]),
            _ => new UnresolvedTelegramTypeExpression(
                candidate,
                "The method result candidate contains multiple linked Telegram types and requires an explicit extraction rule.")
        };
    }

    private static string CleanResultTypeCandidate(string candidate)
    {
        return candidate
            .Replace("objects", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("object", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("on success", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("is returned", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("are returned", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("a ", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("an ", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("the ", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("sent ", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();
    }

    private static TelegramTypeExpression BuildUnionExpression(
        TelegramTypeExpression first,
        TelegramTypeExpression second,
        IReadOnlySet<string> knownTypeNames)
    {
        if (first is UnresolvedTelegramTypeExpression unresolvedFirst)
        {
            return unresolvedFirst;
        }

        if (second is UnresolvedTelegramTypeExpression unresolvedSecond)
        {
            return unresolvedSecond;
        }

        return TelegramTypeExpressionParser.Parse($"{first.Text} or {second.Text}", knownTypeNames);
    }

    private static NormalizedTelegramAbstraction[] BuildAbstractions(
        NormalizedTelegramType[] types,
        NormalizedTelegramMethod[] methods,
        HashSet<string> knownTypeNames)
    {
        var typesByName = types.ToDictionary(static type => type.Name, StringComparer.Ordinal);
        var expressions = types
            .SelectMany(static type => type.Properties.SelectMany(static property => FlattenExpressions(property.TypeExpression)))
            .Concat(methods.SelectMany(static method => method.Parameters.SelectMany(static parameter => FlattenExpressions(parameter.TypeExpression))))
            .Concat(methods.SelectMany(static method => FlattenExpressions(method.ResultExpression)))
            .ToArray();

        var abstractions = new List<NormalizedTelegramAbstraction>
        {
            new("ITelegramApiMethod", "Represents a generated Telegram Bot API method request model.", "interface", null, "none", Array.Empty<string>(), Array.Empty<NormalizedTelegramUnionCase>()),
            new("TelegramApiResponse", "Represents a raw Telegram Bot API response envelope.", "response-envelope", null, "none", Array.Empty<string>(), Array.Empty<NormalizedTelegramUnionCase>())
        };

        foreach (var expression in expressions
                     .Where(static expression => expression.Kind == "union")
                     .GroupBy(static expression => expression.Text, StringComparer.Ordinal)
                     .Select(static group => group.First())
                     .OrderBy(static expression => expression.Text, StringComparer.Ordinal))
        {
            var members = expression.Members
                .Select(static member => NormalizeUnionMember(member.Text))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            abstractions.Add(new(
                ToAbstractionName(expression.Text, members),
                $"Represents the Telegram Bot API type expression '{expression.Text}'.",
                "union",
                expression.Text,
                DetermineValueShape(members, knownTypeNames),
                members,
                members
                    .Select(member => BuildUnionCase(member, knownTypeNames, discriminatorProperty: null))
                    .Select(unionCase => EnrichAnonymousUnionCase(unionCase, typesByName))
                    .ToArray()));
        }

        foreach (var expression in expressions
                     .Where(static expression => expression.Kind == "named")
                     .GroupBy(static expression => expression.Text, StringComparer.Ordinal)
                     .Select(static group => group.First())
                     .OrderBy(static expression => expression.Text, StringComparer.Ordinal))
        {
            if (PrimitiveExpressions.Contains(expression.Text) || knownTypeNames.Contains(expression.Text))
            {
                continue;
            }

            abstractions.Add(new(
                expression.Text,
                $"Represents the Telegram Bot API named type expression '{expression.Text}' for which the documentation does not provide a dedicated object section.",
                "placeholder-type",
                expression.Text,
                "placeholder",
                Array.Empty<string>(),
                Array.Empty<NormalizedTelegramUnionCase>()));
        }

        return abstractions
            .GroupBy(
                static abstraction => $"{abstraction.Kind}|{abstraction.Name}|{abstraction.RawExpression}|{abstraction.ValueShape}|{string.Join("|", abstraction.Members)}",
                StringComparer.Ordinal)
            .Select(static group => group.First())
            .OrderBy(static abstraction => abstraction.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<NormalizedTelegramExpression> FlattenExpressions(NormalizedTelegramExpression expression)
    {
        yield return expression;

        foreach (var member in expression.Members)
        {
            foreach (var nested in FlattenExpressions(member))
            {
                yield return nested;
            }
        }
    }

    private static string DetermineTypeKind(
        string typeName,
        NormalizedTelegramProperty[] properties,
        string[] unionMembers)
    {
        if (typeName == "InputFile")
        {
            return "pseudo-upload";
        }

        if (unionMembers.Length > 0)
        {
            return "named-union";
        }

        return properties.Length == 0 ? "marker-object" : "object";
    }

    private static NormalizedTelegramUnionCase BuildUnionCase(
        string rawMember,
        IReadOnlySet<string> knownTypeNames,
        string? discriminatorProperty)
    {
        var expression = TelegramTypeExpressionParser.Parse(rawMember, knownTypeNames);
        var normalizedExpression = ToNormalizedExpression(expression);
        var csharpType = MapTypeExpression(expression);

        return new NormalizedTelegramUnionCase(
            ToUnionCaseName(expression),
            rawMember,
            normalizedExpression,
            csharpType,
            DetermineUnionCaseMatchStrategy(expression, discriminatorProperty),
            discriminatorProperty,
            null,
            Array.Empty<string>());
    }

    private static string DetermineUnionCaseMatchStrategy(TelegramTypeExpression expression, string? discriminatorProperty)
    {
        return expression switch
        {
            PrimitiveTelegramTypeExpression primitive => primitive.Name switch
            {
                "String" => "string-token",
                "Integer" or "Int" => "integer-token",
                "Boolean" or "True" => "boolean-token",
                "Float" => "float-token",
                _ => "primitive-token"
            },
            ArrayTelegramTypeExpression => "array-token",
            NamedTelegramTypeExpression => discriminatorProperty is null ? "object" : "property-discriminator",
            _ => "object"
        };
    }

    private static string ToUnionCaseName(TelegramTypeExpression expression)
    {
        return expression switch
        {
            PrimitiveTelegramTypeExpression primitive => NormalizeUnionMember(primitive.Name),
            NamedTelegramTypeExpression named => named.Name,
            ArrayTelegramTypeExpression array => ToUnionCaseName(array.ElementType) + "Array",
            _ => ToPascalCase(expression.Text)
        };
    }

    private static string? ExtractLiteralValue(string telegramName, string rawType, bool required, string description)
    {
        if (rawType.Equals("String", StringComparison.OrdinalIgnoreCase) &&
            telegramName is "type" or "status" or "source")
        {
            return ExtractLiteralValueCore(description);
        }

        if ((rawType.Equals("Integer", StringComparison.OrdinalIgnoreCase) ||
             rawType.Equals("Int", StringComparison.OrdinalIgnoreCase)) &&
            telegramName == "date" &&
            Regex.IsMatch(description, @"\balways\s+0\b", RegexOptions.IgnoreCase))
        {
            return "0";
        }

        if (rawType.Equals("Boolean", StringComparison.OrdinalIgnoreCase) &&
            required &&
            telegramName is "force_reply" or "remove_keyboard" &&
            Regex.IsMatch(description, @"\b(always|must)\s+(be\s+)?True\b", RegexOptions.IgnoreCase))
        {
            return "true";
        }

        return null;
    }

    private static string? ExtractLiteralValueCore(string description)
    {
        foreach (var regex in LiteralRegexes)
        {
            var match = regex.Match(description);
            if (match.Success)
            {
                return match.Groups["value"].Value.Trim().Trim('.', ',', ';', ':');
            }
        }

        return null;
    }

    private static string[] ExtractUnionMembers(RawTelegramSection section, IReadOnlySet<string> knownTypeNames)
    {
        var paragraphs = GetParagraphs(section);
        foreach (var text in paragraphs)
        {
            var marker = GetUnionMarker(text);
            if (marker is null)
            {
                continue;
            }

            var index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                continue;
            }

            var suffix = text[(index + marker.Length)..]
                .Trim()
                .TrimEnd('.');

            var listMembers = section.Blocks
                .Where(static block => block.Kind is "unordered-list" or "ordered-list")
                .SelectMany(block => block.Items.Select(item => NormalizeUnionListItem(item, knownTypeNames)))
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .ToArray();

            if (listMembers.Length > 0)
            {
                return AddInlineUnionMembers(text, listMembers, knownTypeNames);
            }

            return suffix
                .Split([','], StringSplitOptions.RemoveEmptyEntries)
                .SelectMany(static part => part.Split([" or "], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Select(static part => part.Trim().TrimEnd('.'))
                .Where(static part => !string.IsNullOrWhiteSpace(part))
                .ToArray();
        }

        if (IsUnionLikeSection(section))
        {
            var listMembers = section.Blocks
                .Where(static block => block.Kind is "unordered-list" or "ordered-list")
                .SelectMany(block => block.Items.Select(item => NormalizeUnionListItem(item, knownTypeNames)))
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .ToArray();

            if (listMembers.Length > 0)
            {
                var paragraph = paragraphs.Length == 0 ? string.Empty : paragraphs[0];
                return AddInlineUnionMembers(paragraph, listMembers, knownTypeNames);
            }
        }

        return Array.Empty<string>();
    }

    private static string NormalizeUnionListItem(RawTelegramListItem item, IReadOnlySet<string> knownTypeNames)
    {
        var linkedType = item.Inlines
            .Where(static inline => inline.Kind == "link")
            .Select(static inline => inline.Text.Trim())
            .FirstOrDefault(knownTypeNames.Contains);

        return linkedType ?? item.Text.Trim().TrimEnd('.');
    }

    private static string[] AddInlineUnionMembers(
        string paragraph,
        string[] listMembers,
        IReadOnlySet<string> knownTypeNames)
    {
        var result = new List<string>();

        if (Regex.IsMatch(paragraph, @"\bString\b", RegexOptions.IgnoreCase))
        {
            result.Add("String");
        }

        var arrayMatches = Regex.Matches(paragraph, @"Array of\s+(?<type>[A-Z][A-Za-z0-9]*)", RegexOptions.IgnoreCase);
        foreach (Match match in arrayMatches)
        {
            var typeName = match.Groups["type"].Value;
            if (knownTypeNames.Contains(typeName))
            {
                result.Add("Array of " + typeName);
            }
        }

        result.AddRange(listMembers);
        return result.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static bool IsUnionLikeSection(RawTelegramSection section)
    {
        var text = string.Join(" ", GetParagraphs(section));
        return text.Contains("following", StringComparison.OrdinalIgnoreCase) &&
               (text.Contains("types", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("supported", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("can be", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("should be", StringComparison.OrdinalIgnoreCase));
    }

    private static string? GetUnionMarker(string text)
    {
        foreach (var marker in new[] { "it can be one of", "it should be one of" })
        {
            if (text.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return marker;
            }
        }

        return null;
    }

    private static NormalizedTelegramType[] FinalizeNamedUnionTypes(NormalizedTelegramType[] initialTypes)
    {
        var typesByName = initialTypes.ToDictionary(static type => type.Name, StringComparer.Ordinal);
        var finalizedTypes = new List<NormalizedTelegramType>(initialTypes.Length);

        foreach (var type in initialTypes)
        {
            if (type.UnionMembers.Count == 0)
            {
                finalizedTypes.Add(type);
                continue;
            }

            var strategy = DetermineNamedUnionStrategy(type, typesByName);
            var unionCases = type.UnionMembers
                .Select(member => BuildUnionCase(member, typesByName.Keys.ToHashSet(StringComparer.Ordinal), strategy.DiscriminatorProperty))
                .Select(unionCase => EnrichUnionCase(unionCase, strategy, typesByName))
                .ToArray();

            finalizedTypes.Add(type with
            {
                NamedUnionStrategy = strategy.Strategy,
                NamedUnionDiscriminatorProperty = strategy.DiscriminatorProperty,
                UnionCases = unionCases
            });
        }

        return finalizedTypes.ToArray();
    }

    private static (string Strategy, string? DiscriminatorProperty) DetermineNamedUnionStrategy(
        NormalizedTelegramType type,
        Dictionary<string, NormalizedTelegramType> typesByName)
    {
        if (type.Name == "MaybeInaccessibleMessage")
        {
            return ("maybe-inaccessible-message", null);
        }

        var memberTypes = type.UnionMembers
            .Where(typesByName.ContainsKey)
            .Select(memberName => typesByName[memberName])
            .ToArray();

        foreach (var propertyName in new[] { "type", "source" })
        {
            if (memberTypes.Length > 0 &&
                memberTypes.All(memberType => memberType.Properties.Any(property => property.TelegramName == propertyName && property.LiteralValue is not null)))
            {
                return ("property-discriminator", propertyName);
            }
        }

        foreach (var propertyName in new[] { "status" })
        {
            if (memberTypes.Length > 0 &&
                memberTypes.All(memberType => memberType.Properties.Any(property => property.TelegramName == propertyName && property.LiteralValue is not null)))
            {
                return ("property-discriminator", propertyName);
            }
        }

        if (memberTypes.Length > 0 && HasUniqueRequiredPropertySignatures(memberTypes))
        {
            return ("required-properties", null);
        }

        throw new InvalidOperationException(
            $"Named union type '{type.Name}' does not match a supported discriminator or required-property strategy.");
    }

    private static NormalizedTelegramUnionCase EnrichUnionCase(
        NormalizedTelegramUnionCase unionCase,
        (string Strategy, string? DiscriminatorProperty) strategy,
        Dictionary<string, NormalizedTelegramType> typesByName)
    {
        if (!typesByName.TryGetValue(unionCase.RawType, out var memberType))
        {
            return unionCase;
        }

        if (strategy.Strategy == "maybe-inaccessible-message")
        {
            return unionCase.RawType switch
            {
                "InaccessibleMessage" => unionCase with { MatchStrategy = "date-zero" },
                "Message" => unionCase with { MatchStrategy = "fallback-object" },
                _ => unionCase
            };
        }

        if (strategy.Strategy == "property-discriminator")
        {
            var discriminatorProperty = strategy.DiscriminatorProperty
                ?? throw new InvalidOperationException($"The union case '{unionCase.RawType}' is missing discriminator property metadata.");
            var discriminator = memberType.Properties.FirstOrDefault(property => property.TelegramName == discriminatorProperty);
            if (discriminator?.LiteralValue is null)
            {
                throw new InvalidOperationException(
                    $"The union case '{unionCase.RawType}' is missing literal value for discriminator property '{discriminatorProperty}'.");
            }

            return unionCase with
            {
                MatchStrategy = "property-discriminator",
                DiscriminatorProperty = discriminatorProperty,
                DiscriminatorValue = discriminator.LiteralValue,
                RequiredProperties = memberType.Properties
                    .Where(static property => property.Required)
                    .Select(static property => property.TelegramName)
                    .Order(StringComparer.Ordinal)
                    .ToArray()
            };
        }

        if (strategy.Strategy == "required-properties")
        {
            return unionCase with
            {
                MatchStrategy = "required-properties",
                RequiredProperties = memberType.Properties
                    .Where(static property => property.Required)
                    .Select(static property => property.TelegramName)
                    .Order(StringComparer.Ordinal)
                    .ToArray()
            };
        }

        if (unionCase.MatchStrategy == "object")
        {
            return unionCase with
            {
                MatchStrategy = "required-properties",
                RequiredProperties = memberType.Properties
                    .Where(static property => property.Required)
                    .Select(static property => property.TelegramName)
                    .Order(StringComparer.Ordinal)
                    .ToArray()
            };
        }

        return unionCase;
    }

    private static NormalizedTelegramUnionCase EnrichAnonymousUnionCase(
        NormalizedTelegramUnionCase unionCase,
        Dictionary<string, NormalizedTelegramType> typesByName)
    {
        if (!typesByName.TryGetValue(unionCase.RawType, out var memberType))
        {
            return unionCase;
        }

        if (memberType.Kind == "pseudo-upload")
        {
            return unionCase with { MatchStrategy = "upload-pseudo" };
        }

        foreach (var propertyName in new[] { "type", "source", "status" })
        {
            var discriminator = memberType.Properties.FirstOrDefault(property =>
                property.TelegramName == propertyName && property.LiteralValue is not null);
            if (discriminator is not null)
            {
                return unionCase with
                {
                    MatchStrategy = "property-discriminator",
                    DiscriminatorProperty = propertyName,
                    DiscriminatorValue = discriminator.LiteralValue,
                    RequiredProperties = memberType.Properties
                        .Where(static property => property.Required)
                        .Select(static property => property.TelegramName)
                        .Order(StringComparer.Ordinal)
                        .ToArray()
                };
            }
        }

        return unionCase with
        {
            MatchStrategy = "required-properties",
            RequiredProperties = memberType.Properties
                .Where(static property => property.Required)
                .Select(static property => property.TelegramName)
                .Order(StringComparer.Ordinal)
                .ToArray()
        };
    }

    private static bool HasUniqueRequiredPropertySignatures(IReadOnlyList<NormalizedTelegramType> memberTypes)
    {
        var signatures = memberTypes
            .Select(static memberType => string.Join(
                "|",
                memberType.Properties
                    .Where(static property => property.Required)
                    .Select(static property => property.TelegramName)
                    .Order(StringComparer.Ordinal)))
            .ToArray();

        return signatures.All(static signature => !string.IsNullOrWhiteSpace(signature)) &&
               signatures.Distinct(StringComparer.Ordinal).Count() == signatures.Length;
    }

    private static string MapTypeExpression(TelegramTypeExpression expression, bool forMethodResult = false)
    {
        var typeName = expression switch
        {
            PrimitiveTelegramTypeExpression primitive => primitive.Name switch
            {
                "Integer" or "Int" => "long",
                "String" => "string",
                "Boolean" or "True" => "bool",
                "Float" => "double",
                _ => "object"
            },
            NamedTelegramTypeExpression named => named.Name,
            ArrayTelegramTypeExpression array => $"IReadOnlyList<{MapTypeExpression(array.ElementType)}>",
            UnionTelegramTypeExpression union => ToAbstractionName(union.Text, union.Members.Select(static member => NormalizeUnionMember(member.Text)).ToArray()),
            UnresolvedTelegramTypeExpression => forMethodResult ? "object" : "object",
            _ => "object"
        };

        return typeName;
    }

    private static string NormalizeUnionMember(string member)
    {
        return member switch
        {
            "True" => "Boolean",
            _ => member
        };
    }

    private static string DetermineValueShape(IReadOnlyList<string> members, IReadOnlySet<string> knownTypeNames)
    {
        if (members.All(IsPrimitiveToken))
        {
            return "primitive-union";
        }

        if (members.All(knownTypeNames.Contains))
        {
            return "type-union";
        }

        return "mixed-union";
    }

    private static bool IsPrimitiveToken(string token)
    {
        return token is "Integer" or "String" or "Boolean" or "Float";
    }

    private static string ToAbstractionName(string expression, IReadOnlyList<string> members)
    {
        if (TelegramUnionNamingRegistry.TryGetSemanticAnonymousUnionName(expression, out var semanticName))
        {
            return semanticName;
        }

        if (expression.Equals("InlineKeyboardMarkup or ReplyKeyboardMarkup or ReplyKeyboardRemove or ForceReply", StringComparison.Ordinal))
        {
            return "ReplyMarkup";
        }

        var concatenatedName = string.Concat(members.Select(ToPascalCase));
        if (concatenatedName.Length <= 80)
        {
            return concatenatedName;
        }

        if (TryGetCommonMemberPrefix(members, out var commonPrefix))
        {
            return commonPrefix + "Union" + ComputeStableSuffix(expression);
        }

        return "TelegramUnion" + ComputeStableSuffix(expression);
    }

    private static bool TryGetCommonMemberPrefix(IReadOnlyList<string> members, out string commonPrefix)
    {
        commonPrefix = string.Empty;

        if (members.Count < 2)
        {
            return false;
        }

        var first = members[0];
        var prefixLength = first.Length;

        for (var index = 1; index < members.Count; index++)
        {
            prefixLength = GetCommonPrefixLength(first, members[index], prefixLength);
            if (prefixLength == 0)
            {
                return false;
            }
        }

        while (prefixLength > 0 && char.IsLower(first[prefixLength - 1]))
        {
            prefixLength--;
        }

        if (prefixLength == 0)
        {
            return false;
        }

        commonPrefix = first[..prefixLength];
        return true;
    }

    private static int GetCommonPrefixLength(string left, string right, int maxLength)
    {
        var length = Math.Min(Math.Min(left.Length, right.Length), maxLength);
        var index = 0;

        while (index < length && left[index] == right[index])
        {
            index++;
        }

        return index;
    }

    private static string ComputeStableSuffix(string expression)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(expression));
        return Convert.ToHexString(bytes[..3]);
    }

    private static NormalizedTelegramExpression ToNormalizedExpression(TelegramTypeExpression expression)
    {
        return expression switch
        {
            PrimitiveTelegramTypeExpression primitive => new NormalizedTelegramExpression(primitive.Kind, primitive.Text, Array.Empty<NormalizedTelegramExpression>()),
            NamedTelegramTypeExpression named => new NormalizedTelegramExpression(named.Kind, named.Text, Array.Empty<NormalizedTelegramExpression>()),
            ArrayTelegramTypeExpression array => new NormalizedTelegramExpression(array.Kind, array.Text, [ToNormalizedExpression(array.ElementType)]),
            UnionTelegramTypeExpression union => new NormalizedTelegramExpression(union.Kind, union.Text, union.Members.Select(ToNormalizedExpression).ToArray()),
            UnresolvedTelegramTypeExpression unresolved => new NormalizedTelegramExpression(unresolved.Kind, unresolved.Text, Array.Empty<NormalizedTelegramExpression>()),
            _ => new NormalizedTelegramExpression("unresolved", expression.Text, Array.Empty<NormalizedTelegramExpression>())
        };
    }

    private static RawTelegramTable? GetFirstTable(RawTelegramSection section, string firstHeader)
    {
        return section.Blocks
            .Where(static block => block.Kind == "table" && block.Table is not null)
            .Select(static block => block.Table)
            .FirstOrDefault(table => table!.Headers.Count > 0 && table.Headers[0].Text == firstHeader);
    }

    private static string[] GetParagraphs(RawTelegramSection section)
    {
        return section.Blocks
            .Where(static block => block.Kind == "paragraph")
            .Select(static block => block.Text)
            .Where(static text => !string.IsNullOrWhiteSpace(text))
            .ToArray();
    }

    private static RawTelegramBlock? GetSummaryBlock(RawTelegramSection section)
    {
        return section.Blocks.FirstOrDefault(static block => block.Kind == "paragraph");
    }

    private static string[] GetRemarks(RawTelegramSection section)
    {
        return section.Blocks
            .Where(static block => block.Kind is "blockquote" or "paragraph")
            .Skip(1)
            .Select(static block => block.Text)
            .Where(static text => !string.IsNullOrWhiteSpace(text))
            .Concat(section.Blocks
                .Where(static block => block.Kind is "unordered-list" or "ordered-list")
                .SelectMany(static block => block.Items.Select(static item => item.Text)))
            .Where(static text => !string.IsNullOrWhiteSpace(text))
            .ToArray();
    }

    private static string[] GetAllTexts(RawTelegramSection section)
    {
        return section.Blocks
            .Select(static block => block.Text)
            .Concat(section.Blocks.SelectMany(static block => block.Items.Select(static item => item.Text)))
            .Where(static text => !string.IsNullOrWhiteSpace(text))
            .ToArray();
    }

    private static string MakePropertyNameSafe(string propertyName, string containingTypeName)
    {
        return propertyName.Equals(containingTypeName, StringComparison.Ordinal)
            ? propertyName + "Value"
            : propertyName;
    }

    private static string TrimOptionalPrefix(string description)
    {
        return description.StartsWith("Optional.", StringComparison.OrdinalIgnoreCase)
            ? description["Optional.".Length..].TrimStart()
            : description;
    }

    private static string ApplyOptionality(string typeName, bool required)
    {
        if (required)
        {
            return typeName;
        }

        return typeName switch
        {
            "long" => "long?",
            "bool" => "bool?",
            "double" => "double?",
            "string" => "string?",
            "object" => "object?",
            _ => $"{typeName}?"
        };
    }

    private static string ToPascalCase(string value)
    {
        var parts = value.Split(['_', '-', ' '], StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(parts.Select(static part => char.ToUpperInvariant(part[0]) + part[1..]));
    }
}
