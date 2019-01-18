using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Hagar.CodeGenerator.SyntaxGeneration
{
    internal static class SymbolExtensions
    {
        public static TypeSyntax ToTypeSyntax(this ITypeSymbol typeSymbol)
        {
            return ParseTypeName(typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        }

        public static TypeSyntax ToTypeSyntax(this ITypeSymbol typeSymbol, params TypeSyntax[] genericParameters)
        {
            var displayString = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (!(ParseTypeName(displayString) is GenericNameSyntax generic))
                throw new InvalidOperationException(
                    $"Attempted to add generic parameters to non-generic type {displayString}, adding parameters {string.Join(", ", genericParameters.Select(n => n.ToFullString()))}");
            return generic.WithTypeArgumentList(TypeArgumentList(SeparatedList(genericParameters)));
        }

        public static NameSyntax ToNameSyntax(this ITypeSymbol typeSymbol)
        {
            return ParseName(typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        }

        public static string GetValidIdentifier(this ITypeSymbol type)
        {
            switch (type)
            {
                case INamedTypeSymbol named when !named.IsGenericType: return $"{named.Name}";
                case INamedTypeSymbol named:
                    return $"{named.Name}_{string.Join("_", named.TypeArguments.Select(GetValidIdentifier))}";
                case IArrayTypeSymbol array:
                    return $"{GetValidIdentifier(array.ElementType)}_{array.Rank}";
                case IPointerTypeSymbol pointer:
                    return $"{GetValidIdentifier(pointer.PointedAtType)}_ptr";
                case ITypeParameterSymbol parameter:
                    return $"{parameter.Name}";
                default:
                    throw new NotSupportedException($"Unable to format type of kind {type.GetType()} with name \"{type.Name}\"");
            }
        }
    }
}
