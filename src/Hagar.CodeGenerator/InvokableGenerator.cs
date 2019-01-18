using System;
using System.Collections.Generic;
using System.Linq;
using Hagar.CodeGenerator.SyntaxGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Hagar.CodeGenerator
{
    internal static class InvokableGenerator
    {
        private const string SerializeMethodName = "Serialize";
        private const string DeserializeMethodName = "Deserialize";

        public static ClassDeclarationSyntax Generate(Compilation compilation, MethodDescription methodDescription)
        {
            var method = methodDescription.Method;
            var libraryTypes = LibraryTypes.FromCompilation(compilation);
            var simpleMethodName = GetSimpleMethodName(libraryTypes, method);

            var fieldDescriptions = GetFieldDescriptions(methodDescription.Method.Parameters.Select(p => p.Type).ToList(), libraryTypes);
            var fields = GetFieldDeclarations(fieldDescriptions);
            var ctor = GenerateConstructor(simpleMethodName, fieldDescriptions);
            
            var classDeclaration = ClassDeclaration(simpleMethodName)
                .AddBaseListTypes(SimpleBaseType(libraryTypes.Invokable.ToTypeSyntax()))
                .AddModifiers(Token(SyntaxKind.InternalKeyword), Token(SyntaxKind.SealedKeyword))
                .AddAttributeLists(AttributeList(SingletonSeparatedList(CodeGenerator.GetGeneratedCodeAttributeSyntax())))
                .AddMembers(fields)
                .AddMembers(ctor);

            if (method.TypeParameters.Length > 0)
            {
                classDeclaration = AddGenericTypeConstraints(classDeclaration, method);
            }

            return classDeclaration;
        }

        public static string GetSimpleMethodName(LibraryTypes libraryTypes, IMethodSymbol method)
        {
            var typeArgs = method.TypeParameters.Length > 0 ? "_" + method.TypeParameters.Length : string.Empty;
            var args = method.Parameters.Length > 0
                ? "_" + string.Join("_", method.Parameters.Select(p => p.Type.Name))
                : string.Empty;
            return $"{CodeGenerator.CodeGeneratorName}_Invokable_{method.ContainingType.Name}_{method.Name}{typeArgs}{args}";
        }

        private static ClassDeclarationSyntax AddGenericTypeConstraints(ClassDeclarationSyntax classDeclaration, IMethodSymbol method)
        {
            classDeclaration = classDeclaration.WithTypeParameterList(TypeParameterList(SeparatedList(method.TypeParameters.Select(tp => TypeParameter(tp.Name)))));
            var constraints = new List<TypeParameterConstraintSyntax>();
            foreach (var tp in method.TypeParameters)
            {
                constraints.Clear();
                if (tp.HasReferenceTypeConstraint)
                {
                    constraints.Add(ClassOrStructConstraint(SyntaxKind.ClassConstraint));
                }

                if (tp.HasValueTypeConstraint)
                {
                    constraints.Add(ClassOrStructConstraint(SyntaxKind.StructConstraint));
                }

                foreach (var c in tp.ConstraintTypes)
                {
                    constraints.Add(TypeConstraint(c.ToTypeSyntax()));
                }

                if (tp.HasConstructorConstraint)
                {
                    constraints.Add(ConstructorConstraint());
                }

                if (constraints.Count > 0)
                {
                    classDeclaration = classDeclaration.AddConstraintClauses(TypeParameterConstraintClause(tp.Name).AddConstraints(constraints.ToArray()));
                }
            }

            return classDeclaration;
        }

        private static MemberDeclarationSyntax[] GetFieldDeclarations(List<FieldDescription> fieldDescriptions)
        {
            return fieldDescriptions.Select(GetFieldDeclaration).ToArray();

            MemberDeclarationSyntax GetFieldDeclaration(FieldDescription description)
            {
                switch (description)
                {
                    case TypeFieldDescription type:
                        return FieldDeclaration(
                                VariableDeclaration(
                                    type.FieldType.ToTypeSyntax(),
                                    SingletonSeparatedList(VariableDeclarator(type.FieldName)
                                        .WithInitializer(EqualsValueClause(TypeOfExpression(type.UnderlyingType.ToTypeSyntax()))))))
                            .AddModifiers(Token(SyntaxKind.PrivateKeyword), Token(SyntaxKind.StaticKeyword), Token(SyntaxKind.ReadOnlyKeyword));
                    default:
                        return FieldDeclaration(VariableDeclaration(description.FieldType.ToTypeSyntax(), SingletonSeparatedList(VariableDeclarator(description.FieldName))))
                            .AddModifiers(Token(SyntaxKind.PrivateKeyword), Token(SyntaxKind.ReadOnlyKeyword));
                }
            }
        }

        private static ConstructorDeclarationSyntax GenerateConstructor(string simpleClassName, List<FieldDescription> fieldDescriptions)
        {
            var injected = fieldDescriptions.Where(f => f.IsInjected).ToList();
            var parameters = injected.Select(f => Parameter(f.FieldName.ToIdentifier()).WithType(f.FieldType.ToTypeSyntax()));
            var body = injected.Select(
                f => (StatementSyntax) ExpressionStatement(
                    AssignmentExpression(
                        SyntaxKind.SimpleAssignmentExpression,
                        ThisExpression().Member(f.FieldName.ToIdentifierName()),
                        Unwrapped(f.FieldName.ToIdentifierName()))));
            return ConstructorDeclaration(simpleClassName)
                .AddModifiers(Token(SyntaxKind.PublicKeyword))
                .AddParameterListParameters(parameters.ToArray())
                .AddBodyStatements(body.ToArray());

            ExpressionSyntax Unwrapped(ExpressionSyntax expr)
            {
                return InvocationExpression(
                    MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, IdentifierName("HagarGeneratedCodeHelper"), IdentifierName("UnwrapService")),
                    ArgumentList(SeparatedList(new [] {Argument(ThisExpression()), Argument(expr)})));
            }
        }

        private static List<FieldDescription> GetFieldDescriptions(List<ITypeSymbol> memberTypes, LibraryTypes libraryTypes)
        {
            var fields = new List<FieldDescription>();
            var distinct = memberTypes.Select(m => GetExpectedType(m)).Distinct();
            fields.AddRange(distinct.Select(GetTypeDescription));
            
            // Add a codec field for any field in the target which does not have a static codec.
            fields.AddRange(distinct
                .Where(t => !libraryTypes.StaticCodecs.Any(c => c.UnderlyingType.Equals(t)))
                .Select(GetCodecDescription));
            return fields;

            CodecFieldDescription GetCodecDescription(ITypeSymbol t)
            {
                var codecType = libraryTypes.FieldCodec.Construct(t);
                var fieldName = '_' + ToLowerCamelCase(t.GetValidIdentifier()) + "Codec";
                return new CodecFieldDescription(codecType, fieldName, t);
            }

            TypeFieldDescription GetTypeDescription(ITypeSymbol t)
            {
                var fieldName = ToLowerCamelCase(t.GetValidIdentifier()) + "Type";
                return new TypeFieldDescription(libraryTypes.Type, fieldName, t);
            }

            string ToLowerCamelCase(string input) => char.IsLower(input, 0) ? input : char.ToLowerInvariant(input[0]) + input.Substring(1);
        }

        /// <summary>
        /// Returns the "expected" type for <paramref name="type"/> which is used for selecting the correct codec.
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        private static ITypeSymbol GetExpectedType(ITypeSymbol type)
        {
            if (type is IArrayTypeSymbol)
                return type;
            if (type is IPointerTypeSymbol pointerType)
                throw new NotSupportedException($"Cannot serialize pointer type {pointerType.Name}");
            return type;
        }
        internal abstract class FieldDescription
        {
            protected FieldDescription(ITypeSymbol fieldType, string fieldName)
            {
                this.FieldType = fieldType;
                this.FieldName = fieldName;
            }

            public ITypeSymbol FieldType { get; }
            public string FieldName { get; }
            public abstract bool IsInjected { get; }
        }

        internal class InjectedFieldDescription : FieldDescription
        {
            public InjectedFieldDescription(ITypeSymbol fieldType, string fieldName) : base(fieldType, fieldName)
            {
            }

            public override bool IsInjected => true;
        }
        
        internal class CodecFieldDescription : FieldDescription, ICodecDescription
        {
            public CodecFieldDescription(ITypeSymbol fieldType, string fieldName, ITypeSymbol underlyingType) : base(fieldType, fieldName)
            {
                this.UnderlyingType = underlyingType;
            }

            public ITypeSymbol UnderlyingType { get; }
            public override bool IsInjected => true;
        }

        internal class TypeFieldDescription : FieldDescription
        {
            public TypeFieldDescription(ITypeSymbol fieldType, string fieldName, ITypeSymbol underlyingType) : base(fieldType, fieldName)
            {
                this.UnderlyingType = underlyingType;
            }

            public ITypeSymbol UnderlyingType { get; }
            public override bool IsInjected => false;
        }
    }
}
