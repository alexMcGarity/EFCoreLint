using Microsoft.CodeAnalysis;

namespace EFCoreLint;

internal static class EFCoreTypeHelpers
{
    internal static bool ImplementsIQueryable(ITypeSymbol? type)
    {
        if (type is null)
            return false;

        if (IsIQueryable(type))
            return true;

        foreach (var iface in type.AllInterfaces)
        {
            if (IsIQueryable(iface))
                return true;
        }

        return false;
    }

    internal static bool IsDbContextDerived(ITypeSymbol? type)
    {
        var current = type;
        while (current is not null)
        {
            if (current.Name == "DbContext" &&
                current.ContainingNamespace?.ToDisplayString() == "Microsoft.EntityFrameworkCore")
                return true;
            current = current.BaseType;
        }
        return false;
    }

    private static bool IsIQueryable(ITypeSymbol type) =>
        type.ContainingNamespace?.ToDisplayString() == "System.Linq" &&
        type.Name is "IQueryable" or "IOrderedQueryable";
}
