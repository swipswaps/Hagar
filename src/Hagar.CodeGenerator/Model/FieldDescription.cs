using Microsoft.CodeAnalysis;

namespace Hagar.CodeGenerator
{
    internal class FieldDescription : IFieldDescription
    {
        public FieldDescription(ushort fieldId, IFieldSymbol field)
        {
            this.FieldId = fieldId;
            this.Field = field;
        }
        public IFieldSymbol Field { get; }
        public ushort FieldId { get; }
        public ISymbol Member => this.Field;
        public ITypeSymbol Type => this.Field.Type;
        public string Name => this.Field.Name;
    }

    internal interface IFieldDescription : IMemberDescription { }
}