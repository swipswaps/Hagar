using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hagar.CodeGenerator.SyntaxGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Hagar.CodeGenerator
{
    internal interface IMemberDescription
    {
        uint FieldId { get; }
        ISymbol Member { get; }
        ITypeSymbol Type { get; }
    }

    internal class FieldDescription : IMemberDescription
    {
        public FieldDescription(uint fieldId, IFieldSymbol field)
        {
            this.FieldId = fieldId;
            this.Field = field;
        }

        public IFieldSymbol Field { get; }
        public uint FieldId { get; }
        public ISymbol Member => this.Field;
        public ITypeSymbol Type => this.Field.Type;
    }

    internal class PropertyDescription : IMemberDescription
    {
        public PropertyDescription(uint fieldId, IPropertySymbol property)
        {
            this.FieldId = fieldId;
            this.Property = property;
        }

        public uint FieldId { get; }
        public ISymbol Member => this.Property;
        public ITypeSymbol Type => this.Property.Type;
        public IPropertySymbol Property { get; }
    }

    internal interface ITypeDescription
    {
        TypeSyntax TypeSyntax { get; }
        bool HasComplexBaseType { get; }
        INamedTypeSymbol BaseType { get; }
        string Name { get; }
        bool IsValueType { get; }
        bool IsGenericType { get; }
        ImmutableArray<ITypeParameterSymbol> TypeParameters { get; }
        List<IMemberDescription> Members { get; }
    }

    internal class TypeDescription : ITypeDescription
    {
        public TypeDescription(INamedTypeSymbol type, IEnumerable<IMemberDescription> members)
        {
            this.Type = type;
            this.Members = members.ToList();
        }

        private INamedTypeSymbol Type { get; }

        public TypeSyntax TypeSyntax => this.Type.ToTypeSyntax();

        public bool HasComplexBaseType => !this.IsValueType &&
                                          this.Type.BaseType != null &&
                                          this.Type.BaseType.SpecialType != SpecialType.System_Object;

        public INamedTypeSymbol BaseType => this.Type.BaseType;

        public string Name => this.Type.Name;

        public bool IsValueType => this.Type.IsValueType;

        public bool IsGenericType => this.Type.IsGenericType;

        public ImmutableArray<ITypeParameterSymbol> TypeParameters => this.Type.TypeParameters;

        public List<IMemberDescription> Members { get; }
    }

    internal class MethodDescription
    {
        public MethodDescription(IMethodSymbol method)
        {
            this.Method = method;
        }

        public IMethodSymbol Method { get; }
    }

    public class CodeGenerator
    {
        internal const string CodeGeneratorName = "HagarGen";
        private readonly Compilation compilation;
        private readonly LibraryTypes libraryTypes;

        public CodeGenerator(Compilation compilation)
        {
            this.compilation = compilation;
            this.libraryTypes = LibraryTypes.FromCompilation(compilation);
        }

        public async Task<CompilationUnitSyntax> GenerateCode(CancellationToken cancellationToken)
        {
            // Collect metadata from the compilation.
            var serializableTypes = await this.GetSerializableTypes(cancellationToken);

            // Generate code.
            var members = new List<MemberDeclarationSyntax>();
            foreach (var type in serializableTypes)
            {
                // Generate a partial serializer class for each serializable type.
                members.Add(SerializerGenerator.GenerateSerializer(this.compilation, this.libraryTypes, type));
            }

            var namespaceName = "HagarGeneratedCode." + this.compilation.AssemblyName;

            // Generate metadata.
            var metadataClass = MetadataGenerator.GenerateMetadata(this.compilation, serializableTypes);
            members.Add(metadataClass);

            var metadataAttribute = AttributeList()
                .WithTarget(AttributeTargetSpecifier(Token(SyntaxKind.AssemblyKeyword)))
                .WithAttributes(
                    SingletonSeparatedList(
                        Attribute(this.libraryTypes.MetadataProviderAttribute.ToNameSyntax())
                            .AddArgumentListArguments(AttributeArgument(TypeOfExpression(ParseTypeName($"{namespaceName}.{metadataClass.Identifier.Text}"))))));

            return CompilationUnit()
                .WithAttributeLists(List(new []{metadataAttribute}))
                .WithMembers(
                    SingletonList<MemberDeclarationSyntax>(
                        NamespaceDeclaration(ParseName(namespaceName))
                        .WithMembers(List(members))
                        .WithUsings(List(new[] {UsingDirective(ParseName("global::Hagar.Codecs")), UsingDirective(ParseName("global::Hagar.GeneratedCodeHelpers")) }))));
        }

        private async Task<List<TypeDescription>> GetSerializableTypes(CancellationToken cancellationToken)
        {
            var results = new List<TypeDescription>(1024);
            foreach (var syntaxTree in this.compilation.SyntaxTrees)
            {
                var semanticModel = this.compilation.GetSemanticModel(syntaxTree, ignoreAccessibility: false);
                var rootNode = await syntaxTree.GetRootAsync(cancellationToken);
                foreach (var node in GetTypeDeclarations(rootNode))
                {
                    if (!this.HasAttribute(node, semanticModel, this.libraryTypes.GenerateSerializerAttribute)) continue;
                    results.Add(this.CreateTypeDescription(semanticModel, node));
                }
            }

            return results;
        }

        private static IEnumerable<TypeDeclarationSyntax> GetTypeDeclarations(SyntaxNode node)
        {
            SyntaxList<MemberDeclarationSyntax> members;
            switch (node)
            {
                case TypeDeclarationSyntax type:
                    yield return type;
                    members = type.Members;
                    break;
                case NamespaceDeclarationSyntax ns:
                    members = ns.Members;
                    break;
                case CompilationUnitSyntax compilationUnit:
                    members = compilationUnit.Members;
                    break;
                default:
                    yield break;
            }

            foreach (var member in members)
            {
                foreach (var decl in GetTypeDeclarations(member))
                {
                    yield return decl;
                }
            }
        }

        private TypeDescription CreateTypeDescription(SemanticModel semanticModel, TypeDeclarationSyntax typeDecl)
        {
            var declared = semanticModel.GetDeclaredSymbol(typeDecl);
            var typeDescription = new TypeDescription(declared, this.GetMembers(declared));
            return typeDescription;
        }

        // Returns descriptions of all fields 
        private IEnumerable<IMemberDescription> GetMembers(INamedTypeSymbol symbol)
        {
            foreach (var member in symbol.GetMembers())
            {
                // Only consider fields and properties.
                if (!(member is IFieldSymbol || member is IPropertySymbol)) continue;

                var idAttr = member.GetAttributes().SingleOrDefault(attr => attr.AttributeClass.Equals(this.libraryTypes.IdAttribute));
                if (idAttr == null) continue;
                var id = (uint)idAttr.ConstructorArguments.First().Value;

                if (member is IPropertySymbol prop)
                {
                    if (prop.IsReadOnly || prop.IsWriteOnly)
                    {
#warning add diagnostic: not read/write property.
                        continue;
                    }

                    yield return new PropertyDescription(id, prop);
                }

                if (member is IFieldSymbol field)
                {
                    if (field.IsConst || field.IsReadOnly)
                    {
#warning add diagnostic: readonly field.
                        continue;
                    }
                    
                    yield return new FieldDescription(id, field);
                }
            }
        }

        // Returns true if the type declaration has the specified attribute.
        private bool HasAttribute(TypeDeclarationSyntax node, SemanticModel model, ISymbol attributeType)
        {
            switch (node)
            {
                case ClassDeclarationSyntax classDecl:
                    return HasAttributeInner(classDecl.AttributeLists);
                case StructDeclarationSyntax structDecl:
                    return HasAttributeInner(structDecl.AttributeLists);
                default:
                    return false;
            }

            bool HasAttributeInner(SyntaxList<AttributeListSyntax> attributeLists)
            {
                return attributeLists
                    .SelectMany(list => list.Attributes)
                    .Select(attr => model.GetTypeInfo(attr).ConvertedType)
                    .Any(attrType => attrType.Equals(attributeType));
            }
        }

        internal static AttributeSyntax GetGeneratedCodeAttributeSyntax()
        {
            var version = typeof(CodeGenerator).Assembly.GetName().Version.ToString();
            return
                Attribute(ParseName("System.CodeDom.Compiler.GeneratedCodeAttribute"))
                    .AddArgumentListArguments(
                        AttributeArgument(CodeGeneratorName.GetLiteralExpression()),
                        AttributeArgument(version.GetLiteralExpression()));
        }
    }
}