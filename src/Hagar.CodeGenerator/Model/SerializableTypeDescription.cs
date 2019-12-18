using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Hagar.CodeGenerator.SyntaxGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hagar.CodeGenerator
{
    internal class SerializableTypeDescription : ISerializableTypeDescription
    {
        public SerializableTypeDescription(SemanticModel semanticModel, INamedTypeSymbol type, IEnumerable<IMemberDescription> members)
        {
            this.Type = type;
            this.Members = members.ToList();
            this.SemanticModel = semanticModel;
            this.TypeParameters = GetTypeParameters(type).ToImmutableArray();
        }

        private INamedTypeSymbol Type { get; }

        public TypeSyntax TypeSyntax => this.Type.ToTypeSyntax();
        public TypeSyntax UnboundTypeSyntax => this.Type.ToTypeSyntax();

        public bool HasComplexBaseType => !this.IsValueType &&
                                          this.Type.BaseType != null &&
                                          this.Type.BaseType.SpecialType != SpecialType.System_Object;

        public INamedTypeSymbol BaseType => this.Type.BaseType;

        public string Name => this.Type.Name;

        public bool IsValueType => this.Type.IsValueType;

        public bool IsGenericType => this.Type.IsGenericType;

        public ImmutableArray<ITypeParameterSymbol> TypeParameters { get; }

        public List<IMemberDescription> Members { get; }
        public SemanticModel SemanticModel { get; }

        private static IEnumerable<ITypeParameterSymbol> GetTypeParameters(INamedTypeSymbol type)
        {
            var baseType = type.BaseType;
            if (baseType != null && baseType.IsUnboundGenericType)
            {
                Console.WriteLine($"{type} has baseType {baseType} with unbound generic parameters");
                foreach (var t in GetTypeParameters(baseType)) yield return t;
            }

            foreach (var t in type.TypeParameters) yield return t;
        }
    }
}